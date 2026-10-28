using System.Linq;
using Kinforge.Sim;
using Xunit;

namespace Kinforge.Sim.Tests
{
    public class TitanTests
    {
        private static AbilitySlot TitanSlot(Unit u) => u.Slot(AbilityId.TitanForm);

        /// <summary>Grows the tank and runs the ramp to full size without stepping the rest of the world.</summary>
        private static void GrowFully(World w, Unit tank)
        {
            tank.Squad.InCombat = true;
            Titan.Begin(w, tank);
            for (int i = 0; i < 12; i++) Titan.Tick(w, tank, Balance.Dt);
        }

        private static Squad VeteranSquad(World w, Vec2 at, int tier = 3, CommandType stance = CommandType.DefensiveFormation) =>
            Scenarios.AddSquad(w, at, tier, stance, 2);

        [Fact]
        public void Titan_slot_unlocks_at_tier_3_and_never_relocks()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 2, CommandType.DefensiveFormation);
            var tank = sq.Member(Role.Tank);
            Assert.Equal(4, tank.Abilities.Length);
            Assert.True(tank.Abilities[2].Unlocked);
            Assert.False(TitanSlot(tank).Unlocked);
            Assert.All(sq.Members.Where(m => m != tank), m => Assert.Equal(3, m.Abilities.Length));

