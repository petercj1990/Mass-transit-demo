using System.Linq;
using Kinforge.Sim;
using Xunit;

namespace Kinforge.Sim.Tests
{
    /// <summary>The push-your-luck loop: wounds, mana and stamina, and steering the kin-band directly.</summary>
    public class ExpeditionTests
    {
        [Fact]
        public void Wounds_cap_healing_until_the_squad_rests_at_base()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation);
            var bruiser = sq.Member(Role.Melee);
            var maw = Scenarios.AddPack(w, new Vec2(40f, 70f), UnitKind.Cratermaw).Members[0];
            float dealt = Combat.Deal(w, maw, bruiser, 100f, DamageType.Thermal, HitFlags.Melee);
            Assert.Equal(dealt * Balance.WoundFraction / bruiser.MaxHp, bruiser.Wound, 4);

            Combat.Heal(w, null, bruiser, 9999f);
            Assert.Equal(bruiser.HealCap, bruiser.Hp, 2);
            Assert.True(bruiser.Hp < bruiser.MaxHp);

            // Resting in the field does not close wounds...
            for (int i = 0; i < 20 * 5; i++) w.Step();
            Assert.True(bruiser.Wound > 0f);
            // ...a garrison at base does.
            SquadSystem.Garrison(w, sq);
            for (int i = 0; i < 20 * 5; i++) w.Step();
            Assert.Equal(0f, bruiser.Wound);
            Combat.Heal(w, null, bruiser, 9999f);
            Assert.Equal(bruiser.MaxHp, bruiser.Hp, 2);
        }

        [Fact]
        public void Being_downed_adds_a_deep_wound()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation);
            var mender = sq.Member(Role.Healer);
            Combat.Deal(w, null, mender, 99999f, DamageType.Thermal, HitFlags.Environment);
            Assert.Equal(LifeState.Downed, mender.Life);
            Assert.Equal(Balance.MaxWound, mender.Wound, 3);
        }

        [Fact]
        public void Abilities_spend_mana_and_stamina_and_wait_when_unaffordable()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 2, CommandType.DefensiveFormation);
            var tank = sq.Member(Role.Tank);
            var ww = sq.Member(Role.Warwright);
            float stamina = tank.Stamina;
            Assert.True(ActionSystem.StartAbility(w, tank, AbilityId.TauntRoar, 0, tank.Pos, Vec2.Forward));
            Assert.Equal(stamina - AbilityDefs.Get(AbilityId.TauntRoar).StaminaCost, tank.Stamina, 2);

            ww.Mana = 10f;
            Assert.False(ww.AbilityReady(AbilityId.RaiseRampart));
            ww.Mana = 100f;
            Assert.True(ww.AbilityReady(AbilityId.RaiseRampart));
            Assert.True(ActionSystem.StartAbility(w, ww, AbilityId.RaiseRampart, 0, ww.Pos + new Vec2(0f, 4f), Vec2.Forward));
            Assert.Equal(100f - AbilityDefs.Get(AbilityId.RaiseRampart).ManaCost, ww.Mana, 2);

            // Enemies have no resources and are never blocked by them.
            var maw = Scenarios.AddPack(w, new Vec2(40f, 70f), UnitKind.Cratermaw).Members[0];
            Assert.True(maw.CanAfford(AbilityDefs.Get(AbilityId.Pounce)));
        }

        [Fact]
        public void Exhausted_vhorn_move_slower()
        {
            var w = Scenarios.Blank(1);
            var bruiser = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation).Member(Role.Melee);
            float fresh = UnitMotor.BaseSpeed(bruiser);
            bruiser.Stamina = 0f;
            Assert.True(bruiser.Exhausted);
            Assert.Equal(fresh * Balance.ExhaustedSpeedMul, UnitMotor.BaseSpeed(bruiser), 3);
        }

        [Fact]
        public void Mana_and_stamina_come_back_faster_out_of_combat()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation);
            var mender = sq.Member(Role.Healer);
            mender.Mana = 0f;
            for (int i = 0; i < 20 * 4; i++) w.Step();
            float expected = mender.MaxMana * Balance.ManaRegenOutOfCombat * 4f;
            Assert.InRange(mender.Mana, expected * 0.9f, expected * 1.1f);
        }

        [Fact]
        public void Steering_walks_the_banner_and_cancels_travel_orders()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation);
            w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = new Vec2(40f, 90f), Squads = { sq.Id } });
            w.Step();
            Assert.Equal(SquadTask.Move, sq.Task);
            Vec2 start = sq.Anchor;
            for (int i = 0; i < 20 * 3; i++)
            {
                w.Issue(new PlayerOrder { Type = OrderType.Steer, Pos = new Vec2(1f, 0f), Squads = { sq.Id } });
                w.Step();
            }
            Assert.Equal(SquadTask.Idle, sq.Task);
            Assert.True(sq.Anchor.X - start.X > 6f, $"banner moved {sq.Anchor.X - start.X:0.0} m");
            Assert.True(sq.Centroid.X - start.X > 4f, "the kin follow the banner");
            // Released (the input layer sends a zero steer): the banner stops.
            w.Issue(new PlayerOrder { Type = OrderType.Steer, Pos = Vec2.Zero, Squads = { sq.Id } });
            w.Step();
            Vec2 stop = sq.Anchor;
            for (int i = 0; i < 20; i++) w.Step();
            Assert.True(Vec2.Distance(stop, sq.Anchor) < 0.01f);
        }

        [Fact]
        public void One_geared_kin_band_can_slay_Rustmaw_but_not_without_ferrite()
        {
            bool Raid(int seed, int gear)
            {
                var w = Scenarios.BossArena(seed);
                var sq = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(0f, -30f), 3, CommandType.DefensiveFormation, gear);
                PlayerBase.ApplyHallLevel(sq, Balance.MaxWarHallLevel);
                w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + new Vec2(0f, -12f), Squads = { sq.Id } });
                for (int t = 0; t < 20 * 400 && !w.Victory && !sq.Wiped; t++) w.Step();
                return w.Victory;
            }
            var seeds = Enumerable.Range(500, 6).ToArray();
            int geared = seeds.AsParallel().Count(s => Raid(s, 2));
            int scrap = seeds.AsParallel().Count(s => Raid(s, 1));
            Assert.True(geared >= 3, $"ferrite-geared kin-band won {geared}/6");
            Assert.True(scrap <= 1, $"scrap-geared kin-band won {scrap}/6");
        }

        [Fact]
        public void A_wiped_kin_band_comes_home_and_regrows_even_with_an_empty_stockpile()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(60f, 60f), 1, CommandType.DefensiveFormation);
            foreach (var m in sq.Members.ToList()) Combat.Deal(w, null, m, 99999f, DamageType.Thermal, HitFlags.Environment);
            Assert.True(sq.Wiped);
            Assert.Single(w.Caches);
            Assert.False(w.Stock.Has(PlayerBase.RegrowCost));

            for (int i = 0; i < 20 * (int)(Balance.WipeRecallSeconds + 1f); i++) w.Step();
            Assert.Empty(w.Caches);
            Assert.True(w.Base.RegrowStarved);

            int regrowAll = (int)(Balance.RegrowSeconds * Balance.StarvedRegrowMul * sq.Members.Count) + 2;
            for (int i = 0; i < 20 * regrowAll; i++) w.Step();
            Assert.False(sq.Wiped);
            Assert.All(sq.Members, m => Assert.Equal(LifeState.Active, m.Life));
        }

        [Fact]
        public void Veterans_keep_a_reserve_green_units_spend_freely()
        {
            var w = Scenarios.Blank(1);
            var green = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation).Member(Role.Warwright);
            var vet = Scenarios.AddSquad(w, new Vec2(80f, 40f), 3, CommandType.DefensiveFormation).Member(Role.Warwright);
            green.Mana = vet.Mana = 30f;
            var c = new BrainContext();
            c.Setup(w, green);
            Assert.Equal(0f, c.ResourcePenalty(AbilityId.RaiseRampart));
            c.Setup(w, vet);
            Assert.True(c.ResourcePenalty(AbilityId.RaiseRampart) > 0f);
            vet.Mana = vet.MaxMana;
            c.Setup(w, vet);
            Assert.Equal(0f, c.ResourcePenalty(AbilityId.RaiseRampart));
        }
    }
}
