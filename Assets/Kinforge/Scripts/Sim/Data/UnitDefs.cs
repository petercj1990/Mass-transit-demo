using System.Collections.Generic;

namespace Kinforge.Sim
{
    public sealed class AttackDef
    {
        public string Name;
        public float Range;
        public float Damage;
        public DamageType Type;
        public float Cooldown;
        public float Windup;
        public bool Projectile;
        public float ProjectileSpeed;
        public float SpreadDeg;
        public bool IsMelee => !Projectile;
    }

    public struct LootEntry
    {
        public ResourceKind Kind;
        public int Amount;

        public LootEntry(ResourceKind kind, int amount)
        {
            Kind = kind;
            Amount = amount;
        }
    }

    public sealed class UnitDef
    {
        public UnitKind Kind;
        public string Name;
        public Role Role;
        public EnemyFamily Family;
        public float MaxHp;
        public float Armor;
        public float Speed;
        public float Radius;
        public float Mass;
        public float Sight = 18f;
        public AttackDef Attack;
        public float ThreatMul = 1f;
        public float Carry;
        public float[] Resist = { 1f, 1f, 1f, 1f, 1f, 1f };
        public AbilityId[] Kit = new AbilityId[0];
        public bool Flying;
        public float Height = 2f;
        public float XpValue;
        public LootEntry[] Loot = new LootEntry[0];
        public string Blurb;

        public bool IsPlayerRole => Role != Role.Enemy;
        public bool IsRanged => Attack != null && Attack.Projectile;
        public float ResistTo(DamageType t) => Resist[(int)t];
    }

    public static class UnitDefs
    {
        private static readonly Dictionary<UnitKind, UnitDef> Defs = new Dictionary<UnitKind, UnitDef>();

        public static UnitDef Get(UnitKind kind) => Defs[kind];

        public static Role RoleOf(UnitKind kind) => Defs[kind].Role;

        public static readonly UnitKind[] SquadRoles =
        {
            UnitKind.Shieldbearer, UnitKind.Mender, UnitKind.Bruiser, UnitKind.Siegewright, UnitKind.Warwright,
        };

