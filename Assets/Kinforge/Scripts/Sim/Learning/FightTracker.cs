using System;
using System.Collections.Generic;
using System.Text;

namespace Kinforge.Sim
{
    /// <summary>Per-unit numbers for the fight in progress; they feed Doctrine XP and Instinct scoring.</summary>
    public sealed class FightStats
    {
        public float DamageDealt;
        public float DamageTaken;
        public float HealingDone;
        public float StructureAbsorbed;
        public float CcSeconds;
        public float ThreatHeld;
        public int Downs;
        public int Kills;
        public int Stabilizes;
        public int Interrupts;
        public int TitanGrows;
        public int TitanClashes;
        public int TitanSheltered;
        public bool Died;
        public readonly float[] CommandTime = new float[CommandInfo.Count];

        public float TotalTime
        {
            get
            {
                float t = 0f;
                foreach (float c in CommandTime) t += c;
                return t;
            }
        }
    }

    public struct ReportLine
    {
        public int UnitId;
        public string Who;
        public string Text;
        public int Kind; // 0 xp, 1 tier up / unlock, 2 learned, 3 setback
    }

    public sealed class BattleReport
    {
        public int SquadId;
        public string SquadName;
        public float Time;
        public float Duration;
        public string Result;
        public string Enemies;
        public int Kills;
        public readonly List<ReportLine> Lines = new List<ReportLine>();

        public string Headline => $"{SquadName}: {Result} ({Duration:0}s, {Kills} kills) vs {Enemies}";
    }

    /// <summary>
    /// Tracks one squad's fight from first contact until no enemies have been engaged for a few
    /// seconds, then hands out Doctrine XP, runs the Instinct update and writes the battle report.
    /// </summary>
    public sealed class FightTracker
    {
        public float StartTime;
        public readonly HashSet<int> EnemyIds = new HashSet<int>();
        public readonly Dictionary<EnemyFamily, float> FamilyHp = new Dictionary<EnemyFamily, float>();
        public readonly HashSet<UnitKind> KindsSeen = new HashSet<UnitKind>();
        public readonly Dictionary<UnitKind, int> KindCounts = new Dictionary<UnitKind, int>();
        public float EnemyMaxHpTotal;
        public float SquadMaxHp;
        public float EnemyTargetTime;
        public bool UsedFallBack;
        public bool BossKilled;
        private EnemyFamily _dominant = EnemyFamily.None;

        public EnemyFamily DominantFamily => _dominant;

        public void Begin(World w, Squad sq)
        {
            StartTime = w.Time;
            foreach (var m in sq.Members)
            {
                if (!m.IsActive) continue;
                StartUnit(m);
                SquadMaxHp += m.MaxHp;
            }
        }

        private static void StartUnit(Unit m)
        {
            m.Fight = new FightStats();
            m.Instinct?.BeginFight();
        }

        public void Tick(World w, Squad sq, float dt)
        {
            foreach (var e in sq.Engaged)
            {
                if (EnemyIds.Add(e.Id))
                {
                    EnemyMaxHpTotal += e.MaxHp;
                    KindsSeen.Add(e.Kind);
                    KindCounts.TryGetValue(e.Kind, out int kc);
                    KindCounts[e.Kind] = kc + 1;
                    FamilyHp.TryGetValue(e.Def.Family, out float hp);
                    FamilyHp[e.Def.Family] = hp + e.MaxHp;
                    float best = -1f;
                    foreach (var kv in FamilyHp)
                    {
                        if (kv.Value > best)
                        {
                            best = kv.Value;
                            _dominant = kv.Key;
                        }
                    }
                }
                if (e.Life != LifeState.Active || e.CurrentTargetId == 0) continue;
                var t = w.FindUnit(e.CurrentTargetId);
                if (t == null || t.Squad != sq) continue;
                EnemyTargetTime += dt;
                if (t.Fight != null) t.Fight.ThreatHeld += dt;
            }
            if (sq.Stance == CommandType.FallBack) UsedFallBack = true;
            foreach (var m in sq.Members)
            {
                if (!m.IsActive) continue;
                if (m.Fight == null)
                {
                    StartUnit(m);
                    SquadMaxHp += m.MaxHp;
                }
                m.Fight.CommandTime[(int)sq.Stance] += dt;
            }
        }

