using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    public enum InstinctParam : byte
    {
        Reaction,       // seconds before reacting to a telegraph (all roles)
        Peel,           // tank: weight on enemies hitting squadmates
        TauntLoose,     // tank: loose enemies before Taunt Roar
        Leash,          // tank: how far it leaves its slot to engage
        HealThreshold,  // mender: heal allies below this health fraction
        TankBias,       // mender: extra priority for the tank
        KeepDistance,   // mender / siegewright: preferred distance from enemies
        FieldMin,       // mender: wounded allies needed for Regen Field
        FlankAngle,     // bruiser: degrees off the tank's line when attacking
        FocusLow,       // bruiser / siegewright: preference for wounded targets
        AoeMin,         // bruiser / warwright: enemies needed for area abilities
        Retreat,        // bruiser / warwright: health fraction that triggers backing off
        TurretForward,  // siegewright: metres toward the enemy turrets get placed
        HoldBurst,      // siegewright: tendency to save Burst Volley for big moments
        WallDistance,   // warwright: metres in front of the protected ally ramparts go up
        TrapLead,       // warwright: seconds of lead when predicting where enemies will run
        Reserve,        // all roles: share of mana and stamina kept back for the moments that matter
    }

    public sealed class InstinctParamDef
    {
        public InstinctParam Param;
        public float Min;
        public float Max;
        public float Default;
        public string MoreText;
        public string LessText;
    }

    /// <summary>
    /// Real learning, bounded by Doctrine. Each unit keeps a few tunable numbers per
    /// (command, enemy family). During a fight it tries a small random perturbation; afterwards
    /// the perturbation is kept or reversed depending on whether the unit did better than its
    /// running baseline. This is a (1+1) evolution strategy with antithetic steps, as the design
    /// doc suggests: no neural networks, a few dozen numbers per unit, updated between fights.
    /// </summary>
    public sealed class Instinct
    {
        private static readonly Dictionary<Role, InstinctParamDef[]> RoleParams = new Dictionary<Role, InstinctParamDef[]>();
        private static readonly float[] TierWidth = { 0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f };

        public sealed class Set
        {
            public float[] Values;
            public float Baseline;
            public int Samples;
        }

        public readonly Role Role;
        public readonly InstinctParamDef[] Params;
        public bool Locked;
        private readonly Dictionary<int, Set> _sets = new Dictionary<int, Set>();
        private readonly Dictionary<int, float[]> _delta = new Dictionary<int, float[]>();
        private readonly Dictionary<int, int> _deltaTier = new Dictionary<int, int>();

        public Instinct(Role role)
        {
            Role = role;
            Params = RoleParams.TryGetValue(role, out var p) ? p : new InstinctParamDef[0];
        }

        public static InstinctParamDef[] ParamsFor(Role role) =>
            RoleParams.TryGetValue(role, out var p) ? p : new InstinctParamDef[0];

        public static int Key(CommandType c, EnemyFamily f) => (int)c * 16 + (int)f;

        public static float ReactionFloor(int tier) => 1.1f - 0.15f * tier;

        public void Bounds(InstinctParamDef def, int tier, out float lo, out float hi)
        {
            tier = KMath.Clamp(tier, 0, 5);
            if (def.Param == InstinctParam.Reaction)
            {
                lo = ReactionFloor(tier);
                hi = lo + 0.6f;
                return;
            }
            float w = TierWidth[tier];
            lo = def.Default - w * (def.Default - def.Min);
            hi = def.Default + w * (def.Max - def.Default);
        }

        private float DefaultFor(InstinctParamDef def, int tier) =>
            def.Param == InstinctParam.Reaction ? ReactionFloor(tier) + 0.25f : def.Default;

        public int IndexOf(InstinctParam p)
        {
            for (int i = 0; i < Params.Length; i++)
            {
                if (Params[i].Param == p) return i;
            }
            return -1;
        }

        public Set GetSet(int key)
        {
            if (!_sets.TryGetValue(key, out var s))
            {
                s = new Set { Values = new float[Params.Length] };
                for (int i = 0; i < Params.Length; i++) s.Values[i] = Params[i].Param == InstinctParam.Reaction ? float.NaN : Params[i].Default;
                _sets[key] = s;
            }
            return s;
        }

        public bool HasSet(int key) => _sets.ContainsKey(key);

        /// <summary>Learned value (without this fight's experiment), clamped to Doctrine bounds.</summary>
        public float Learned(CommandType c, EnemyFamily f, InstinctParam p, int tier)
        {
            int i = IndexOf(p);
            if (i < 0) return 0f;
            var def = Params[i];
            var set = GetSet(Key(c, f));
            float v = float.IsNaN(set.Values[i]) ? DefaultFor(def, tier) : set.Values[i];
            Bounds(def, tier, out float lo, out float hi);
            return KMath.Clamp(v, lo, hi);
        }

        /// <summary>Value the unit actually fights with right now: learned value plus the current experiment.</summary>
        public float Get(CommandType c, EnemyFamily f, InstinctParam p, int tier, SimRng rng, bool inFight)
        {
            int i = IndexOf(p);
            if (i < 0) return 0f;
            var def = Params[i];
            int key = Key(c, f);
            float v = Learned(c, f, p, tier);
            if (inFight && !Locked && rng != null)
            {
                if (!_delta.TryGetValue(key, out var d))
                {
                    d = new float[Params.Length];
                    for (int k = 0; k < Params.Length; k++)
                    {
                        Bounds(Params[k], tier, out float l, out float h);
                        d[k] = rng.Gaussian() * Balance.InstinctSigma * (h - l) * 0.5f;
                    }
                    _delta[key] = d;
                    _deltaTier[key] = tier;
                }
                v += d[i];
            }
            Bounds(def, tier, out float lo, out float hi);
            return KMath.Clamp(v, lo, hi);
        }

        public void BeginFight()
        {
            _delta.Clear();
            _deltaTier.Clear();
        }

        /// <summary>
        /// Applies the experiment result. Returns plain-language notes about meaningful changes,
        /// e.g. "keeps more distance vs Cratermaw".
        /// </summary>
        public List<string> EndFight(float score)
        {
            var notes = new List<string>();
            if (Locked)
            {
                _delta.Clear();
                return notes;
            }
            foreach (var kv in _delta)
            {
                int key = kv.Key;
                float[] d = kv.Value;
                int tier = _deltaTier[key];
                var set = GetSet(key);
                var family = (EnemyFamily)(key % 16);
                if (set.Samples == 0)
                {
                    set.Baseline = score;
                    set.Samples = 1;
                    // First fight against this family: keep half the experiment so personalities start to form.
                    ApplyStep(set, d, tier, 0.5f, family, notes, false);
                    continue;
                }
                float adv = score - set.Baseline;
                float step = KMath.Clamp(adv / 0.5f, -1f, 1f) * Balance.InstinctLearningRate;
                ApplyStep(set, d, tier, step, family, notes, true);
                set.Baseline += Balance.InstinctBaselineRate * (score - set.Baseline);
                set.Samples++;
            }
            _delta.Clear();
            _deltaTier.Clear();
            return notes;
        }

        private void ApplyStep(Set set, float[] d, int tier, float step, EnemyFamily family, List<string> notes, bool report)
        {
            float bestRel = 0f;
            int bestIdx = -1;
            float bestDir = 0f;
            for (int i = 0; i < Params.Length; i++)
            {
                var def = Params[i];
                Bounds(def, tier, out float lo, out float hi);
                float before = float.IsNaN(set.Values[i]) ? DefaultFor(def, tier) : set.Values[i];
                before = KMath.Clamp(before, lo, hi);
                float after = KMath.Clamp(before + step * d[i], lo, hi);
                set.Values[i] = after;
                float rel = MathF.Abs(after - before) / MathF.Max(0.0001f, hi - lo);
                if (rel > bestRel)
                {
                    bestRel = rel;
                    bestIdx = i;
                    bestDir = after - before;
                }
            }
            if (report && bestIdx >= 0 && bestRel > 0.05f)
            {
                var def = Params[bestIdx];
                string phrase = bestDir > 0 ? def.MoreText : def.LessText;
                string vs = family == EnemyFamily.None ? "" : " vs " + FamilyName(family);
                notes.Add(phrase + vs);
            }
        }

        public void ResetCommand(CommandType c)
        {
            var remove = new List<int>();
            foreach (var key in _sets.Keys)
            {
                if (key / 16 == (int)c) remove.Add(key);
            }
            foreach (var k in remove) _sets.Remove(k);
        }

        /// <summary>Copies part of a veteran's instinct (Drill Grounds style).</summary>
        public void CopyFrom(Instinct other, float fraction)
        {
            if (other == null || other.Role != Role) return;
            foreach (var kv in other._sets)
            {
                var mine = GetSet(kv.Key);
                for (int i = 0; i < mine.Values.Length; i++)
                {
                    float o = kv.Value.Values[i];
                    if (float.IsNaN(o)) continue;
                    float m = float.IsNaN(mine.Values[i]) ? Params[i].Default : mine.Values[i];
                    mine.Values[i] = KMath.Lerp(m, o, fraction);
                }
            }
        }

        public IEnumerable<KeyValuePair<int, Set>> Sets => _sets;

        public static string FamilyName(EnemyFamily f)
        {
            switch (f)
            {
                case EnemyFamily.Cratermaw: return "Cratermaws";
                case EnemyFamily.Rockback: return "Rockbacks";
                case EnemyFamily.Drone: return "drones";
                case EnemyFamily.Scrap: return "scrap hounds";
                case EnemyFamily.Rustmaw: return "Rustmaw";
            }
            return "anything";
        }

        static Instinct()
        {
            var reaction = new InstinctParamDef { Param = InstinctParam.Reaction, Min = 0.35f, Max = 1.7f, Default = 1f, MoreText = "reacts slower to tells", LessText = "reacts faster to tells" };

            RoleParams[Role.Tank] = new[]
            {
                reaction,
                new InstinctParamDef { Param = InstinctParam.Peel, Min = 0f, Max = 45f, Default = 15f, MoreText = "peels enemies off squadmates sooner", LessText = "sticks to its own target more" },
                new InstinctParamDef { Param = InstinctParam.TauntLoose, Min = 1f, Max = 4f, Default = 1.5f, MoreText = "saves Taunt Roar for bigger crowds", LessText = "roars at the first loose enemy" },
                new InstinctParamDef { Param = InstinctParam.Leash, Min = 1.5f, Max = 8f, Default = 4f, MoreText = "steps further out to meet enemies", LessText = "holds closer to the line" },
                new InstinctParamDef { Param = InstinctParam.Retreat, Min = 0.08f, Max = 0.45f, Default = 0.18f, MoreText = "rotates back earlier when hurt", LessText = "stays in front longer when hurt" },
                Reserve(0.2f, 0.5f),
            };
            RoleParams[Role.Healer] = new[]
            {
                reaction,
                new InstinctParamDef { Param = InstinctParam.HealThreshold, Min = 0.5f, Max = 0.95f, Default = 0.78f, MoreText = "heals earlier", LessText = "waits longer before healing" },
                new InstinctParamDef { Param = InstinctParam.TankBias, Min = 0f, Max = 35f, Default = 10f, MoreText = "prioritises the tank more", LessText = "spreads heals more evenly" },
                new InstinctParamDef { Param = InstinctParam.KeepDistance, Min = 5f, Max = 14f, Default = 9f, MoreText = "keeps more distance", LessText = "stays closer to the fight" },
                new InstinctParamDef { Param = InstinctParam.FieldMin, Min = 1f, Max = 4f, Default = 2f, MoreText = "saves Regen Field for bigger heals", LessText = "drops Regen Field sooner" },
                Reserve(0.3f, 0.6f),
            };
            RoleParams[Role.Melee] = new[]
            {
                reaction,
                new InstinctParamDef { Param = InstinctParam.FlankAngle, Min = 0f, Max = 85f, Default = 35f, MoreText = "flanks wider", LessText = "attacks closer to the tank's line" },
                new InstinctParamDef { Param = InstinctParam.FocusLow, Min = 0f, Max = 40f, Default = 12f, MoreText = "goes after wounded targets more", LessText = "spreads damage more" },
                new InstinctParamDef { Param = InstinctParam.AoeMin, Min = 1f, Max = 4f, Default = 2f, MoreText = "saves Cleave for bigger groups", LessText = "cleaves smaller groups" },
                new InstinctParamDef { Param = InstinctParam.Retreat, Min = 0.08f, Max = 0.5f, Default = 0.22f, MoreText = "backs off earlier when hurt", LessText = "fights on longer when hurt" },
                Reserve(0.15f, 0.45f),
            };
            RoleParams[Role.Siege] = new[]
            {
                reaction,
                new InstinctParamDef { Param = InstinctParam.KeepDistance, Min = 8f, Max = 18f, Default = 13f, MoreText = "keeps more distance", LessText = "fights from closer in" },
                new InstinctParamDef { Param = InstinctParam.FocusLow, Min = 0f, Max = 40f, Default = 12f, MoreText = "opens on wounded targets faster", LessText = "spreads fire more" },
                new InstinctParamDef { Param = InstinctParam.TurretForward, Min = -4f, Max = 6f, Default = 0f, MoreText = "pushes turrets further forward", LessText = "builds its turrets further back" },
                new InstinctParamDef { Param = InstinctParam.HoldBurst, Min = 0f, Max = 1f, Default = 0.3f, MoreText = "saves Burst Volley for bigger moments", LessText = "fires Burst Volley sooner" },
                Reserve(0.2f, 0.5f),
            };
            RoleParams[Role.Warwright] = new[]
            {
                reaction,
                new InstinctParamDef { Param = InstinctParam.WallDistance, Min = 1.5f, Max = 7f, Default = 3.5f, MoreText = "raises walls further forward", LessText = "raises walls closer to the squad" },
                new InstinctParamDef { Param = InstinctParam.TrapLead, Min = 0f, Max = 1.6f, Default = 0.5f, MoreText = "leads its traps further ahead of enemies", LessText = "drops traps right on enemies" },
                new InstinctParamDef { Param = InstinctParam.AoeMin, Min = 1f, Max = 4f, Default = 2f, MoreText = "waits for bigger groups before trapping", LessText = "traps smaller groups" },
                new InstinctParamDef { Param = InstinctParam.Retreat, Min = 0.08f, Max = 0.5f, Default = 0.25f, MoreText = "backs off earlier when hurt", LessText = "fights on longer when hurt" },
                Reserve(0.25f, 0.55f),
            };
        }

        private static InstinctParamDef Reserve(float def, float max) => new InstinctParamDef
        {
            Param = InstinctParam.Reserve, Min = 0f, Max = max, Default = def,
            MoreText = "keeps more mana and stamina in reserve", LessText = "spends mana and stamina more freely",
        };
    }
}