            // Earn tier 3 the hard way (unbanked), then lose it on death: the slot stays open.
            tank.Doctrine.AddXp(CommandType.Charge, Balance.DoctrineTierXp[3]);
            var opened = PlayerUnits.RefreshAbilitySlots(tank);
            Assert.Equal(AbilityId.TitanForm, opened.Def.Id);
            Assert.Null(PlayerUnits.RefreshAbilitySlots(tank));
            tank.Doctrine.LoseUnbanked();
            Assert.True(tank.Doctrine.MaxTier < 3);
            Assert.Null(PlayerUnits.RefreshAbilitySlots(tank)); // the refresh never closes a slot
            Assert.True(TitanSlot(tank).Unlocked);
        }

        [Fact]
        public void Grow_scales_body_and_keeps_health_fraction()
        {
            var w = Scenarios.Blank(1);
            var tank = VeteranSquad(w, new Vec2(30f, 30f)).Member(Role.Tank);
            float hp0 = tank.MaxHp, armor0 = tank.Armor;
            tank.Hp = hp0 * 0.5f;

            GrowFully(w, tank);

            Assert.Equal(1f, tank.TitanT);
            Assert.Equal(tank.Def.Radius * Balance.TitanScale, tank.Radius, 3);
            Assert.Equal(hp0 * Balance.TitanHpMul, tank.MaxHp, 2);
            Assert.Equal(0.5f, tank.HpFraction, 2);
            Assert.Equal(armor0 + Balance.TitanArmor, tank.Armor, 2);
            Assert.Equal(Balance.TitanMass, tank.Mass, 3);
            Assert.Equal(tank.Def.ThreatMul * Balance.TitanThreatMul, tank.ThreatMul, 3);
            Assert.Equal(AbilityDefs.Get(AbilityId.TitanForm).Cooldown, TitanSlot(tank).Cooldown);

            // Run the form out (still in combat): back to exactly the normal body, health fraction kept.
            for (int i = 0; i < 20 * 16; i++) Titan.Tick(w, tank, Balance.Dt);
            Assert.Equal(0f, tank.TitanTime);
            Assert.Equal(0f, tank.TitanT);
            Assert.Equal(tank.Def.Radius, tank.Radius);
            Assert.Equal(hp0, tank.MaxHp, 2);
            Assert.Equal(0.5f, tank.HpFraction, 2);
            Assert.Null(Titan.CheckInvariants(w));
        }

        [Fact]
        public void The_form_ends_soon_after_the_fight()
        {
            var w = Scenarios.Blank(1);
            var tank = VeteranSquad(w, new Vec2(30f, 30f)).Member(Role.Tank);
            GrowFully(w, tank);
            tank.Squad.InCombat = false;
            for (int i = 0; i < 20 * 3; i++) Titan.Tick(w, tank, Balance.Dt);
            Assert.Equal(0f, tank.TitanT);
            Assert.Equal(tank.Def.Radius, tank.Radius);
        }

        [Fact]
        public void Cancelled_cast_does_not_burn_the_cooldown()
        {
            var w = Scenarios.Blank(1);
            var tank = VeteranSquad(w, new Vec2(30f, 30f)).Member(Role.Tank);
            Assert.True(ActionSystem.StartAbility(w, tank, AbilityId.TitanForm, 0, tank.Pos, Vec2.Forward));
            Assert.Equal(Balance.TitanCastCooldown, TitanSlot(tank).Cooldown);
            Combat.Stun(w, tank, 1f);
            Assert.Equal(UnitAction.None, tank.Action);
            Assert.True(TitanSlot(tank).Cooldown <= Balance.TitanCastCooldown);
            Assert.Equal(0f, tank.TitanTime);
        }

        [Fact]
        public void No_room_to_grow_fizzles()
        {
            var w = Scenarios.Blank(1);
            var sq = VeteranSquad(w, new Vec2(40f, 30f));
            var tank = sq.Member(Role.Tank);
            tank.Pos = new Vec2(40.5f, 40.5f);
            // Between two ramparts 2.2 m apart: no room here or a step away.
            StructureSystem.SpawnRampart(w, sq.Member(Role.Warwright), new Vec2(40.5f, 41.6f), new Vec2(1f, 0f), 9f);
            StructureSystem.SpawnRampart(w, sq.Member(Role.Warwright), new Vec2(40.5f, 39.4f), new Vec2(1f, 0f), 9f);
            Assert.False(Titan.HasRoomNear(w, tank.Pos));
            sq.InCombat = true;
            Titan.Begin(w, tank);
            Assert.Equal(0f, tank.TitanTime);
            Assert.Equal(Balance.TitanFizzleCooldown, TitanSlot(tank).Cooldown);
            Assert.Contains(w.Events, e => e.Type == SimEventType.TitanFade && e.A == tank.Id && e.Value == Titan.FadeNoRoom);
        }

        [Fact]
        public void Downed_or_garrisoned_titans_return_to_normal_size()
        {
            var w = Scenarios.Blank(1);
            var sq = VeteranSquad(w, new Vec2(30f, 30f));
            var tank = sq.Member(Role.Tank);
            var mender = sq.Member(Role.Healer);
            GrowFully(w, tank);
            Combat.Deal(w, null, tank, 99999f, DamageType.Thermal, HitFlags.Environment);
            Assert.Equal(LifeState.Downed, tank.Life);
            Assert.Equal(0f, tank.TitanT);
            Assert.Equal(0f, tank.TitanTime);
            Assert.Equal(tank.Def.Radius, tank.Radius);
            Combat.Revive(w, tank, Balance.StabilizeHpFraction, mender);
            // Revived at normal size; wounds from the huge hit cap how far it comes back.
            Assert.Equal(System.Math.Min(tank.MaxHp * Balance.StabilizeHpFraction, tank.HealCap), tank.Hp, 2);
            Assert.Null(Titan.CheckInvariants(w));

            GrowFully(w, tank);
            Assert.Equal(1f, tank.TitanT);
            Assert.False(ActionSystem.StartAbility(w, tank, AbilityId.TitanForm, 0, tank.Pos, Vec2.Forward)); // cooling down
            SquadSystem.Garrison(w, sq);
            Assert.True(tank.Garrisoned);
            Assert.Equal(0f, tank.TitanT);
            Assert.Equal(tank.Def.Radius, tank.Radius);
            Assert.Null(Titan.CheckInvariants(w));
        }

        [Fact]
        public void Growing_as_the_squad_marches_off_fizzles_without_the_full_cooldown()
        {
            var w = Scenarios.Blank(1);
            var sq = VeteranSquad(w, new Vec2(30f, 30f));
            var tank = sq.Member(Role.Tank);
            sq.InCombat = false; // the fight ended during the cast
            Titan.Begin(w, tank);
            Assert.Equal(0f, tank.TitanTime);
            Assert.Equal(Balance.TitanFizzleCooldown, TitanSlot(tank).Cooldown);
            Assert.Contains(w.Events, e => e.Type == SimEventType.TitanFade && e.Value == Titan.FadeLeftFight);
        }

        [Fact]
        public void A_titan_is_heavy_and_hard_to_stun()
        {
            var w = Scenarios.Blank(1);
            var tank = VeteranSquad(w, new Vec2(30f, 30f)).Member(Role.Tank);
            Combat.Knockback(w, tank, Vec2.Forward, 3.5f);
            float normal = tank.ForcedTime * tank.ForcedVel.Length;
            tank.ForcedTime = 0f;
            GrowFully(w, tank);
            Combat.Knockback(w, tank, Vec2.Forward, 3.5f);
            float grown = tank.ForcedTime * tank.ForcedVel.Length;
            Assert.Equal(3.5f, normal, 2);
            Assert.True(grown < normal / 3.4f, $"grown knockback {grown}");
            Combat.Stun(w, tank, 1f);
            Assert.Equal(Balance.TitanStunMul, tank.Status.StunTime, 3);
        }

        [Fact]
        public void Enemy_shots_slam_into_the_titans_shield()
        {
            foreach (bool grown in new[] { false, true })
            {
                var w = Scenarios.Blank(1);
                var sq = VeteranSquad(w, new Vec2(30f, 30f));
                var tank = sq.Member(Role.Tank);
                var mender = sq.Member(Role.Healer);
                var drone = Scenarios.AddPack(w, new Vec2(30f, 52f), UnitKind.SalvageDrone).Members[0];
                foreach (var m in sq.Members) m.Pos = new Vec2(60f, 10f + 2f * sq.Members.IndexOf(m));
                mender.Pos = new Vec2(30f, 30f);
                drone.Pos = new Vec2(30f, 50f);
                tank.Pos = new Vec2(31.5f, 40f); // 1.5 m off the line of fire
                if (grown) GrowFully(w, tank);
                float tankHp = tank.Hp, menderHp = mender.Hp;
                ProjectileSystem.Fire(w, drone, 0, mender, Vec2.Zero, false, 16f, DamageType.Kinetic, 30f, 25f, 0f, ProjectileVisual.Rivet, HitFlags.None);
                for (int i = 0; i < 40; i++) ProjectileSystem.Update(w);
                if (grown)
                {
                    Assert.True(tank.Hp < tankHp, "a grown Titan should catch the shot");
                    Assert.Equal(menderHp, mender.Hp);
                }
                else
                {
                    Assert.Equal(tankHp, tank.Hp);
                    Assert.True(mender.Hp < menderHp, "a normal tank should not");
                }
            }
        }

        private static (World w, Squad sq, Vec2 dir) RaidAtPhase1(int tier, CommandType stance)
        {
            var w = Scenarios.BossArena(1);
            var sq = VeteranSquad(w, w.ArenaCenter + new Vec2(0f, -12f), tier, stance);
            for (int i = 0; i < 40; i++) w.Step();
            Assert.Equal(1, w.Boss.Phase);
            // Line everyone up south of the boss, out of swipe reach.
            var body = w.Boss.Body;
            body.Pos = w.ArenaCenter;
            var dir = new Vec2(0f, -1f);
            int k = 0;
            foreach (var m in sq.Members) m.Pos = w.ArenaCenter + new Vec2(-8f + 4f * k++, -17f);
            // Clear anything the engineers already built, so each test places its own walls.
            foreach (var st in w.Structures.Where(st => st.Faction == Faction.Player && !st.Removed).ToList()) Combat.DestroyStructure(w, st);
            return (w, sq, dir);
        }

        [Fact]
        public void A_titan_stops_rustmaws_charge_dead()
        {
            var (w, sq, dir) = RaidAtPhase1(3, CommandType.DefensiveFormation);
            var tank = sq.Member(Role.Tank);
            var mender = sq.Member(Role.Healer);
            tank.Pos = w.ArenaCenter + dir * 6f;
            mender.Pos = w.ArenaCenter + dir * 14f;
            GrowFully(w, tank);
            float menderHp = mender.Hp;
            Assert.True(w.Boss.ClashReady(w));
            tank.TitanBraceUntil = w.Time + 5f; // what its AI does while reading the tell
            w.Boss.DebugChargeAt(w, dir, 20f);

            bool clashed = false;
            for (int i = 0; i < 10 && !clashed; i++)
            {
                w.Step();
                clashed = w.Events.Any(e => e.Type == SimEventType.TitanClash && e.A == tank.Id && e.Value == 0f);
            }
            Assert.True(clashed);
            Assert.True(tank.TitanClashed);
            Assert.True(w.Boss.Staggered);
            Assert.True(w.Boss.VentsExposed);
            Assert.False(w.Boss.ClashReady(w));
            Assert.Equal(menderHp, mender.Hp);
            Assert.True(Vec2.Distance(w.Boss.Body.Pos, mender.Pos) > 5f);

            // Within the immunity window the next charge goes through the giant.
            for (int i = 0; i < 20 * 3; i++) w.Step();
            tank.TitanClashed = false;
            w.Boss.DebugChargeAt(w, (tank.Pos - w.Boss.Body.Pos).Normalized, 12f);
            for (int i = 0; i < 20; i++)
            {
                w.Step();
                Assert.DoesNotContain(w.Events, e => e.Type == SimEventType.TitanClash);
            }
        }

        [Fact]
        public void A_rampart_behind_the_titan_makes_an_anvil()
        {
            var (w, sq, dir) = RaidAtPhase1(3, CommandType.DefensiveFormation);
            var tank = sq.Member(Role.Tank);
            tank.Pos = w.ArenaCenter + dir * 6f;
            GrowFully(w, tank);
            var wall = StructureSystem.SpawnRampart(w, sq.Member(Role.Warwright), tank.Pos + dir * (tank.Radius + 1.5f), dir.Right);
            tank.TitanBraceUntil = w.Time + 5f;
            w.Boss.DebugChargeAt(w, dir, 20f);
            bool anvil = false;
            for (int i = 0; i < 10 && !anvil; i++)
            {
                w.Step();
                anvil = w.Events.Any(e => e.Type == SimEventType.TitanClash && e.Value == 1f);
            }
            Assert.True(anvil);
            Assert.True(wall.Removed);
            for (int i = 0; i < 20 * 3; i++) w.Step(); // a plain clash staggers for 2 s, an anvil for 4 s
            Assert.True(w.Boss.Staggered);
        }

        [Fact]
        public void A_titan_shelters_three_kin_from_a_fire_wave()
        {
            var w = Scenarios.BossArena(1);
            var sq = VeteranSquad(w, w.ArenaCenter + new Vec2(0f, -12f));
            for (int i = 0; i < 40; i++) w.Step();
            w.Boss.DebugSkipToPhase(w, 3);
            var body = w.Boss.Body;
            body.Pos = w.ArenaCenter;
            var tank = sq.Member(Role.Tank);
            tank.Pos = w.ArenaCenter + new Vec2(0f, -6f);
            GrowFully(w, tank);
            var kin = sq.Members.Where(m => m != tank).ToList();
            for (int i = 0; i < kin.Count; i++)
            {
                kin[i].Pos = tank.Pos + new Vec2(0f, -(tank.Radius + 0.6f + 0.5f * i));
                kin[i].Hp = kin[i].MaxHp;
            }
            tank.Hp = tank.MaxHp;
            var before = sq.Members.ToDictionary(m => m.Id, m => m.Hp);

            w.Boss.DebugBlastNow(w);

            float full = Balance.RustmawBlast * w.Boss.DamageDealtMultiplier;
            for (int i = 0; i < kin.Count; i++)
            {
                float taken = before[kin[i].Id] - kin[i].Hp;
                Assert.Equal(i < Balance.TitanShelterCap ? full * Balance.TitanShelterMul : full, taken, 1);
            }
            Assert.Equal(full, before[tank.Id] - tank.Hp, 1);
            Assert.Equal(Balance.TitanShelterCap, w.Events.Count(e => e.Type == SimEventType.TitanShelter));
        }

        [Fact]
        public void A_grown_tank_stands_firm_in_a_charge_lane_instead_of_dodging()
        {
            var (w, sq, dir) = RaidAtPhase1(0, CommandType.Charge);
            var tank = sq.Member(Role.Tank);
            TitanSlot(tank).Unlocked = true; // a tier-3 veteran in another command, fighting under Charge (tier 0)
            tank.Pos = w.ArenaCenter + dir * 7f + dir.Right * 1f;
            GrowFully(w, tank);
            w.AddTelegraph(new Telegraph
            {
                Shape = TelegraphShape.Line, Pos = w.ArenaCenter, Dir = dir, Length = 20f, Width = 4.6f, Duration = 1.7f,
                Threatens = Faction.Player, OwnerId = w.Boss.Body.Id, Label = "Line Charge", Catchable = true,
            });
            w.InspectedUnitId = tank.Id;
            PlayerBrain.Think(w, tank);
            Assert.Equal(IntentKind.Move, tank.Intent.Kind);
            Assert.Equal(tank.Pos, tank.Intent.Point);
            Assert.Equal("Stand firm", tank.DebugLabel[0]);
            Assert.True(tank.TitanBraceUntil > w.Time);
        }

        [Fact]
        public void An_unbraced_titan_is_trampled_by_the_charge()
        {
            var (w, sq, dir) = RaidAtPhase1(3, CommandType.DefensiveFormation);
            var tank = sq.Member(Role.Tank);
            tank.Pos = w.ArenaCenter + dir * 6f;
            GrowFully(w, tank);
            w.Boss.DebugChargeAt(w, dir, 20f);
            for (int i = 0; i < 20; i++)
            {
                w.Step();
                Assert.DoesNotContain(w.Events, e => e.Type == SimEventType.TitanClash);
            }
            Assert.True(tank.Hp < tank.MaxHp);
        }

        [Fact]
        public void A_titan_steps_aside_for_a_rampart_further_down_the_lane()
        {
            var (w, sq, dir) = RaidAtPhase1(0, CommandType.Charge);
            var tank = sq.Member(Role.Tank);
            TitanSlot(tank).Unlocked = true;
            tank.Pos = w.ArenaCenter + dir * 6f;
            GrowFully(w, tank);
            StructureSystem.SpawnRampart(w, sq.Member(Role.Warwright), w.ArenaCenter + dir * 15f, dir.Right);
            w.AddTelegraph(new Telegraph
            {
                Shape = TelegraphShape.Line, Pos = w.ArenaCenter, Dir = dir, Length = 20f, Width = 4.6f, Duration = 1.7f,
                Threatens = Faction.Player, OwnerId = w.Boss.Body.Id, Label = "Line Charge", Catchable = true,
            });
            w.InspectedUnitId = tank.Id;
            PlayerBrain.Think(w, tank);
            Assert.NotEqual("Stand firm", tank.DebugLabel[0]);
            Assert.True(tank.TitanBraceUntil < w.Time);
        }

        [Fact]
        public void A_veteran_titan_steps_into_the_lane_to_hold_the_line()
        {
            var (w, sq, dir) = RaidAtPhase1(4, CommandType.DefensiveFormation);
            var tank = sq.Member(Role.Tank);
            var mender = sq.Member(Role.Healer);
            tank.Pos = w.ArenaCenter + dir * 7f + dir.Right * 3.8f;
            mender.Pos = w.ArenaCenter + dir * 15f;
            GrowFully(w, tank);
            w.AddTelegraph(new Telegraph
            {
                Shape = TelegraphShape.Line, Pos = w.ArenaCenter, Dir = dir, Length = 20f, Width = 4.6f, Duration = 1.7f,
                Threatens = Faction.Player, OwnerId = w.Boss.Body.Id, Label = "Line Charge", Catchable = true,
            });
            w.InspectedUnitId = tank.Id;
            PlayerBrain.Think(w, tank);
            Assert.Equal(IntentKind.Move, tank.Intent.Kind);
            Assert.Equal("Hold the line", tank.DebugLabel[0]);
            Assert.True(tank.TitanBraceUntil > w.Time);
            float lat = System.MathF.Abs(Vec2.Dot(tank.Intent.Point - w.ArenaCenter, dir.Right));
            Assert.True(lat < 1f, $"hold point {lat:0.00} m off the lane centre");
        }

        [Fact]
        public void A_braced_titan_far_down_the_lane_still_stops_the_charge()
        {
            // The AI braces during the tell (nothing set by hand) and Rustmaw needs most of a second to arrive.
            var (w, sq, dir) = RaidAtPhase1(4, CommandType.HoldPosition);
            var tank = sq.Member(Role.Tank);
            tank.Pos = w.ArenaCenter + dir * 15f;
            sq.Member(Role.Healer).Pos = w.ArenaCenter + dir * 19f;
            GrowFully(w, tank);
            w.Boss.DebugChargeWindupAt(w, dir, 25f);
            bool clashed = false;
            for (int i = 0; i < 20 * 4 && !clashed; i++)
            {
                w.Step();
                clashed = w.Events.Any(e => e.Type == SimEventType.TitanClash && e.A == tank.Id);
            }
            Assert.True(clashed);
        }

        [Fact]
        public void With_titan_form_off_no_tank_ever_grows()
        {
            var w = Scenarios.BossArena(7);
            w.TitanEnabled = false;
            var a = VeteranSquad(w, w.ArenaCenter + new Vec2(-6f, -30f), 4);
            var b = VeteranSquad(w, w.ArenaCenter + new Vec2(6f, -30f), 4);
            w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + new Vec2(0f, -12f), Squads = { a.Id, b.Id } });
            for (int i = 0; i < 20 * 90; i++)
            {
                w.Step();
                Assert.DoesNotContain(w.Events, e => (e.Type == SimEventType.AbilityCast && e.Ability == AbilityId.TitanForm)
                                                     || e.Type == SimEventType.TitanRise || e.Type == SimEventType.TitanFade
                                                     || (e.Type == SimEventType.AbilityUnlocked && e.Ability == AbilityId.TitanForm));
                Assert.All(w.Units, u => Assert.Equal(0f, u.TitanT));
            }
        }

        [Fact]
        public void A_squad_whose_carrier_died_can_finish_returning_home()
        {
            var w = Scenarios.Blank(1);
            var sq = Scenarios.AddSquad(w, new Vec2(30f, 30f), 0, CommandType.DefensiveFormation);
            var bruiser = sq.Member(Role.Melee);
            bruiser.Carry.Add(ResourceKind.Ferrite, 10f);
            bruiser.Life = LifeState.Dead;
            Assert.Equal(0f, sq.CarryLoad);
        }

        [Fact]
        public void Raids_with_titans_are_deterministic_and_consistent()
        {
            string Run()
            {
                var w = Scenarios.BossArena(7);
                var a = VeteranSquad(w, w.ArenaCenter + new Vec2(-6f, -30f));
                var b = VeteranSquad(w, w.ArenaCenter + new Vec2(6f, -30f));
                w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + new Vec2(0f, -12f), Squads = { a.Id, b.Id } });
                int rises = 0;
                for (int i = 0; i < 20 * 90; i++)
                {
                    w.Step();
                    rises += w.Events.Count(e => e.Type == SimEventType.TitanRise);
                    Assert.Null(Titan.CheckInvariants(w));
                }
                Assert.True(rises > 0, "veteran tanks should grow during a raid");
                return string.Join(";", w.Units.Select(u => $"{u.Id}:{u.Pos.X:0.###},{u.Pos.Z:0.###},{u.Hp:0.##}")) + $"|{rises}";
            }
            Assert.Equal(Run(), Run());
        }
    }
}
