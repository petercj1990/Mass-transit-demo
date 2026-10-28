using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    public enum BenchJob : byte
    {
        None,
        Weapons,
        Armor,
        Sigil,
    }

    /// <summary>
    /// The clan base: Kin Cradle (rebirth), War Hall (drills that make the kin-band stronger), Smelter
    /// and Salvage Bench (which only run while the kin-band is garrisoned, so resting at home to heal
    /// wounds is also when the workshops turn loot into power).
    /// </summary>
    public sealed class PlayerBase
    {
        // Rebirth only uses renewable materials (hide from respawning camps, plentiful crater stone), so a
        // run of deaths can never soft-lock the clan.
        public static readonly Cost RegrowCost = Cost.Of(ResourceKind.Hide, 6, ResourceKind.Stone, 8);
        public static readonly Cost RegrowCostTier2 = Cost.Of(ResourceKind.Hide, 12, ResourceKind.Stone, 12);
        public static readonly Cost RecruitCost = Cost.Of(ResourceKind.Scrap, 60, ResourceKind.Stone, 50, ResourceKind.Fiber, 40, ResourceKind.Hide, 20);
        public static readonly Cost[] WarHallCost =
        {
            null,
            Cost.Of(ResourceKind.Stone, 60, ResourceKind.Fiber, 35, ResourceKind.Ferrite, 25),
            Cost.Of(ResourceKind.Stone, 80, ResourceKind.Alloy, 30, ResourceKind.Hide, 30),
        };
        public static readonly Cost WeaponsCost = Cost.Of(ResourceKind.Alloy, 30, ResourceKind.Hide, 16);
        public static readonly Cost ArmorCost = Cost.Of(ResourceKind.Alloy, 35, ResourceKind.Hide, 20, ResourceKind.Fiber, 15);
        public static readonly Cost SigilCost = Cost.Of(ResourceKind.Alloy, 30, ResourceKind.Hide, 30, ResourceKind.Stone, 50);

        public const float RecruitSeconds = 30f;
        public const float WeaponsSeconds = 15f;
        public const float ArmorSeconds = 15f;
        public const float SigilSeconds = 20f;

        public Vec2 Pos;
        public int WarHallLevel = 1;
        public readonly List<int> CradleQueue = new List<int>();
        public int Regrowing;
        public float RegrowTimer;
        public bool RegrowStarved;
        public float WipeRecallTimer;
        public bool Recruiting;
        public float RecruitTimer;
        public BenchJob Bench;
        public int BenchSquadId;
        public float BenchTimer;
        public float BenchTotal;
        public float SmeltTimer;
        public bool SigilCrafted;
        public readonly Dictionary<BuildingKind, Structure> Buildings = new Dictionary<BuildingKind, Structure>();

        public string CradleStatus = "Idle";
        public string BenchStatus = "Idle";
        public string SmelterStatus = "Idle";

        /// <summary>This slice plays one kin-band; the War Hall makes it stronger instead of bigger.</summary>
        public int SquadCap => 1;

        public bool HasGarrison(World w)
        {
            foreach (var s in w.Squads)
            {
                if (s.Garrisoned) return true;
            }
            return false;
        }

        public int GarrisonCount(World w)
        {
            int n = 0;
            foreach (var s in w.Squads)
            {
                if (s.Garrisoned) n++;
            }
            return n;
        }

        public void ReceiveCore(World w, int unitId)
        {
            var u = w.FindUnitAny(unitId);
            if (u == null || u.Life != LifeState.Dead) return;
            u.Core = CoreState.AtCradle;
            u.CoreHolder = 0;
            if (!CradleQueue.Contains(unitId) && Regrowing != unitId) CradleQueue.Add(unitId);
            w.Alert($"{u.Name}'s kin-core is home. The Kin Cradle will regrow them.", Pos, 0);
        }

        /// <summary>Raises every member's War Hall level, keeping their health, mana and stamina fractions.</summary>
        public static void ApplyHallLevel(Squad sq, int level)
        {
            sq.HallLevel = level;
            foreach (var m in sq.Members)
            {
                float hp = m.HpFraction, mana = m.ManaFraction, stamina = m.StaminaFraction;
                m.HallLevel = level;
                if (m.Life == LifeState.Active) m.Hp = m.MaxHp * hp;
                m.Mana = m.MaxMana * mana;
                m.Stamina = m.MaxStamina * stamina;
            }
        }

        public Cost RegrowCostFor(Unit u) => u.ArmorTier >= 2 ? RegrowCostTier2 : RegrowCost;

        public void HandleOrder(World w, PlayerOrder o)
        {
            switch (o.Type)
            {
                case OrderType.Recruit:
                    if (Recruiting)
                    {
                        w.Alert("The Kin Cradle is already growing recruits.", 1);
                        return;
                    }
                    if (w.Squads.Count >= SquadCap)
                    {
                        w.Alert("You lead a single kin-band. Upgrade the War Hall to make it stronger.", 1);
                        return;
                    }
                    if (w.Squads.Count >= SquadCap)
                    {
                        w.Alert($"Squad cap reached ({SquadCap}). Upgrade the War Hall first.", 1);
                        return;
                    }
                    if (!w.Stock.TrySpend(RecruitCost))
                    {
                        w.Alert("Not enough materials to recruit a kin-band: " + RecruitCost, 1);
                        return;
                    }
                    Recruiting = true;
                    RecruitTimer = RecruitSeconds;
                    w.Alert("The Kin Cradle begins growing a new kin-band.", Pos, 0);
                    return;

                case OrderType.UpgradeWarHall:
                {
                    if (WarHallLevel >= Balance.MaxWarHallLevel)
                    {
                        w.Alert("The War Hall is fully upgraded for this milestone.", 1);
                        return;
                    }
                    var cost = WarHallCost[WarHallLevel];
                    if (!w.Stock.TrySpend(cost))
                    {
                        w.Alert("Not enough materials to upgrade the War Hall: " + cost, 1);
                        return;
                    }
                    WarHallLevel++;
                    foreach (var sq in w.Squads) ApplyHallLevel(sq, WarHallLevel);
                    w.Alert($"War Hall level {WarHallLevel}: the kin-band drills harder (+{Balance.HallHealthPerLevel:P0} health, +{Balance.HallResourcePerLevel:P0} mana and stamina).", Pos, 3);
                    return;
                }

                case OrderType.CraftWeapons:
                case OrderType.CraftArmor:
                case OrderType.CraftSigil:
                {
                    if (Bench != BenchJob.None)
                    {
                        w.Alert("The Salvage Bench is busy.", 1);
                        return;
                    }
                    BenchJob job = o.Type == OrderType.CraftWeapons ? BenchJob.Weapons : o.Type == OrderType.CraftArmor ? BenchJob.Armor : BenchJob.Sigil;
                    var sq = w.FindSquad(o.TargetId);
                    if (job != BenchJob.Sigil && sq == null) return;
                    if (job == BenchJob.Weapons && sq.WeaponTier >= 2) { w.Alert($"{sq.Name} already has ferrite arms.", 1); return; }
                    if (job == BenchJob.Armor && sq.ArmorTier >= 2) { w.Alert($"{sq.Name} already has ferrite plate.", 1); return; }
                    if (job == BenchJob.Sigil && SigilCrafted) { w.Alert("The Breach Sigil is already made.", 1); return; }
                    var cost = job == BenchJob.Weapons ? WeaponsCost : job == BenchJob.Armor ? ArmorCost : SigilCost;
                    if (!w.Stock.TrySpend(cost))
                    {
                        w.Alert("Not enough materials: " + cost, 1);
                        return;
                    }
                    Bench = job;
                    BenchSquadId = sq?.Id ?? 0;
                    BenchTotal = job == BenchJob.Weapons ? WeaponsSeconds : job == BenchJob.Armor ? ArmorSeconds : SigilSeconds;
                    BenchTimer = BenchTotal;
                    if (!HasGarrison(w)) w.Alert("Salvage Bench queued. It only runs while a squad is garrisoned at base.", Pos, 1);
                    return;
                }

                case OrderType.RecallCores:
                {
                    bool anyActive = false;
                    foreach (var s in w.Squads)
                    {
                        foreach (var m in s.Members)
                        {
                            if (m.Life == LifeState.Active) anyActive = true;
                        }
                    }
                    if (anyActive)
                    {
                        w.Alert("Kin-cores can only be recalled when no Vhorn is left standing. Send the kin-band to recover the cache.", 1);
                        return;
                    }
                    RecallAll(w);
                    return;
                }
            }
        }

        private void RecallAll(World w)
        {
            foreach (var c in w.Caches)
            {
                if (c.Removed) continue;
                foreach (int id in c.UnitIds) ReceiveCore(w, id);
                c.Removed = true;
            }
            WipeRecallTimer = 0f;
        }

        private static bool AnyoneStanding(World w)
        {
            foreach (var s in w.Squads)
            foreach (var m in s.Members)
            {
                if (m.Life == LifeState.Active) return true;
            }
            return false;
        }

        private static bool AnyCache(World w)
        {
            foreach (var c in w.Caches)
            {
                if (!c.Removed) return true;
            }
            return false;
        }

        public void Update(World w)
        {
            float dt = Balance.Dt;
            int garrisons = GarrisonCount(w);
            bool raid = w.Boss != null && w.Boss.Active;

            // One kin-band, so nobody can fetch a wiped band's cores: the clan carries them home itself.
            if (!raid && AnyCache(w) && !AnyoneStanding(w))
            {
                WipeRecallTimer += dt;
                if (WipeRecallTimer >= Balance.WipeRecallSeconds)
                {
                    RecallAll(w);
                    w.Alert("The clan has carried the fallen kin-cores home to the Kin Cradle.", Pos, 2);
                }
            }
            else WipeRecallTimer = 0f;

            // Kin Cradle: rebirth first, then recruits. Rebirth the stockpile can't pay for still happens, slowly,
            // so a wiped kin-band is never stuck.
            if (Regrowing == 0 && CradleQueue.Count > 0)
            {
                var u = w.FindUnitAny(CradleQueue[0]);
                if (u == null)
                {
                    CradleQueue.RemoveAt(0);
                }
                else
                {
                    RegrowStarved = !w.Stock.TrySpend(RegrowCostFor(u));
                    Regrowing = u.Id;
                    RegrowTimer = Balance.RegrowSeconds * (RegrowStarved ? Balance.StarvedRegrowMul : 1f);
                    CradleQueue.RemoveAt(0);
                }
            }
            if (Regrowing != 0)
            {
                var u = w.FindUnitAny(Regrowing);
                if (raid) CradleStatus = "Paused: the Cradle can't reach inside the raid";
                else
                {
                    RegrowTimer -= dt;
                    CradleStatus = RegrowStarved
                        ? $"Regrowing {u?.Name} slowly, no materials ({RegrowTimer:0}s)"
                        : $"Regrowing {u?.Name} ({RegrowTimer:0}s)";
                    if (RegrowTimer <= 0f)
                    {
                        Regrowing = 0;
                        if (u != null)
                        {
                            Combat.Regrow(w, u, CradleDoor());
                            w.Alert($"{u.Name} ({u.Def.Name}) has been regrown with gear, Doctrine and Instinct intact.", Pos, 3);
                        }
                    }
                }
            }
            else if (Recruiting)
            {
                RecruitTimer -= dt;
                CradleStatus = $"Growing a new kin-band ({RecruitTimer:0}s)";
                if (RecruitTimer <= 0f)
                {
                    Recruiting = false;
                    var sq = PlayerUnits.CreateSquad(w, CradleDoor() + new Vec2(0f, 5f));
                    w.Alert($"{sq.Name} has joined the clan (fresh recruits, no Doctrine yet).", sq.Anchor, 3);
                }
            }
            else if (CradleQueue.Count == 0) CradleStatus = "Idle";

            // Smelter: 2 ferrite -> 1 alloy, only with a garrison working it.
            if (garrisons == 0) SmelterStatus = w.Stock[ResourceKind.Ferrite] >= 2f ? "Idle: needs a garrisoned squad" : "Idle";
            else if (w.Stock[ResourceKind.Ferrite] < 2f)
            {
                SmelterStatus = "Idle: no ferrite";
                SmeltTimer = 0f;
            }
            else
            {
                SmeltTimer += dt * (1f + 0.5f * (garrisons - 1));
                SmelterStatus = $"Smelting ({SmeltTimer / Balance.SmeltSeconds:P0})";
                if (SmeltTimer >= Balance.SmeltSeconds)
                {
                    SmeltTimer = 0f;
                    w.Stock.Add(ResourceKind.Ferrite, -2f);
                    w.Stock.Add(ResourceKind.Alloy, 1f);
                }
            }

            // Salvage Bench
            if (Bench == BenchJob.None) BenchStatus = "Idle";
            else if (garrisons == 0) BenchStatus = $"{BenchName(w)}: waiting for a garrisoned squad";
            else
            {
                BenchTimer -= dt;
                BenchStatus = $"{BenchName(w)} ({BenchTimer:0}s)";
                if (BenchTimer <= 0f) FinishBench(w);
            }
        }

        private string BenchName(World w)
        {
            var sq = w.FindSquad(BenchSquadId);
            switch (Bench)
            {
                case BenchJob.Weapons: return $"Ferrite arms for {sq?.Name}";
                case BenchJob.Armor: return $"Ferrite plate for {sq?.Name}";
                case BenchJob.Sigil: return "Breach Sigil";
            }
            return "";
        }

        private void FinishBench(World w)
        {
            var sq = w.FindSquad(BenchSquadId);
            switch (Bench)
            {
                case BenchJob.Weapons:
                    if (sq != null)
                    {
                        sq.WeaponTier = 2;
                        foreach (var m in sq.Members) m.WeaponTier = 2;
                        w.Alert($"{sq.Name} now carries ferrite arms (+{Balance.WeaponTierDamage:P0} damage and healing).", Pos, 3);
                    }
                    break;
                case BenchJob.Armor:
                    if (sq != null)
                    {
                        sq.ArmorTier = 2;
                        foreach (var m in sq.Members)
                        {
                            float frac = m.HpFraction;
                            m.ArmorTier = 2;
                            if (m.Life == LifeState.Active) m.Hp = m.MaxHp * frac;
                        }
                        w.Alert($"{sq.Name} now wears ferrite plate (+{Balance.ArmorTierHealth:P0} health, +{Balance.ArmorTierArmor:0} armour).", Pos, 3);
                    }
                    break;
                case BenchJob.Sigil:
                    SigilCrafted = true;
                    w.Boss?.OpenGate(w);
                    break;
            }
            Bench = BenchJob.None;
        }

        public Vec2 CradleDoor()
        {
            if (Buildings.TryGetValue(BuildingKind.KinCradle, out var s))
            {
                Vec2 toPlaza = (Pos - s.Pos).Normalized;
                return s.Pos + toPlaza * (s.HalfWidth + 2f);
            }
            return Pos;
        }
    }
}
