namespace Kinforge.Sim
{
    /// <summary>Cooldowns, damage over time, crowd-control timers, resting and passive recovery.</summary>
    public static class StatusSystem
    {
        public static void Update(World w)
        {
            float dt = Balance.Dt;
            for (int i = 0; i < w.Units.Count; i++)
            {
                var u = w.Units[i];
                if (u.Life != LifeState.Active || u.Removed) continue;

                if (u.Garrisoned)
                {
                    Rest(w, u, dt, Balance.RestBaseMultiplier);
                    continue;
                }

                if (u.AttackCooldown > 0f) u.AttackCooldown -= dt;
                if (u.Abilities != null)
                {
                    foreach (var s in u.Abilities)
                    {
                        if (s.Cooldown > 0f) s.Cooldown -= dt;
                    }
                }
                if (u.TitanTime > 0f || u.TitanT > 0f) Titan.Tick(w, u, dt);

                ref var st = ref u.Status;

                if (st.BurnTime > 0f)
                {
                    st.BurnTime -= dt;
                    Combat.Deal(w, w.FindEntity(st.BurnSource), u, st.BurnDps * dt, DamageType.Thermal, HitFlags.DoT);
                    if (u.Life != LifeState.Active) continue;
                    if (u.Status.BurnTime <= 0f) u.Status.BurnDps = 0f;
                }
                if (u.Status.BleedTime > 0f)
                {
                    u.Status.BleedTime -= dt;
                    Combat.Deal(w, w.FindEntity(u.Status.BleedSource), u, u.Status.BleedDps * dt, DamageType.Kinetic, HitFlags.DoT);
                    if (u.Life != LifeState.Active) continue;
                }

                ref var s2 = ref u.Status;
                if (s2.CryoTime > 0f && (s2.CryoTime -= dt) <= 0f) s2.CryoStacks = 0;
                if (s2.FrozenTime > 0f) s2.FrozenTime -= dt;
                if (s2.StunTime > 0f) s2.StunTime -= dt;
                if (s2.RootTime > 0f) s2.RootTime -= dt;
                if (s2.SlowTime > 0f && (s2.SlowTime -= dt) <= 0f) s2.SlowPct = 0f;
                if (s2.ShredTime > 0f && (s2.ShredTime -= dt) <= 0f) s2.ShredStacks = 0;
                if (s2.TauntTime > 0f && (s2.TauntTime -= dt) <= 0f) s2.TauntBy = 0;

                if (!u.IsPlayer) continue;

                if (u.Action == UnitAction.Resting)
                {
                    float mul = 1f;
                    if (w.InBase(u.Pos)) mul = Balance.RestBaseMultiplier;
                    var mender = u.Squad?.ActiveMember(Role.Healer);
                    if (mender != null && mender != u && Vec2.DistanceSq(mender.Pos, u.Pos) < 100f) mul += Balance.RestMenderBonus;
                    Rest(w, u, dt, mul);
                }
                else if (u.Squad != null && !u.Squad.InCombat && w.Time - u.LastHurtTime > 6f)
                {
                    Combat.Heal(w, null, u, u.MaxHp * Balance.OutOfCombatRegen * dt, false);
                }
            }
        }

        private static void Rest(World w, Unit u, float dt, float mul)
        {
            Combat.Heal(w, null, u, u.MaxHp * Balance.RestHealPerSecond * mul * dt, false);
            if (!u.Injured) return;
            u.RestProgress += dt * mul;
            if (u.RestProgress >= Balance.RestSecondsToClearInjury)
            {
                u.Injured = false;
                u.RestProgress = 0f;
                w.Emit(SimEventType.Revived, u.Id, 0, u.Pos, 1f);
            }
        }
    }
}
