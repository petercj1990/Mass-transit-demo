using System.Linq;
using Kinforge.Sim;
using Xunit;

namespace Kinforge.Sim.Tests
{
    public class DamageTests
    {
        [Fact]
        public void Kinetic_damage_is_reduced_by_armour()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var g = Scenarios.AddPack(w, new Vec2(30f, 60f), UnitKind.Rockback);
            var rock = g.Members[0];
            var bruiser = sq.Member(Role.Melee);

            float hp = rock.Hp;
            float dealt = Combat.Deal(w, bruiser, rock, 100f, DamageType.Kinetic, HitFlags.Melee);

            // Rockback: 80 armour (100/180) and 0.6 kinetic resistance.
            Assert.Equal(100f * 0.6f * 100f / 180f, dealt, 2);
            Assert.Equal(hp - dealt, rock.Hp, 2);
        }

        [Fact]
        public void Corrosive_hits_strip_armour()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var rock = Scenarios.AddPack(w, new Vec2(30f, 60f), UnitKind.Rockback).Members[0];
            float before = rock.Armor;
            Combat.Deal(w, sq.Member(Role.Melee), rock, 10f, DamageType.Corrosive, HitFlags.Melee);
            Combat.Deal(w, sq.Member(Role.Melee), rock, 10f, DamageType.Corrosive, HitFlags.Melee);
            Assert.Equal(before - 2 * Balance.ShredPerStack, rock.Armor, 2);
        }

        [Fact]
        public void Arc_damage_chains_to_nearby_enemies()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var g = Scenarios.AddPack(w, new Vec2(30f, 60f), UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw);
            foreach (var m in g.Members) m.Pos = new Vec2(30f + g.Members.IndexOf(m) * 1.5f, 60f);
            Combat.Deal(w, sq.Member(Role.Siege), g.Members[0], 40f, DamageType.Arc, HitFlags.Ranged);
            Assert.True(g.Members[1].Hp < g.Members[1].MaxHp);
            Assert.True(g.Members[2].Hp < g.Members[2].MaxHp);
        }

        [Fact]
        public void No_friendly_fire_on_direct_attacks()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            float dealt = Combat.Deal(w, sq.Member(Role.Melee), sq.Member(Role.Healer), 50f, DamageType.Kinetic, HitFlags.Melee);
            Assert.Equal(0f, dealt);
        }

        [Fact]
        public void Bestiary_learns_weaknesses_from_hits()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var rock = Scenarios.AddPack(w, new Vec2(30f, 60f), UnitKind.Rockback).Members[0];
            Assert.False(w.Bestiary.Knows(EnemyFamily.Rockback, DamageType.Corrosive));
            Combat.Deal(w, sq.Member(Role.Melee), rock, 10f, DamageType.Corrosive, HitFlags.Melee);
            Assert.True(w.Bestiary.Knows(EnemyFamily.Rockback, DamageType.Corrosive));
            Assert.Equal(1.5f, w.Bestiary.KnownMultiplier(EnemyFamily.Rockback, DamageType.Corrosive, rock.Def));
        }
    }

    public class ThreatTests
    {
        [Fact]
        public void Tank_threat_multiplier_beats_equal_damage()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var maw = Scenarios.AddPack(w, new Vec2(30f, 40f), UnitKind.Cratermaw).Members[0];
            Combat.Deal(w, sq.Member(Role.Tank), maw, 20f, DamageType.Kinetic, HitFlags.Melee);
            Combat.Deal(w, sq.Member(Role.Melee), maw, 20f, DamageType.Kinetic, HitFlags.Melee);
            Assert.True(maw.Threat.Get(sq.Member(Role.Tank).Id) > maw.Threat.Get(sq.Member(Role.Melee).Id) * 2.5f);
        }

        [Fact]
        public void Taunt_puts_the_tank_on_top()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var maw = Scenarios.AddPack(w, new Vec2(30f, 40f), UnitKind.Cratermaw).Members[0];
            maw.Threat.Add(sq.Member(Role.Healer).Id, 500f);
            Combat.Taunt(w, maw, sq.Member(Role.Tank), 4f);
            Assert.True(maw.Threat.Get(sq.Member(Role.Tank).Id) > 500f);
            Assert.Equal(sq.Member(Role.Tank).Id, maw.CurrentTargetId);
        }

        [Fact]
        public void Healing_creates_threat_on_engaged_enemies()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var maw = Scenarios.AddPack(w, new Vec2(30f, 40f), UnitKind.Cratermaw).Members[0];
            sq.Engaged.Add(maw);
            var tank = sq.Member(Role.Tank);
            tank.Hp -= 100f;
            Combat.Heal(w, sq.Member(Role.Healer), tank, 60f);
            Assert.True(maw.Threat.Get(sq.Member(Role.Healer).Id) > 0f);
        }
    }

    public class InjuryTests
    {
        [Fact]
        public void Down_then_injured_then_killed_with_core_carried()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var maw = Scenarios.AddPack(w, new Vec2(30f, 60f), UnitKind.Cratermaw).Members[0];
            var bruiser = sq.Member(Role.Melee);

            Combat.Deal(w, maw, bruiser, 9999f, DamageType.Kinetic, HitFlags.Melee);
            Assert.Equal(LifeState.Downed, bruiser.Life);

            Combat.Revive(w, bruiser, 0.3f, null);
            Assert.Equal(LifeState.Active, bruiser.Life);
            Assert.True(bruiser.Injured);
            Assert.Equal(bruiser.Def.MaxHp * Balance.InjuredMaxHpMul, bruiser.MaxHp, 2);

            Combat.Deal(w, maw, bruiser, 9999f, DamageType.Kinetic, HitFlags.Melee);
            Assert.Equal(LifeState.Dead, bruiser.Life);
            Assert.Equal(CoreState.Carried, bruiser.Core);
            Assert.Contains(sq.Members, m => m.CarriedCores.Contains(bruiser.Id));
        }

        [Fact]
        public void Squad_wipe_drops_a_core_cache()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var maw = Scenarios.AddPack(w, new Vec2(30f, 60f), UnitKind.Cratermaw).Members[0];
            foreach (var m in sq.Members.ToList()) Combat.Deal(w, maw, m, 9999f, DamageType.Kinetic, HitFlags.Melee);
            Assert.True(sq.Wiped);
            Assert.Single(w.Caches);
            Assert.Equal(5, w.Caches[0].UnitIds.Count);
            Assert.All(sq.Members, m => Assert.Equal(LifeState.Dead, m.Life));
        }

        [Fact]
        public void Death_loses_unbanked_doctrine()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var maw = Scenarios.AddPack(w, new Vec2(30f, 60f), UnitKind.Cratermaw).Members[0];
            var u = sq.Member(Role.Siege);
            u.Doctrine.AddXp(CommandType.Kite, 40f);
            u.Doctrine.Bank();
            u.Doctrine.AddXp(CommandType.Kite, 30f);
            u.Injured = true;
            Combat.Deal(w, maw, u, 9999f, DamageType.Kinetic, HitFlags.Melee);
            Assert.Equal(40f, u.Doctrine.Total(CommandType.Kite), 2);
        }

        [Fact]
        public void Cradle_regrows_a_returned_core()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, w.BasePos + new Vec2(0f, 4f), 0, CommandType.DefensiveFormation);
            var maw = Scenarios.AddPack(w, new Vec2(80f, 80f), UnitKind.Cratermaw).Members[0];
            var u = sq.Member(Role.Melee);
            u.Doctrine.AddXp(CommandType.Charge, 70f);
            u.Doctrine.Bank();
            u.Injured = true;
            Combat.Deal(w, maw, u, 9999f, DamageType.Kinetic, HitFlags.Melee);
            w.Stock.Add(ResourceKind.Stone, 50f);
            w.Stock.Add(ResourceKind.Hide, 50f);
            for (int i = 0; i < 20 * 30; i++) w.Step();
            Assert.Equal(LifeState.Active, u.Life);
            Assert.Equal(2, u.Doctrine.Tier(CommandType.Charge)); // Doctrine survives rebirth
        }
    }

    public class LearningTests
    {
        [Fact]
        public void Doctrine_tiers_follow_xp_thresholds()
        {
            var d = new Doctrine();
            Assert.Equal(0, d.Tier(CommandType.Kite));
            Assert.Equal(1, d.AddXp(CommandType.Kite, Balance.DoctrineTierXp[1]));
            d.AddXp(CommandType.Kite, Balance.DoctrineTierXp[3]);
            Assert.Equal(3, d.Tier(CommandType.Kite));
            Assert.Equal(0, d.Tier(CommandType.Stack));
        }

        [Fact]
        public void Instinct_stays_inside_doctrine_bounds()
        {
            var rng = new SimRng(9);
            foreach (Role role in new[] { Role.Tank, Role.Healer, Role.Melee, Role.Siege, Role.Warwright })
            {
                var inst = new Instinct(role);
                for (int tier = 0; tier <= 5; tier++)
                {
                    for (int fight = 0; fight < 60; fight++)
                    {
                        inst.BeginFight();
                        foreach (var p in inst.Params) inst.Get(CommandType.Kite, EnemyFamily.Cratermaw, p.Param, tier, rng, true);
                        inst.EndFight(rng.Range(-2f, 2f));
                    }
                    foreach (var p in inst.Params)
                    {
                        inst.Bounds(p, tier, out float lo, out float hi);
                        float v = inst.Learned(CommandType.Kite, EnemyFamily.Cratermaw, p.Param, tier);
                        Assert.InRange(v, lo - 1e-4f, hi + 1e-4f);
                    }
                }
            }
        }

        [Fact]
        public void Locked_instinct_does_not_drift()
        {
            var rng = new SimRng(3);
            var inst = new Instinct(Role.Siege) { Locked = true };
            float before = inst.Learned(CommandType.Kite, EnemyFamily.Drone, InstinctParam.KeepDistance, 3);
            for (int i = 0; i < 20; i++)
            {
                inst.BeginFight();
                inst.Get(CommandType.Kite, EnemyFamily.Drone, InstinctParam.KeepDistance, 3, rng, true);
                inst.EndFight(rng.Range(-2f, 2f));
            }
            Assert.Equal(before, inst.Learned(CommandType.Kite, EnemyFamily.Drone, InstinctParam.KeepDistance, 3));
        }

        [Fact]
        public void Higher_tiers_allow_wider_instinct()
        {
            var inst = new Instinct(Role.Healer);
            var p = inst.Params.First(x => x.Param == InstinctParam.KeepDistance);
            inst.Bounds(p, 0, out float lo0, out float hi0);
            inst.Bounds(p, 5, out float lo5, out float hi5);
            Assert.True(hi5 - lo5 > hi0 - lo0);
            Assert.Equal(p.Min, lo5, 3);
            Assert.Equal(p.Max, hi5, 3);
        }

        [Fact]
        public void Fights_award_doctrine_and_report()
        {
            var w = Scenarios.Blank(5);
            var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation);
            Scenarios.AddPack(w, new Vec2(40f, 60f), UnitKind.Cratermaw, UnitKind.Cratermaw);
            w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = new Vec2(40f, 52f), Squads = { sq.Id } });
            for (int i = 0; i < 20 * 90 && w.Reports.Count == 0; i++) w.Step();
            Assert.Single(w.Reports);
            Assert.Equal("Victory", w.Reports[0].Result);
            Assert.All(sq.Members, m => Assert.True(m.Doctrine.Total(CommandType.DefensiveFormation) > 0f));
        }
    }

    public class WorldTests
    {
        [Fact]
        public void Path_goes_around_a_rampart_and_shots_are_blocked()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(40f, 30f), 0, CommandType.DefensiveFormation);
            var ww = sq.Member(Role.Warwright);
            var wall = StructureSystem.SpawnRampart(w, ww, new Vec2(40f, 40f), new Vec2(1f, 0f));
            Assert.False(w.Nav.SightClear(new Vec2(40f, 35f), new Vec2(40f, 45f)));
            var path = new System.Collections.Generic.List<Vec2>();
            Assert.True(w.Nav.FindPath(new Vec2(40f, 35f), new Vec2(40f, 45f), path, 0.6f));
            Assert.True(path.Count >= 2);
            Combat.DestroyStructure(w, wall);
            Assert.True(w.Nav.SightClear(new Vec2(40f, 35f), new Vec2(40f, 45f)));
        }

        [Fact]
        public void World_generation_is_deterministic()
        {
            var a = World.Create(42);
            var b = World.Create(42);
            Assert.Equal(a.Units.Count, b.Units.Count);
            Assert.Equal(a.Nodes.Count, b.Nodes.Count);
            Assert.Equal(a.ArenaCenter, b.ArenaCenter);
            Assert.Equal(a.Height.Sample(new Vec2(77f, 133f)), b.Height.Sample(new Vec2(77f, 133f)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(1234)]
        public void Generated_world_is_playable(int seed)
        {
            var w = World.Create(seed);
            Assert.Single(w.Squads);
            Assert.Equal(5, w.Squads[0].Members.Count);
            Assert.True(w.Groups.Count(g => g.IsCamp) >= 6, "enough camps");
            Assert.NotNull(w.Boss);
            Assert.True(w.Nodes.Count(n => n.Kind == ResourceKind.Ferrite) >= 5);
            var path = new System.Collections.Generic.List<Vec2>();
            Assert.True(w.Nav.FindPath(w.BasePos + new Vec2(0f, 8f), w.ArenaGate + (w.BasePos - w.ArenaGate).Normalized * 3f, path, 0.8f), "base reaches the raid gate");
            foreach (var g in w.Groups.Where(g => g.IsCamp))
                Assert.True(w.Nav.FindPath(w.BasePos + new Vec2(0f, 8f), g.Home, path, 0.8f), "base reaches " + g.Name);
        }

        [Fact]
        public void Long_run_does_not_throw()
        {
            var w = World.Create(3);
            var sq = w.Squads[0];
            var node = w.Nodes.First(n => n.Kind == ResourceKind.Ferrite);
            w.Issue(new PlayerOrder { Type = OrderType.Gather, TargetId = node.Id, Squads = { sq.Id } });
            for (int i = 0; i < 20 * 60 * 6; i++) w.Step();
            Assert.True(w.Stock[ResourceKind.Ferrite] > 10f);
        }
    }

    public class EconomyTests
    {
        [Fact]
        public void One_kin_band_and_the_war_hall_makes_it_stronger()
        {
            var w = World.Create(2);
            w.Stock.Add(ResourceKind.Scrap, 500f);
            w.Stock.Add(ResourceKind.Stone, 500f);
            w.Stock.Add(ResourceKind.Fiber, 500f);
            w.Stock.Add(ResourceKind.Hide, 500f);
            w.Stock.Add(ResourceKind.Ferrite, 500f);
            var tank = w.Squads[0].Member(Role.Tank);
            float hp = tank.MaxHp, mana = tank.MaxMana, stamina = tank.MaxStamina;
            w.Issue(new PlayerOrder { Type = OrderType.Recruit });
            w.Issue(new PlayerOrder { Type = OrderType.UpgradeWarHall });
            w.Step();
            Assert.False(w.Base.Recruiting);
            Assert.Single(w.Squads);
            Assert.Equal(2, w.Base.WarHallLevel);
            Assert.Equal(2, tank.HallLevel);
            Assert.Equal(hp * (1f + Balance.HallHealthPerLevel), tank.MaxHp, 2);
            Assert.Equal(mana * (1f + Balance.HallResourcePerLevel), tank.MaxMana, 2);
            Assert.Equal(stamina * (1f + Balance.HallResourcePerLevel), tank.MaxStamina, 2);
            Assert.Equal(1f, tank.HpFraction, 2);
        }

        [Fact]
        public void Smelter_and_bench_only_run_with_a_garrison()
        {
            var w = World.Create(2);
            w.Stock.Add(ResourceKind.Ferrite, 100f);
            w.Stock.Add(ResourceKind.Hide, 100f);
            w.Stock.Add(ResourceKind.Stone, 100f);
            for (int i = 0; i < 20 * 10; i++) w.Step();
            Assert.Equal(0f, w.Stock[ResourceKind.Alloy]);
            var sq = w.Squads[0];
            w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { sq.Id } });
            for (int i = 0; i < 20 * 40; i++) w.Step();
            Assert.True(sq.Garrisoned);
            Assert.True(w.Stock[ResourceKind.Alloy] >= 5f);
        }

        [Fact]
        public void Sigil_opens_the_raid_gate()
        {
            var w = World.Create(2);
            w.Stock.Add(ResourceKind.Alloy, 50f);
            w.Stock.Add(ResourceKind.Hide, 50f);
            w.Stock.Add(ResourceKind.Stone, 50f);
            Assert.True(w.Nav.BlocksMoveAt(w.ArenaGate));
            w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { w.Squads[0].Id } });
            w.Issue(new PlayerOrder { Type = OrderType.CraftSigil });
            for (int i = 0; i < 20 * 60; i++) w.Step();
            Assert.True(w.Boss.GateOpen);
            Assert.False(w.Nav.BlocksMoveAt(w.ArenaGate));
        }
    }

    public class BossTests
    {
        [Fact]
        public void Rustmaw_moves_through_its_phases()
        {
            var w = Scenarios.BossArena(1);
            var sq = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(0f, -12f), 2, CommandType.DefensiveFormation, 2);
            for (int i = 0; i < 40; i++) w.Step();
            Assert.Equal(1, w.Boss.Phase);
            w.Boss.Body.Hp = w.Boss.Body.MaxHp * 0.69f;
            for (int i = 0; i < 40; i++) w.Step();
            Assert.Equal(2, w.Boss.Phase);
            Assert.True(w.Boss.Burrowed);
            w.Boss.DebugSkipToPhase(w, 3);
            Assert.Equal(3, w.Boss.Phase);
            Assert.Equal(0f, w.Boss.Body.Armor);
            Assert.Equal(2f, w.Boss.DamageTakenMultiplier(w, null, HitFlags.None));
        }

        [Fact]
        public void Cores_left_in_the_raid_return_when_it_ends()
        {
            var w = Scenarios.BossArena(1);
            var sq = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(0f, -12f), 2, CommandType.DefensiveFormation, 2);
            for (int i = 0; i < 40; i++) w.Step();
            Assert.True(w.Boss.Active);
            foreach (var m in sq.Members.ToList()) Combat.Deal(w, w.Boss.Body, m, 99999f, DamageType.Kinetic, HitFlags.Melee);
            Assert.True(sq.Wiped);
            Assert.Single(w.Caches);
            for (int i = 0; i < 20 * 15; i++) w.Step();
            Assert.Equal(0, w.Boss.Phase);
            Assert.Empty(w.Caches);
            Assert.All(sq.Members, m => Assert.True(m.Core == CoreState.AtCradle || m.Life == LifeState.Active));
        }

        [Fact]
        public void A_sleeping_Rustmaw_does_not_pin_a_kin_band_at_the_gate()
        {
            var w = Scenarios.BossArena(1);
            var sq = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(0f, -(w.ArenaRadius + 12f)), 2, CommandType.DefensiveFormation, 2);
            // A stray heal or shot from outside the lair lands on its threat table.
            w.Boss.Body.Threat.Add(sq.Member(Role.Tank).Id, 50f);
            for (int i = 0; i < 20 * (int)(Balance.CombatEndDelay + 2f); i++) w.Step();
            Assert.Equal(0, w.Boss.Phase);
            Assert.False(sq.InCombat);
        }

        [Fact]
        public void Rustmaw_resets_when_everyone_leaves()
        {
            var w = Scenarios.BossArena(1);
            var sq = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(0f, -12f), 2, CommandType.DefensiveFormation, 2);
            for (int i = 0; i < 40; i++) w.Step();
            Assert.True(w.Boss.Active);
            foreach (var m in sq.Members) m.Pos = new Vec2(5f, 5f);
            for (int i = 0; i < 20 * 15; i++)
            {
                foreach (var m in sq.Members) m.Pos = new Vec2(5f, 5f);
                w.Step();
            }
            Assert.Equal(0, w.Boss.Phase);
            Assert.True(w.Boss.Body.Hp > w.Boss.Body.MaxHp * 0.99f); // a shot already in flight may still land
        }
    }
}
