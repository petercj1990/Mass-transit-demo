namespace Kinforge.Sim
{
    /// <summary>
    /// Cooldowns, damage over time, crowd-control timers, resting and passive recovery, mana and
    /// stamina regeneration, and wound recovery (only at base).
    /// </summary>
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
                    // Home: wounds close, mana and stamina refill, and the healing ceiling comes back up.
                    u.Wound = System.Math.Max(0f, u.Wound - Balance.WoundHealGarrison * dt);
                    Regen(u, dt, Balance.ManaRegenGarrison, Balance.StaminaRegenResting, true);
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

                bool inCombat = u.Squad != null && u.Squad.InCombat;
                bool resting = u.Action == UnitAction.Resting;
                if (resting) Regen(u, dt, Balance.ManaRegenResting, Balance.StaminaRegenResting, true);
                else if (inCombat) Regen(u, dt, Balance.ManaRegenCombat, Balance.StaminaRegenCombat, w.Time - u.LastExertTime > Balance.StaminaRegenDelay);
                else Regen(u, dt, Balance.ManaRegenOutOfCombat, Balance.StaminaRegenOutOfCombat, true);
                if (!inCombat && u.Wound > 0f && w.InBase(u.Pos)) u.Wound = System.Math.Max(0f, u.Wound - Balance.WoundHealAtBase * dt);
                if (u.Hp > u.HealCap) u.Hp = System.Math.Max(1f, u.HealCap);

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

        private static void Regen(Unit u, float dt, float manaRate, float staminaRate, bool staminaBack)
        {
            float mm = u.MaxMana;
            if (u.Mana < mm) u.Mana = System.Math.Min(mm, u.Mana + mm * manaRate * dt);
            float ms = u.MaxStamina;
            if (staminaBack && u.Stamina < ms) u.Stamina = System.Math.Min(ms, u.Stamina + ms * staminaRate * dt);
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
