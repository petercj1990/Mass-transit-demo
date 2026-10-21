namespace Kinforge.Sim
{
    public enum Faction : byte
    {
        Player,
        Enemy,
        Neutral,
    }

    public enum DamageType : byte
    {
        Kinetic,
        Thermal,
        Cryo,
        Arc,
        Corrosive,
        Psionic,
    }

    public enum EnemyFamily : byte
    {
        None,
        Cratermaw,
        Rockback,
        Drone,
        Scrap,
        Rustmaw,
    }

    public enum UnitKind : byte
    {
        // Vhorn kin-band roles
        Shieldbearer,
        Mender,
        Bruiser,
        Siegewright,
        Warwright,

        // Craters fauna and salvage
        Cratermaw,
        Rockback,
        SalvageDrone,
        ScrapMender,
        ScrapHound,
        Rustmaw,
    }

    public enum Role : byte
    {
        Tank,
        Healer,
        Melee,
        Siege,
        Warwright,
        Enemy,
    }

    public enum LifeState : byte
    {
        Active,
        Downed,
        Dead,
    }

    public enum UnitAction : byte
    {
        None,
        Windup,
        Casting,
        Channel,
        Gathering,
        Resting,
    }

    /// <summary>The twelve combat commands from the design doc; the MVP implements the first nine plus Fortify.</summary>
    public enum CommandType : byte
    {
        Charge,
        DefensiveFormation,
        HoldPosition,
        Kite,
        FocusFire,
        SpreadOut,
        Stack,
        FallBack,
        Rest,
        Fortify,
    }

    public enum AbilityId : byte
    {
        None,

        // Shieldbearer
        TauntRoar,
        ShieldBash,
        ShockwaveSlam,

        // Mender
        MendBeam,
        RegenField,
        Stabilize,

        // Bruiser
        Cleave,
        GutHook,
        CorrodeEdge,

        // Siegewright
        BurstVolley,
        BoltTurret,
        MortarPit,

        // Warwright
        RaiseRampart,
        SpikeField,
        SnareCage,

        // Enemies
        Pounce,
        TailSlam,
        WeldBeam,
        RustmawSwipe,

        // Shieldbearer ability slot 4 (opens at Doctrine tier 3)
        TitanForm,
    }

    public enum AbilityTarget : byte
    {
        Enemy,
        Ally,
        DownedAlly,
        Ground,
        Self,
    }

    public enum ResourceKind : byte
    {
        Scrap,
        Ferrite,
        Stone,
        Fiber,
        Hide,
        Alloy,
    }

    public enum StructureKind : byte
    {
        Rampart,
        BoltTurret,
        MortarPit,
        SpikeField,
        RegenField,
        Building,
        Hatch,
    }

    public enum BuildingKind : byte
    {
        None,
        Stockhouse,
        KinCradle,
        WarHall,
        Smelter,
        SalvageBench,
        SiegeYard,
        PowerHub,
    }

    public enum SquadTask : byte
    {
        Idle,
        Move,
        Attack,
        Gather,
        Return,
        Garrison,
        Recover,
        FallBack,
    }

    public enum GroundType : byte
    {
        Grass,
        Stone,
        CraterBed,
        CraterRim,
        Base,
        Arena,
        Wreckage,
    }

    [System.Flags]
    public enum HitFlags : ushort
    {
        None = 0,
        Melee = 1,
        Ranged = 2,
        Ability = 4,
        DoT = 8,
        NoChain = 16,
        Splash = 32,
        FromStructure = 64,
        Environment = 128,
        Execute = 256,
    }
}