        static UnitDefs()
        {
            Add(new UnitDef
            {
                Kind = UnitKind.Shieldbearer, Name = "Shieldbearer", Role = Role.Tank,
                MaxHp = 430f, Armor = 40f, Speed = 4.2f, Radius = 0.75f, Mass = 3.5f, ThreatMul = 3f, Carry = 30f, Height = 2.3f,
                Attack = new AttackDef { Name = "Shield Mace", Range = 1.6f, Damage = 14f, Type = DamageType.Kinetic, Cooldown = 1.4f, Windup = 0.35f },
                Kit = new[] { AbilityId.TauntRoar, AbilityId.ShieldBash, AbilityId.ShockwaveSlam },
                Blurb = "Holds enemy attention, body-blocks and protects the Mender.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.Mender, Name = "Mender", Role = Role.Healer,
                MaxHp = 220f, Armor = 10f, Speed = 4.5f, Radius = 0.55f, Mass = 1f, Carry = 20f, Height = 2.1f,
                Attack = new AttackDef { Name = "Sear Bolt", Range = 14f, Damage = 8f, Type = DamageType.Thermal, Cooldown = 1.6f, Windup = 0.3f, Projectile = true, ProjectileSpeed = 26f, SpreadDeg = 2.5f },
                Kit = new[] { AbilityId.MendBeam, AbilityId.RegenField, AbilityId.Stabilize },
                Blurb = "Keeps the squad alive, stabilises downed kin.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.Bruiser, Name = "Bruiser", Role = Role.Melee,
                MaxHp = 300f, Armor = 20f, Speed = 4.8f, Radius = 0.6f, Mass = 1.6f, Carry = 20f, Height = 2.2f,
                Attack = new AttackDef { Name = "Twin Blades", Range = 1.7f, Damage = 22f, Type = DamageType.Kinetic, Cooldown = 1.1f, Windup = 0.28f },
                Kit = new[] { AbilityId.Cleave, AbilityId.GutHook, AbilityId.CorrodeEdge },
                Blurb = "Close-range damage; finishes what the Warwright traps.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.Siegewright, Name = "Siegewright", Role = Role.Siege,
                MaxHp = 230f, Armor = 12f, Speed = 4.3f, Radius = 0.55f, Mass = 1.1f, Carry = 20f, Height = 2.1f,
                Attack = new AttackDef { Name = "Long Rifle", Range = 18f, Damage = 20f, Type = DamageType.Kinetic, Cooldown = 1.6f, Windup = 0.25f, Projectile = true, ProjectileSpeed = 42f, SpreadDeg = 2.2f },
                Kit = new[] { AbilityId.BurstVolley, AbilityId.BoltTurret, AbilityId.MortarPit },
                Blurb = "Ranged damage; builds turrets and siege works.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.Warwright, Name = "Warwright", Role = Role.Warwright,
                MaxHp = 280f, Armor = 25f, Speed = 4.2f, Radius = 0.65f, Mass = 1.8f, ThreatMul = 1.2f, Carry = 25f, Height = 2.2f,
                Attack = new AttackDef { Name = "Rig Hammer", Range = 1.6f, Damage = 10f, Type = DamageType.Kinetic, Cooldown = 1.5f, Windup = 0.35f },
                Kit = new[] { AbilityId.RaiseRampart, AbilityId.SpikeField, AbilityId.SnareCage },
                Blurb = "Raises cover, lays traps and seals escape routes.",
            });

            Add(new UnitDef
            {
                Kind = UnitKind.Cratermaw, Name = "Cratermaw", Role = Role.Enemy, Family = EnemyFamily.Cratermaw,
                MaxHp = 190f, Armor = 10f, Speed = 5.8f, Radius = 0.7f, Mass = 1.2f, Height = 1.3f, XpValue = 1f,
                Attack = new AttackDef { Name = "Bite", Range = 1.5f, Damage = 21f, Type = DamageType.Kinetic, Cooldown = 1.2f, Windup = 0.3f },
                Resist = new[] { 1f, 1.2f, 1.25f, 1f, 1f, 1f },
                Kit = new[] { AbilityId.Pounce },
                Loot = new[] { new LootEntry(ResourceKind.Hide, 4) },
                Blurb = "Pack hunter that dens in crater walls. Pounces on the soft back line.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.Rockback, Name = "Rockback Crawler", Role = Role.Enemy, Family = EnemyFamily.Rockback,
                MaxHp = 520f, Armor = 80f, Speed = 2.6f, Radius = 1.15f, Mass = 5f, Height = 1.5f, XpValue = 2.5f,
                Attack = new AttackDef { Name = "Crush", Range = 1.8f, Damage = 40f, Type = DamageType.Kinetic, Cooldown = 2.2f, Windup = 0.5f },
                Resist = new[] { 0.6f, 1f, 1f, 1.1f, 1.5f, 1f },
                Kit = new[] { AbilityId.TailSlam },
                Loot = new[] { new LootEntry(ResourceKind.Hide, 5), new LootEntry(ResourceKind.Stone, 6) },
                Blurb = "Slow, stone-plated brute. Shrugs off kinetic hits; corrosion strips its plates.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.SalvageDrone, Name = "Feral Salvage Drone", Role = Role.Enemy, Family = EnemyFamily.Drone,
                MaxHp = 125f, Armor = 5f, Speed = 4.3f, Radius = 0.5f, Mass = 0.6f, Height = 2.6f, Flying = true, XpValue = 1f,
                Attack = new AttackDef { Name = "Rivet Gun", Range = 13f, Damage = 16f, Type = DamageType.Kinetic, Cooldown = 1.4f, Windup = 0.2f, Projectile = true, ProjectileSpeed = 30f, SpreadDeg = 2.5f },
                Resist = new[] { 1f, 0.9f, 0.5f, 1.6f, 1.2f, 1.5f },
                Loot = new[] { new LootEntry(ResourceKind.Scrap, 5), new LootEntry(ResourceKind.Ferrite, 2) },
                Blurb = "Ark salvage gone feral. Keeps its distance and peppers the squad.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.ScrapMender, Name = "Scrap Mender", Role = Role.Enemy, Family = EnemyFamily.Drone,
                MaxHp = 240f, Armor = 10f, Speed = 4.1f, Radius = 0.5f, Mass = 0.6f, Height = 2.4f, Flying = true, XpValue = 1.5f,
                Attack = new AttackDef { Name = "Spark", Range = 8f, Damage = 6f, Type = DamageType.Arc, Cooldown = 1.8f, Windup = 0.2f, Projectile = true, ProjectileSpeed = 24f, SpreadDeg = 5f },
                Resist = new[] { 1f, 0.9f, 0.5f, 1.6f, 1.2f, 1.5f },
                Kit = new[] { AbilityId.WeldBeam },
                Loot = new[] { new LootEntry(ResourceKind.Scrap, 6) },
                Blurb = "Welds damaged machines and beasts back together. Kill it first.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.ScrapHound, Name = "Scrap Hound", Role = Role.Enemy, Family = EnemyFamily.Scrap,
                MaxHp = 95f, Armor = 15f, Speed = 6.3f, Radius = 0.55f, Mass = 0.8f, Height = 1.1f, XpValue = 0.6f,
                Attack = new AttackDef { Name = "Gnash", Range = 1.3f, Damage = 16f, Type = DamageType.Kinetic, Cooldown = 1f, Windup = 0.25f },
                Resist = new[] { 1f, 1f, 0.7f, 1.4f, 1.2f, 1f },
                Loot = new[] { new LootEntry(ResourceKind.Scrap, 2) },
                Blurb = "Rustmaw's brood. Swarms healers and engineers.",
            });
            Add(new UnitDef
            {
                Kind = UnitKind.Rustmaw, Name = "Rustmaw", Role = Role.Enemy, Family = EnemyFamily.Rustmaw,
                MaxHp = 16000f, Armor = 70f, Speed = 4.2f, Radius = 2.4f, Mass = 40f, Height = 3.4f, Sight = 40f, XpValue = 20f,
                Attack = new AttackDef { Name = "Plated Swipe", Range = 2.4f, Damage = 55f, Type = DamageType.Kinetic, Cooldown = 2.6f, Windup = 0.6f },
                Resist = new[] { 0.8f, 0.6f, 1.3f, 1f, 1.4f, 1f },
                Kit = new[] { AbilityId.RustmawSwipe },
                Loot = new[] { new LootEntry(ResourceKind.Ferrite, 60), new LootEntry(ResourceKind.Hide, 30) },
                Blurb = "Boss of Breach Hold: a beast armoured in ferrite plates. Three phases.",
            });
        }

        private static void Add(UnitDef def) => Defs[def.Kind] = def;
    }
}
