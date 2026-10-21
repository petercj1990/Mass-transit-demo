using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// Everything a player unit's utility AI needs for one think: the squad's command and
    /// Doctrine tier, the Instinct parameters it fights with, the enemies in the fight, and the
    /// running best option. Reused across thinks to avoid allocations.
    /// </summary>
    public sealed class BrainContext
    {
        public World W;
        public Unit U;
        public Squad Sq;
        public CommandType Cmd;
        public int Tier;
        public EnemyFamily Family;
        public bool InFight;
        public bool Debug;

        public readonly List<Unit> Enemies = new List<Unit>(32);
        public Vec2 Center;
        public Vec2 EnemyCenter;
        public Vec2 Fwd;
        public Vec2 Rt;
        public Vec2 Anchor;
        public Unit Tank;
        public Unit Healer;
        public Unit Melee;
        public Unit Siege;
        public Unit Ww;
        public Unit Focus;
        public Unit Boss;

        public Intent Best;
        public float BestScore;
        private readonly string[] _labels = new string[5];
        private readonly float[] _scores = new float[5];
        private int _count;

        public void Setup(World w, Unit u)
        {
            W = w;
            U = u;
            Sq = u.Squad;
            Cmd = Sq.Stance;
            Tier = u.Doctrine != null ? u.Doctrine.Tier(Cmd) : 0;
            Family = Sq.Fight != null ? Sq.Fight.DominantFamily : EnemyFamily.None;
            InFight = Sq.InCombat;
            Debug = w.InspectedUnitId == u.Id;
            _count = 0;
            Best = default;
            BestScore = float.MinValue;

            Enemies.Clear();
            Boss = null;
            Vec2 esum = Vec2.Zero;
            foreach (var e in Sq.Engaged)
            {
                if (!e.IsTargetable) continue;
                Enemies.Add(e);
                esum += e.Pos;
                if (e.Kind == UnitKind.Rustmaw) Boss = e;
            }
            if (Boss == null && w.Boss != null && w.Boss.Active && Sq.InCombat) Boss = w.Boss.Body;

            Tank = Sq.ActiveMember(Role.Tank);
            Healer = Sq.ActiveMember(Role.Healer);
            Melee = Sq.ActiveMember(Role.Melee);
            Siege = Sq.ActiveMember(Role.Siege);
            Ww = Sq.ActiveMember(Role.Warwright);

            Center = Sq.Centroid;
            EnemyCenter = Enemies.Count > 0 ? esum / Enemies.Count : Center + Sq.Forward * 6f;
            Fwd = Sq.Forward.LengthSq > 0.001f ? Sq.Forward : Vec2.Forward;
            Rt = Fwd.Right;
            bool anchored = Cmd == CommandType.DefensiveFormation || Cmd == CommandType.HoldPosition || Cmd == CommandType.Fortify;
            Anchor = anchored ? Sq.CombatAnchor : Center;
            if (Cmd == CommandType.Stack) Anchor = StackPoint();

            UpdateFocus();
            Focus = W.FindUnit(Sq.FocusTargetId != 0 ? Sq.FocusTargetId : Sq.AiFocusId);
            if (Focus != null && !Focus.IsTargetable) Focus = null;
        }

        public float P(InstinctParam p)
        {
            if (U.Instinct == null) return 0f;
            return U.Instinct.Get(Cmd, Family, p, Tier, W.Rng, InFight);
        }

        public void Offer(float score, Intent it, string label)
        {
            if (Debug) Record(label ?? it.Kind.ToString(), score);
            if (score <= BestScore) return;
            BestScore = score;
            it.Score = score;
            Best = it;
        }

        private void Record(string label, float score)
        {
            // keep the top five, sorted
            int pos = _count;
            for (int i = 0; i < _count; i++)
            {
                if (score > _scores[i])
                {
                    pos = i;
                    break;
                }
            }
            if (pos >= 5) return;
            int last = Math.Min(_count, 4);
            for (int i = last; i > pos; i--)
            {
                _scores[i] = _scores[i - 1];
                _labels[i] = _labels[i - 1];
            }
            _scores[pos] = score;
            _labels[pos] = label;
            if (_count < 5) _count++;
        }

        public void Commit()
        {
            if (BestScore == float.MinValue) Best = default;
            U.Intent = Best;
            if (!Debug) return;
            U.DebugCount = _count;
            for (int i = 0; i < _count; i++)
            {
                U.DebugLabel[i] = _labels[i];
                U.DebugScore[i] = _scores[i];
            }
        }

        // ------------------------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------------------------

        public static Intent MoveTo(Vec2 p) => new Intent { Kind = IntentKind.Move, Point = p };

        public static Intent AttackIntent(Unit target) => new Intent { Kind = IntentKind.Attack, TargetId = target.Id };

        public static Intent AbilityOn(AbilityId id, Entity target) =>
            new Intent { Kind = IntentKind.Ability, Ability = id, TargetId = target.Id, Point = target.Pos };

        public static Intent AbilityAt(AbilityId id, Vec2 point, Vec2 dir) =>
            new Intent { Kind = IntentKind.Ability, Ability = id, Point = point, Dir = dir };

        public bool Ready(AbilityId id) => U.AbilityReady(id);

        public Unit NearestEnemy(Vec2 p, out float dist)
        {
            Unit best = null;
            dist = float.MaxValue;
            foreach (var e in Enemies)
            {
                float d = Vec2.Distance(e.Pos, p);
                if (d < dist)
                {
                    dist = d;
                    best = e;
                }
            }
            return best;
        }

        public int EnemiesWithin(Vec2 p, float r)
        {
            int n = 0;
            foreach (var e in Enemies)
            {
                if (Vec2.DistanceSq(e.Pos, p) <= (r + e.Radius) * (r + e.Radius)) n++;
            }
            return n;
        }

        public bool TargetsSquadmate(Unit e, out Unit victim)
        {
            victim = W.FindUnit(e.CurrentTargetId);
            return victim != null && victim.Squad == Sq;
        }

        /// <summary>Utility of attacking an enemy, shaped by command, Doctrine tier and Instinct.</summary>
        public float TargetScore(Unit e, DamageType myType, float focusLowWeight)
        {
            float d = Vec2.Distance(U.Pos, e.Pos);
            float s = 50f - d * 1.2f;
            if (U.Intent.Kind == IntentKind.Attack && U.Intent.TargetId == e.Id) s += 8f;
            if (Focus != null && e.Id == Focus.Id)
            {
                // Focus Fire puts "both DPS on one target"; the tank and healer keep their jobs.
                bool dps = U.Role == Role.Melee || U.Role == Role.Siege || U.Role == Role.Warwright;
                if (Cmd == CommandType.FocusFire) s += dps ? 45f : 6f;
                else s += dps ? 22f : 8f;
            }
            if (Tier >= 2) s += (1f - e.HpFraction) * focusLowWeight;
            if (Tier >= 3)
            {
                if (e.Kind == UnitKind.ScrapMender) s += 22f;
                else if (e.Def.IsRanged) s += 8f;
                float m = W.Bestiary.KnownMultiplier(e.Def.Family, myType, e.Def);
                if (e.Kind == UnitKind.Rustmaw && W.Boss != null && (W.Boss.Overheated || W.Boss.VentsExposed)) m *= 1.5f;
                s += (m - 1f) * 30f;
            }
            if (e.Kind == UnitKind.Rustmaw && W.Boss != null && W.Boss.VentsExposed && Tier >= 3) s += 12f;
            return s;
        }

        // ------------------------------------------------------------------------------------
        // Positions
        // ------------------------------------------------------------------------------------

        /// <summary>Where this role stands in the squad's current combat posture.</summary>
        public Vec2 Slot(Role role)
        {
            Vec2 a = Anchor;
            Vec2 f = Fwd;
            Vec2 r = Rt;
            switch (Cmd)
            {
                case CommandType.Fortify:
                    switch (role)
                    {
                        case Role.Tank: return a + f * 1.8f;
                        case Role.Melee: return a + r * 1.7f + f * 0.6f;
                        case Role.Warwright: return a - r * 1.7f + f * 0.6f;
                        case Role.Siege: return a - f * 1.0f + r * 0.8f;
                        default: return a - f * 1.4f - r * 0.8f;
                    }
                case CommandType.Stack:
                    switch (role)
                    {
                        case Role.Tank: return a + f * 1.3f;
                        case Role.Melee: return a + r * 1.1f;
                        case Role.Warwright: return a - r * 1.1f;
                        case Role.Siege: return a - f * 0.9f + r * 0.7f;
                        default: return a - f * 1.1f - r * 0.6f;
                    }
                case CommandType.SpreadOut:
                    switch (role)
                    {
                        case Role.Tank: return a + f * 3f;
                        case Role.Melee: return a + f * 2f + r * 5.5f;
                        case Role.Warwright: return a + f * 1f - r * 5.5f;
                        case Role.Siege: return a - f * 5f + r * 5f;
                        default: return a - f * 6.5f - r * 3f;
                    }
                default:
                {
                    float k = Cmd == CommandType.HoldPosition ? 0.9f : 1f;
                    switch (role)
                    {
                        case Role.Tank: return a + f * (2.5f * k);
                        case Role.Melee: return a + (f * 1.4f + r * 1.7f) * k;
                        case Role.Warwright: return a + (f * 0.9f - r * 1.9f) * k;
                        case Role.Siege: return a + (-f * 3.2f + r * 1.5f) * k;
                        default: return a + (-f * 4.6f - r * 0.9f) * k;
                    }
                }
            }
        }

        /// <summary>
        /// Stack point: behind a friendly wall if one stands between the squad and the threat. Against
        /// a boss the squad stacks a safe distance out of swipe range, so the wall survives.
        /// </summary>
        public Vec2 StackPoint()
        {
            Vec2 threat = EnemyCenter;
            Vec2 baseP = Tank != null ? Tank.Pos : Center;
            if (Boss != null && !Boss.Untargetable)
            {
                threat = Boss.Pos;
                Vec2 away = (Center - Boss.Pos).Normalized;
                if (away == Vec2.Zero) away = -Fwd;
                baseP = W.Nav.NearestWalkable(Boss.Pos + away * 8.5f, 4);
            }
            Structure bestWall = null;
            float bestD = 12f;
            foreach (var s in W.Structures)
            {
                if (s.Removed || s.Kind != StructureKind.Rampart || s.Faction != Faction.Player) continue;
                if (Vec2.Distance(s.Pos, threat) < 4.5f) continue; // too close to the threat to hide behind
                float d = Vec2.Distance(s.Pos, baseP);
                if (d < bestD)
                {
                    bestD = d;
                    bestWall = s;
                }
            }
            if (bestWall != null)
            {
                Vec2 away = (bestWall.Pos - threat).Normalized;
                if (away == Vec2.Zero) away = -Fwd;
                return W.Nav.NearestWalkable(bestWall.Pos + away * 2.2f, 4);
            }
            return baseP;
        }

        /// <summary>
        /// Picks the squad's Focus Fire target when the player hasn't: tier 1 the nearest, tier 2 the
        /// weakest, tier 3 healers then casters, tier 4+ switches off targets being healed.
        /// </summary>
        private void UpdateFocus()
        {
            if (Sq.Stance != CommandType.FocusFire && Sq.Stance != CommandType.Charge)
            {
                Sq.AiFocusId = 0;
                return;
            }
            if (Sq.FocusTargetId != 0 && W.FindUnit(Sq.FocusTargetId) is Unit pf && pf.IsTargetable) return;
            var cur = W.FindUnit(Sq.AiFocusId);
            int tier = SquadSystem.SquadTier(Sq);
            bool valid = cur != null && cur.IsTargetable && Enemies.Contains(cur);
            if (valid && tier >= 4 && W.Time - cur.LastHealedTime < 1.2f && W.Time - Sq.AiFocusChosenAt > 1.5f) valid = false;
            if (valid && W.Time - Sq.AiFocusChosenAt < 2.5f) return;
            Unit best = null;
            float bestS = float.MinValue;
            Vec2 from = Tank != null ? Tank.Pos : Center;
            foreach (var e in Enemies)
            {
                float s = -Vec2.Distance(e.Pos, from);
                if (tier >= 2) s += (1f - e.HpFraction) * 20f;
                if (tier >= 3)
                {
                    if (e.Kind == UnitKind.ScrapMender) s += 40f;
                    else if (e.Def.IsRanged) s += 15f;
                }
                if (tier >= 4 && W.Time - e.LastHealedTime < 1.2f && e.Kind != UnitKind.Rustmaw) s -= 25f;
                if (e.Kind == UnitKind.Rustmaw && W.Boss != null && W.Boss.MendersAlive > 0 && tier >= 3) s -= 30f;
                if (valid && e == cur) s += 6f;
                if (s > bestS)
                {
                    bestS = s;
                    best = e;
                }
            }
            if (best != null && best.Id != Sq.AiFocusId)
            {
                Sq.AiFocusId = best.Id;
                Sq.AiFocusChosenAt = W.Time;
            }
        }

        /// <summary>Nearest spot outside a telegraph, preferring cover for fire waves.</summary>
        public Vec2 SafePoint(Telegraph t)
        {
            // Hiding behind a wall from a fire wave is a Stack / Fortify behaviour, or a veteran instinct.
            bool seeksCover = Cmd == CommandType.Stack || Cmd == CommandType.Fortify || Tier >= 4;
            if (t.CoverBlocks && seeksCover)
            {
                Structure best = null;
                float bestD = 11f;
                foreach (var s in W.Structures)
                {
                    if (s.Removed || !s.IsWall || s.Faction != Faction.Player) continue;
                    float d = Vec2.Distance(s.Pos, U.Pos);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = s;
                    }
                }
                if (best != null)
                {
                    Vec2 behind = best.Pos + (best.Pos - t.Pos).Normalized * 1.7f;
                    behind = W.Nav.NearestWalkable(behind, 3);
                    if (!W.Nav.SightClear(t.Pos, behind)) return behind;
                }
                // No wall to hide behind: a grown Titan's shadow is the next best thing.
                Unit giant = null;
                float giantD = 12f;
                foreach (var g in W.Units)
                {
                    if (g == U || !g.IsPlayer || !g.IsTargetable || g.TitanT <= 0f) continue;
                    float d = Vec2.Distance(g.Pos, U.Pos);
                    if (d < giantD)
                    {
                        giantD = d;
                        giant = g;
                    }
                }
                if (giant != null)
                {
                    Vec2 away = (giant.Pos - t.Pos).Normalized;
                    if (away == Vec2.Zero) away = -Fwd;
                    Vec2 behind = W.Nav.NearestWalkable(giant.Pos + away * (giant.Radius + U.Radius + 0.4f), 3);
                    if (Titan.InShadow(giant, t.Pos, behind)) return behind;
                }
            }

            Vec2 pref = (Center - U.Pos);
            Vec2 bestP = U.Pos;
            float bestScore = float.MaxValue;
            float[] rings = { 1.5f, 2.5f, 3.5f, 5f, 7f, 9.5f, 12.5f, 17f };
            foreach (float r in rings)
            {
                for (int i = 0; i < 16; i++)
                {
                    Vec2 dir = Vec2.FromAngle(i * KMath.TwoPi / 16f);
                    Vec2 p = U.Pos + dir * r;
                    if (W.Nav.BlocksMoveAt(p)) continue;
                    if (t.Contains(p, U.Radius + 0.4f)) continue;
                    bool inOther = false;
                    foreach (var o in W.Telegraphs)
                    {
                        if (o != t && !o.Removed && o.Threatens == Faction.Player && o.Remaining > 0f && o.Contains(p, U.Radius))
                        {
                            inOther = true;
                            break;
                        }
                    }
                    if (inOther) continue;
                    float score = r - Vec2.Dot(dir, pref.Normalized) * 0.8f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestP = p;
                    }
                }
                if (bestScore < float.MaxValue) return bestP;
            }
            return bestP;
        }

        /// <summary>A retreat point away from a threat, avoiding walls from tier 2 and seeking high ground from tier 4.</summary>
        public Vec2 RetreatPoint(Vec2 from, float distance)
        {
            Vec2 away = (U.Pos - from).Normalized;
            if (away == Vec2.Zero) away = -Fwd;
            if (Tier < 2) return U.Pos + away * distance;
            Vec2 best = U.Pos + away * distance;
            float bestS = float.MinValue;
            for (int i = -3; i <= 3; i++)
            {
                Vec2 dir = away.Rotate(i * 0.35f);
                Vec2 p = U.Pos + dir * distance;
                if (!W.Nav.LineWalkable(U.Pos, p, U.Radius)) continue;
                float s = -Math.Abs(i) * 0.6f + Vec2.Distance(p, from) * 0.2f;
                if (Tier >= 3 && Tank != null && Tank != U) s -= Vec2.Distance(p, Tank.Pos) * 0.08f; // drag toward the tank
                if (Tier >= 4) s += (W.HeightAt(p) - W.HeightAt(U.Pos)) * 0.8f;
                if (s > bestS)
                {
                    bestS = s;
                    best = p;
                }
            }
            return best;
        }

        public Vec2 HighGroundNear(Vec2 p, Unit target, float radius)
        {
            Vec2 best = p;
            float bestH = W.HeightAt(p);
            for (int i = 0; i < 8; i++)
            {
                Vec2 c = p + Vec2.FromAngle(i * KMath.TwoPi / 8f) * radius;
                if (W.Nav.BlocksMoveAt(c)) continue;
                float h = W.HeightAt(c);
                if (h > bestH + 0.6f && (target == null || W.Nav.SightClear(c, target.Pos)))
                {
                    bestH = h;
                    best = c;
                }
            }
            return best;
        }

        public bool OwnWallNear(Vec2 p, float r)
        {
            foreach (var s in W.Structures)
            {
                if (s.Removed || s.Kind != StructureKind.Rampart || s.OwnerSquad != Sq) continue;
                if (Vec2.DistanceToSegment(s.EndA, s.EndB, p) < r) return true;
            }
            return false;
        }

        public bool OwnFieldNear(StructureKind kind, Vec2 p, float r)
        {
            foreach (var s in W.Structures)
            {
                if (s.Removed || s.Kind != kind || s.OwnerSquad != Sq) continue;
                if (Vec2.Distance(s.Pos, p) < r) return true;
            }
            return false;
        }
    }
}
