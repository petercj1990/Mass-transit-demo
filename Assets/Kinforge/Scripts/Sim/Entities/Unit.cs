using System.Collections.Generic;

namespace Kinforge.Sim
{
    public sealed class AbilitySlot
    {
        public AbilityDef Def;
        public float Cooldown;
        public bool Unlocked;

        public bool Ready => Unlocked && Cooldown <= 0f;
    }

    /// <summary>Damage-over-time, crowd control and damage-type side effects on one unit.</summary>
    public struct StatusState
    {
        public float BurnDps;
        public float BurnTime;
        public int BurnSource;
        public float BleedDps;
        public float BleedTime;
        public int BleedSource;
        public int CryoStacks;
        public float CryoTime;
        public float FrozenTime;
        public float SlowPct;
        public float SlowTime;
        public float StunTime;
        public float RootTime;
        public int ShredStacks;
        public float ShredTime;
        public float TauntTime;
        public int TauntBy;
        public float RegenHps;
        public float RegenTime;
        public int RegenSource;

        public bool Disabled => StunTime > 0f || FrozenTime > 0f;
        public bool Immobile => Disabled || RootTime > 0f;
    }

    public enum IntentKind : byte
    {
        Idle,
        Move,
        Attack,
        Ability,
        Dodge,
        Gather,
        Rest,
    }

    /// <summary>The option a unit's utility AI picked on its last think.</summary>
    public struct Intent
    {
        public IntentKind Kind;
        public int TargetId;
        public Vec2 Point;
        public Vec2 Dir;
        public AbilityId Ability;
        public bool HasStand;
        public Vec2 Stand;
        public float StandTolerance;
        public bool FireOnMove;
        public float Score;
    }

    public enum CoreState : byte
    {
        WithBody,
        Carried,
        Cached,
        AtCradle,
    }

    public sealed class Unit : Entity
    {
        public UnitDef Def;
        public string Name;
        public Squad Squad;
        public EnemyGroup Group;
        public LifeState Life;
        public bool Injured;
        public float Facing;
        public Vec2 Velocity;
        public int WeaponTier = 1;
        public int ArmorTier = 1;
        public float ArmorOverride = -1f;
        public bool Untargetable;
        public float HeightOffset;

        // Titan Form (Shieldbearer slot 4). TitanT is the 0..1 grow ramp that every size and stat
        // modifier scales with; only Titan.SetT writes it (and Radius while it is above zero).
        public float TitanTime;
        public float TitanT;
        public bool TitanClashed;
        public float TitanStuck;
        public byte TitanEndReason;
        /// <summary>Sim time until which a grown tank is braced to meet a charge (set by its AI while reading the tell).</summary>
        public float TitanBraceUntil = -999f;

        // Action state
        public UnitAction Action;
        public float ActionTimer;
        public float ActionTotal;
        public int ActionTargetId;
        public Vec2 ActionPoint;
        public AbilityId ActionAbility;
        public int ActionShots;
        public Vec2 ActionDir;
        public float ActionSubTimer;
        public float ActionAccum;
        public int ActionTelegraphId;
        public float AttackCooldown;
        public AbilitySlot[] Abilities;
        public StatusState Status;
        public ThreatTable Threat;
        public int CurrentTargetId;
        public Intent Intent;
        public int ThinkPhase;

        // Movement
        public readonly List<Vec2> Path = new List<Vec2>();
        public int PathIndex;
        public Vec2 PathGoal;
        public int PathNavVersion = -1;
        public float PathTime = -999f;
        public bool PathFailed;
        public Vec2 ForcedVel;
        public float ForcedTime;
        public bool Moving;
        public bool HasMoveGoal;
        public Vec2 MoveGoal;
        public float MoveSpeedMul = 1f;
        public float MoveArriveDist = 0.35f;

        // Player-side
        public Doctrine Doctrine;
        public Instinct Instinct;
        public FightStats Fight;
        public Stockpile Carry;
        public readonly List<int> CarriedCores = new List<int>();
        public CoreState Core;
        public int CoreHolder;
        public float RestProgress;
        public bool Garrisoned;
        public int GatherNodeId;
        public int Kills;
        public int Fights;

        // Timeline markers (sim time) used by AI and the view
        public float LastHurtTime = -999f;
        public float LastAttackTime = -999f;
        public float LastCastTime = -999f;
        public float LastHealedTime = -999f;
        public float DownedAt;
        public float DiedAt;
        public float SpawnedAt;

        // Utility AI debug (only filled for the inspected unit)
        public readonly string[] DebugLabel = new string[5];
        public readonly float[] DebugScore = new float[5];
        public int DebugCount;

        public UnitKind Kind => Def.Kind;
        public Role Role => Def.Role;
        public bool IsPlayer => Faction == Faction.Player;
        public bool IsActive => Life == LifeState.Active && !Removed && !Garrisoned;
        public override bool IsTargetable => IsActive && !Untargetable;

        public override float MaxHp
        {
            get
            {
                float hp = Def.MaxHp * (1f + Balance.ArmorTierHealth * (ArmorTier - 1));
                if (Injured) hp *= Balance.InjuredMaxHpMul;
                if (TitanT > 0f) hp *= 1f + (Balance.TitanHpMul - 1f) * TitanT;
                return hp;
            }
        }

        public override float Armor
        {
            get
            {
                float a = ArmorOverride >= 0f ? ArmorOverride : Def.Armor + Balance.ArmorTierArmor * (ArmorTier - 1);
                if (TitanT > 0f) a += Balance.TitanArmor * TitanT;
                a -= Status.ShredStacks * Balance.ShredPerStack;
                return a < 0f ? 0f : a;
            }
        }

        public bool TitanActive => TitanTime > 0f;
        public float Mass => TitanT > 0f ? Def.Mass + (Balance.TitanMass - Def.Mass) * TitanT : Def.Mass;
        public float ThreatMul => TitanT > 0f ? Def.ThreatMul * (1f + (Balance.TitanThreatMul - 1f) * TitanT) : Def.ThreatMul;
        /// <summary>Visual height multiplier, for the view and HUD anchors.</summary>
        public float HeightMul => 1f + (Balance.TitanHeightMul - 1f) * TitanT;

        public float CarryCapacity => Def.Carry;
        public float CarryLoad => Carry == null ? 0f : Carry.Total;
        public float LoadFraction => CarryCapacity > 0f ? KMath.Clamp01(CarryLoad / CarryCapacity) : 0f;

        public float DamageMultiplier => 1f + Balance.WeaponTierDamage * (WeaponTier - 1);

        /// <summary>Edge-to-edge reach turned into a centre-to-centre distance for this target.</summary>
        public float ReachTo(Entity target, float edgeRange) => edgeRange + Radius + (target != null ? target.Radius : 0f);

        public AbilitySlot Slot(AbilityId id)
        {
            if (Abilities == null) return null;
            foreach (var s in Abilities)
            {
                if (s.Def.Id == id) return s;
            }
            return null;
        }

        public bool AbilityReady(AbilityId id)
        {
            var s = Slot(id);
            return s != null && s.Ready;
        }

        public bool IsBusy => Action == UnitAction.Windup || Action == UnitAction.Casting || Action == UnitAction.Channel;

        public override string ToString() => $"{Name}#{Id}";
    }
}
