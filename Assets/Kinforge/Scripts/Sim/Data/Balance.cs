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
        public const int MaxWarHallLevel = 3;       // squad cap equals War Hall level (design: up to 8 later)

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

        // --- Engineers ---------------------------------------------------------------------------
        public const int MaxTurretsPerSquad = 2;
        public const int MaxMortarsPerSquad = 1;
        public const int MaxRampartsPerSquad = 4;
        public const int MaxSpikeFieldsPerSquad = 2;

        // --- Doctrine -----------------------------------------------------------------------------
        // Cumulative XP needed for tiers 1..5. Recruits start at tier 0 ("no Doctrine").
        public static readonly float[] DoctrineTierXp = { 0f, 15f, 60f, 150f, 320f, 600f };
        public const int ThirdAbilityTier = 2;        // ability slot 3 opens at Doctrine tier 2 in any command
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
        public const float SmeltSeconds = 3f;          // 2 ferrite -> 1 alloy
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
