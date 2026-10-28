namespace Kinforge.Sim
{
    /// <summary>
    /// Every tunable number that is not a per-unit stat lives here, so playtest feedback can be
    /// turned into edits in one place. "Starting value" in the design doc maps to these defaults.
    /// </summary>
    public static class Balance
    {
        // --- Simulation -------------------------------------------------------------------------
        public const float TickRate = 20f;
        public const float Dt = 1f / TickRate;
        public const int ThinkInterval = 5; // AI re-scores options 4x per second
        public const int MapSize = 240;

        // --- Squads -----------------------------------------------------------------------------
        public const float EngageRadius = 18f;      // enemies this close pull a squad into combat
        public const float CombatEndDelay = 4f;     // seconds with no engaged enemies before a fight ends
        public const float SquadSight = 18f;
        public const float BaseSight = 30f;
        public const float TurretSight = 12f;
        public const float BaseRadius = 16f;        // inside this: deposit, bank Doctrine XP, rest bonus
        public const int StartingSquads = 1;
        public const int MaxWarHallLevel = 3;       // one kin-band; each War Hall level drills it stronger
        public const float HallHealthPerLevel = 0.12f;   // max health per War Hall level above 1
        public const float HallResourcePerLevel = 0.25f; // max mana and stamina per War Hall level above 1

        // --- Direct control (WASD / gamepad steering) ----------------------------------------------
        public const float SteerTimeout = 0.3f;     // steering input older than this is treated as released
        public const float SteerLeash = 5f;         // the banner slows when it gets this far ahead of the squad

        // --- Mana and stamina ----------------------------------------------------------------------
        // Regeneration as a fraction of the maximum per second.
        public const float ManaRegenCombat = 0.025f;
        public const float ManaRegenOutOfCombat = 0.05f;
        public const float ManaRegenResting = 0.12f;
        public const float ManaRegenGarrison = 0.3f;
        public const float StaminaRegenCombat = 0.08f;
        public const float StaminaRegenOutOfCombat = 0.2f;
        public const float StaminaRegenResting = 0.3f;
        public const float StaminaRegenDelay = 1f;      // seconds after exertion before stamina comes back in a fight
        public const float ExhaustedFraction = 0.12f;   // below this much stamina a Vhorn is exhausted
        public const float ExhaustedSpeedMul = 0.8f;
        public const float ExhaustedAttackMul = 1.3f;   // attack cooldown
        public const float DodgeStamina = 12f;
        public const float ReservePenalty = 60f;        // utility lost per unit of reserve an ability would dig into

        // --- Wounds: damage that only resting at base heals --------------------------------------
        public const float WoundFraction = 0.1f;        // share of damage taken that becomes lasting wounds
        public const float MaxWound = 0.75f;            // wounds never take more than this share of max health
        public const float DownWound = 0.15f;           // extra wounds for being downed
        public const float WoundHealGarrison = 0.08f;   // wound fraction healed per second while garrisoned
        public const float WoundHealAtBase = 0.03f;     // ...while resting or idle inside the base

        // --- Injury, death and rebirth -----------------------------------------------------------
        public const float InjuredMaxHpMul = 0.8f;  // design doc starting value: 20% less
        public const float InjuredSpeedMul = 0.8f;
        public const float RiseHpFraction = 0.3f;   // downed units rise with this much health when a fight ends
        public const float StabilizeHpFraction = 0.4f;
        public const float RestSecondsToClearInjury = 18f;
        public const float RestHealPerSecond = 0.035f; // fraction of max health
        public const float RestBaseMultiplier = 3f;    // resting while garrisoned
        public const float RestMenderBonus = 0.5f;
        public const float OutOfCombatRegen = 0.006f;
        public const bool DeathLosesUnbankedDoctrine = true; // design doc "proposed extra cost"
        public const float RegrowSeconds = 20f;
        public const float StarvedRegrowMul = 2f;      // regrowth the stockpile can't pay for still happens, this much slower
        public const float WipeRecallSeconds = 6f;     // with nobody left standing, the clan carries the kin-cores home

        // --- Combat ------------------------------------------------------------------------------
        public const float ArmorConstant = 100f;       // kinetic damage * 100 / (100 + armor)
        public const float ShredPerStack = 12f;
        public const int MaxShredStacks = 5;
        public const float BurnFraction = 0.12f;       // of the hit, per second, for 3 s
        public const float BurnDuration = 3f;
        public const int CryoStacksToFreeze = 4;
        public const float CryoSlowPerStack = 0.12f;
        public const float FreezeDuration = 1.6f;
        public const float ArcChainFraction = 0.5f;
        public const float ArcChainRange = 4.5f;
        public const int ArcChainTargets = 2;
        public const float HighGroundBonus = 0.15f;
        public const float HighGroundHeight = 1.2f;
        public const float HealThreatFactor = 0.45f;  // healing adds this much threat on every engaged enemy
        public const float TauntDuration = 4f;
        public const float WeaponTierDamage = 0.35f;  // per gear tier above scrap
        public const float ArmorTierHealth = 0.30f;
        public const float ArmorTierArmor = 15f;
        public const float MenderTierHeal = 0.25f;
        public const float LoadSpeedPenalty = 0.35f;  // fully loaded squads move this much slower
        public const float LoadAttackPenalty = 0.3f;  // and attack this much slower ("fights badly")

        // --- Titan Form (Shieldbearer slot 4) ---------------------------------------------------
        // Every modifier scales with the 0..1 grow ramp, so a unit that never grows is unchanged.
        public const float TitanScale = 1.6f;          // body radius multiplier (0.75 m -> 1.2 m)
        public const float TitanHeightMul = 1.7f;      // visual height (2.3 m -> 3.9 m, taller than Rustmaw)
        public const float TitanRampSeconds = 0.5f;
        public const float TitanHpMul = 1.35f;         // max health; the health fraction is kept when growing or shrinking
        public const float TitanArmor = 20f;
        public const float TitanMass = 9f;             // body-block and knockback weight (normally 3.5)
        public const float TitanThreatMul = 1.5f;
        public const float TitanSpeedMul = 0.8f;
        public const float TitanAttackMul = 1.3f;      // basic attacks only
        public const float TitanStunMul = 0.5f;
        public const float TitanTauntBonus = 2f;       // Taunt Roar reach while grown
        public const float TitanChallengeSeconds = 2f; // growing taunts nearby enemies this long
        public const float TitanShieldReach = 0.5f;    // enemy shots passing this close to a Titan hit it instead
        public const float TitanLoomReach = 1.5f;      // ground enemies this close to a Titan move slower
        public const float TitanLoomSlow = 0.3f;
        public const float TitanShelterMul = 0.3f;     // fire-wave damage to kin in a Titan's shadow
        public const float TitanShelterDepth = 3f;
        public const float TitanShelterPad = 0.3f;
        public const int TitanShelterCap = 3;          // kin each Titan can shelter per blast
        public const float TitanClashStagger = 2f;     // a rampart crash staggers Rustmaw 4 s
        public const float TitanClashVents = 2.5f;
        public const float TitanClashImmunity = 20f;   // Rustmaw can be clashed at most once per 20 s
        public const float TitanOutOfCombat = 2f;      // the form ends this soon after a fight
        public const float TitanMarching = 0.5f;       // or when the squad marches off
        public const float TitanStuckSeconds = 2f;     // wedged against a wall this long: shrink so the tank can get through
        public const float TitanRoom = 1.4f;           // clear ground needed around the tank to grow
        public const float TitanFizzleCooldown = 3f;
        public const float TitanCastCooldown = 1f;     // cooldown while casting; the full cooldown starts when it grows

        // --- Rustmaw, tuned for one kin-band ------------------------------------------------------
        public const float RustmawHp = 8500f;
        public const float RustmawSwipe = 60f;
        public const float RustmawChargeHit = 80f;
        public const float RustmawEruption = 55f;
        public const float RustmawBlast = 90f;
        public const int RustmawHoundsPerWave = 2;
        public const float RustmawEnrageAfter = 100f;    // seconds into a phase before its damage ramps

        // --- Engineers ---------------------------------------------------------------------------
        public const int MaxTurretsPerSquad = 2;
        public const int MaxMortarsPerSquad = 1;
        public const int MaxRampartsPerSquad = 4;
        public const int MaxSpikeFieldsPerSquad = 2;

        // --- Doctrine -----------------------------------------------------------------------------
        // Cumulative XP needed for tiers 1..5. Recruits start at tier 0 ("no Doctrine").
        public static readonly float[] DoctrineTierXp = { 0f, 15f, 60f, 150f, 320f, 600f };
        public const int ThirdAbilityTier = 2;        // ability slot 3 opens at Doctrine tier 2 in any command
        public const int FourthAbilityTier = 3;       // slot 4 (only the Shieldbearer has one: Titan Form) at tier 3
        public const float DoctrineXpPerSecond = 1f;
        public const float DoctrineKillXp = 3f;
        public const float DoctrineWinMul = 1.25f;
        public const float DoctrineLossMul = 0.6f;
        public const float DoctrineSuitMul = 1.3f;
        public const float BossXpBurst = 120f;

        // --- Instinct (bounded evolution strategy) ----------------------------------------------
        public const float InstinctSigma = 0.3f;       // perturbation, as a fraction of the half-width allowed by Doctrine
        public const float InstinctLearningRate = 0.8f;
        public const float InstinctBaselineRate = 0.3f;

        // --- Economy ------------------------------------------------------------------------------
        public const float GatherRate = 1f;            // units per second per Vhorn
        public const float WarwrightQuarryBonus = 1.5f; // Warwrights quarry stone and ferrite faster
        public const float SmeltSeconds = 4f;          // 2 ferrite -> 1 alloy
        public const float GatherReturnFraction = 0.9f;

        // --- World director -----------------------------------------------------------------------
        public const float MeteorMinInterval = 75f;
        public const float MeteorMaxInterval = 115f;
        public const float MeteorWarning = 3.5f;
        public const float MeteorRadius = 5f;
        public const float MeteorDamage = 90f;
        public const float CampRespawnSeconds = 240f;
        public const float WarPartyFirstAt = 540f;
        public const float WarPartyInterval = 300f;
    }
}