        private bool Suits(CommandType c, bool win)
        {
            bool has(UnitKind k) => KindsSeen.Contains(k);
            int count = EnemyIds.Count;
            switch (c)
            {
                case CommandType.Charge: return has(UnitKind.SalvageDrone) || count <= 2;
                case CommandType.DefensiveFormation: return (has(UnitKind.Cratermaw) || has(UnitKind.ScrapHound)) && count >= 3;
                case CommandType.HoldPosition: return count >= 3 && !has(UnitKind.SalvageDrone);
                case CommandType.Kite: return has(UnitKind.Rockback) || has(UnitKind.Rustmaw);
                case CommandType.FocusFire: return has(UnitKind.ScrapMender) || has(UnitKind.Rustmaw);
                case CommandType.SpreadOut: return has(UnitKind.Rustmaw) || has(UnitKind.Rockback);
                case CommandType.Stack: return has(UnitKind.Rustmaw);
                case CommandType.FallBack: return !win;
                case CommandType.Fortify: return count >= 4;
            }
            return false;
        }

        private static float ExpectedDps(Role r)
        {
            switch (r)
            {
                case Role.Tank: return 8f;
                case Role.Healer: return 4f;
                case Role.Melee: return 18f;
                case Role.Siege: return 14f;
                default: return 6f;
            }
        }

        private float Score(Unit m, float duration, bool win, bool wiped, float squadDamageTaken)
        {
            var f = m.Fight;
            float dmg = KMath.Clamp(f.DamageDealt / (duration * ExpectedDps(m.Role)), 0f, 2.5f);
            float taken = f.DamageTaken / Math.Max(1f, m.Def.MaxHp);
            float s;
            switch (m.Role)
            {
                case Role.Tank:
                    s = 0.4f * dmg + 1.5f * (f.ThreatHeld / Math.Max(1f, EnemyTargetTime)) - 0.15f * taken + 0.2f * f.Interrupts;
                    break;
                case Role.Healer:
                    s = 0.2f * dmg + 1.2f * KMath.Clamp(f.HealingDone / Math.Max(50f, squadDamageTaken), 0f, 1.5f) + 0.3f * f.Stabilizes - 0.5f * taken;
                    break;
                case Role.Warwright:
                    s = 0.4f * dmg + 0.6f * (f.StructureAbsorbed / 150f + f.CcSeconds / 8f) - 0.4f * taken;
                    break;
                default:
                    s = dmg - 0.45f * taken;
                    break;
            }
            s -= 0.5f * f.Downs;
            if (f.Died) s -= 1f;
            if (win) s += 0.3f;
            if (wiped) s -= 0.3f;
            return s;
        }

