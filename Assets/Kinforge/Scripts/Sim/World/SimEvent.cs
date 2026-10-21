namespace Kinforge.Sim
{
    public enum SimEventType : byte
    {
        Swing,          // A swung at B
        Hit,            // A hit B for Value of Damage type
        Miss,
        Heal,           // A healed B for Value
        AbilityCast,    // A cast Ability (at B / Pos)
        AbilityImpact,  // Ability landed at Pos
        Downed,
        Revived,
        Killed,         // player unit killed (kin-core dropped or carried)
        EnemyDied,
        CorePickup,     // A picked up B's core
        CacheDropped,
        Interrupt,
        Explosion,      // Pos, Value = radius
        Meteor,         // Pos, Value = radius
        ArcChain,       // A -> B
        WeakPoint,      // B hit in the weak point
        BossPhase,      // Value = phase
        BossCharge,
        BossCrash,
        BossBurrow,
        BossSurface,
        BossBlast,
        StructureBuilt,
        StructureDestroyed,
        Regrown,
        Deposit,
        TierUp,         // A reached tier Value in Ability-slot-free command (B = command)
        Victory,
        Knockback,
        TitanRise,      // A grew into Titan Form
        TitanFade,      // A shrank back; Value: 0 timer, 1 too cramped, 2 no room to grow, 3 felled, 4 left the fight
        TitanClash,     // A (Titan) stopped B (Rustmaw) dead; Value 1 when a rampart behind A made it an anvil
        TitanShelter,   // A (Titan) shielded B from a fire wave
        AbilityUnlocked, // A opened a new ability slot (Ability)
    }

    public struct SimEvent
    {
        public SimEventType Type;
        public int A;
        public int B;
        public Vec2 Pos;
        public float Value;
        public DamageType Damage;
        public AbilityId Ability;
        public float Time;
    }
}