        public BattleReport End(World w, Squad sq, bool wiped)
        {
            float duration = Math.Max(3f, w.Time - StartTime);
            int kills = 0;
            int alive = 0;
            foreach (int id in EnemyIds)
            {
                var e = w.FindUnitAny(id);
                if (e == null || e.Life == LifeState.Dead) kills++;
                else alive++;
            }
            bool win = !wiped && alive == 0 && kills > 0;
            string result = wiped ? "Squad wiped" : win ? "Victory" : UsedFallBack ? "Fell back" : "Disengaged";
            float difficulty = KMath.Clamp(EnemyMaxHpTotal / Math.Max(1f, SquadMaxHp), 0.4f, 2.5f);
            float outcome = win ? Balance.DoctrineWinMul : (wiped ? 0.5f : Balance.DoctrineLossMul);

            var report = new BattleReport
            {
                SquadId = sq.Id, SquadName = sq.Name, Time = w.Time, Duration = duration, Result = result, Kills = kills,
                Enemies = EnemySummary(KindCounts),
            };

            float squadTaken = 0f;
            foreach (var m in sq.Members)
            {
                if (m.Fight != null) squadTaken += m.Fight.DamageTaken;
            }

            foreach (var m in sq.Members)
            {
                var f = m.Fight;
                if (f == null) continue;
                float total = Math.Max(0.01f, f.TotalTime);
                float xpSum = 0f;
                var tierUps = new List<string>();
                for (int c = 0; c < CommandInfo.Count; c++)
                {
                    float t = f.CommandTime[c];
                    if (t < 0.5f) continue;
                    var cmd = (CommandType)c;
                    float share = t / total;
                    float xp = t * Balance.DoctrineXpPerSecond * difficulty * outcome * (Suits(cmd, win) ? Balance.DoctrineSuitMul : 1f);
                    xp += f.Kills * Balance.DoctrineKillXp * share;
                    if (BossKilled) xp += Balance.BossXpBurst * share;
                    if (m.Life == LifeState.Dead) xp = 0f; // the kin-core keeps banked Doctrine only
                    if (xp <= 0f) continue;
                    xpSum += xp;
                    int up = m.Doctrine.AddXp(cmd, xp);
                    if (up > 0)
                    {
                        tierUps.Add($"{CommandInfo.Get(cmd).Name} tier {up}");
                        w.Emit(SimEventType.TierUp, m.Id, c, m.Pos, up);
                    }
                }

                if (xpSum > 0f) report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = $"+{xpSum:0} Doctrine XP", Kind = 0 });
                foreach (var t in tierUps) report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = "reached " + t, Kind = 1 });
                for (var slot = PlayerUnits.RefreshAbilitySlots(m); slot != null; slot = PlayerUnits.RefreshAbilitySlots(m))
                {
                    var d = slot.Def;
                    report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = $"unlocked {d.Name}", Kind = 1 });
                    w.Emit(SimEventType.AbilityUnlocked, m.Id, 0, m.Pos, 0f, DamageType.Kinetic, d.Id);
                    if (d.Id == AbilityId.TitanForm)
                        w.Alert($"{m.Name}'s kin-core remembers the war-form! Titan Form unlocked: in hard fights {m.Name} grows into a living wall.", m.Pos, 3);
                    else
                        w.Alert($"{m.Name} unlocked {d.Name}!", m.Pos, 3);
                }
                if (f.TitanGrows > 0) report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = "rose as a Titan" + Times(f.TitanGrows), Kind = 0 });
                if (f.TitanClashes > 0) report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = "stopped Rustmaw's charge dead" + Times(f.TitanClashes), Kind = 0 });
                if (f.TitanSheltered > 0) report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = $"sheltered kin from {f.TitanSheltered} fire-wave hit{(f.TitanSheltered == 1 ? "" : "s")}", Kind = 0 });

                if (m.Life != LifeState.Dead && m.Instinct != null)
                {
                    float score = Score(m, duration, win, wiped, squadTaken);
                    foreach (var note in m.Instinct.EndFight(score))
                        report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = note, Kind = 2 });
                }
                else
                {
                    m.Instinct?.BeginFight();
                }

                if (m.Life == LifeState.Dead) report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = "was killed", Kind = 3 });
                else if (f.Downs > 0) report.Lines.Add(new ReportLine { UnitId = m.Id, Who = m.Name, Text = "was downed and is Injured", Kind = 3 });

                m.Fights++;
                m.Fight = null;
            }
            return report;
        }

        private static string Times(int n) => n > 1 ? $" x{n}" : "";

        private static string EnemySummary(Dictionary<UnitKind, int> names)
        {
            var sb = new StringBuilder();
            foreach (var kv in names)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Value).Append(' ').Append(UnitDefs.Get(kv.Key).Name);
            }
            return sb.Length > 0 ? sb.ToString() : "nothing";
        }
    }
}
