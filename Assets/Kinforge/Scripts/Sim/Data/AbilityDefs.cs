using System.Collections.Generic;

namespace Kinforge.Sim
{
    public sealed class AbilityDef
    {
        public AbilityId Id;
        public string Name;
        public float Cooldown;
        public float CastTime;
        public float ChannelTime;
        /// <summary>For melee abilities this is edge-to-edge reach, like basic attacks.</summary>
        public float Range;
        public bool MeleeReach;
        public AbilityTarget Target;
        public DamageType Type;
        public bool Multi;
        public float Power;
        public float Radius;
        public float ArcDeg;
        public float Duration;
        public string Blurb;

        public bool IsChannel => ChannelTime > 0f;
    }

    public static class AbilityDefs
    {
        private static readonly Dictionary<AbilityId, AbilityDef> Defs = new Dictionary<AbilityId, AbilityDef>();

        public static AbilityDef Get(AbilityId id) => Defs[id];

        static AbilityDefs()
        {
            // --- Shieldbearer ---
            Add(new AbilityDef { Id = AbilityId.TauntRoar, Name = "Taunt Roar", Cooldown = 14f, CastTime = 0.3f, Target = AbilityTarget.Self, Multi = true, Radius = 7.5f, Duration = Balance.TauntDuration,
                Blurb = "Forces nearby enemies to attack the Shieldbearer." });
            Add(new AbilityDef { Id = AbilityId.ShieldBash, Name = "Shield Bash", Cooldown = 9f, CastTime = 0.25f, Range = 1.8f, MeleeReach = true, Target = AbilityTarget.Enemy, Type = DamageType.Kinetic, Power = 22f, Duration = 1.2f,
                Blurb = "Interrupts a cast; short stun." });
            Add(new AbilityDef { Id = AbilityId.ShockwaveSlam, Name = "Shockwave Slam", Cooldown = 12f, CastTime = 0.45f, Range = 5f, Target = AbilityTarget.Self, Type = DamageType.Kinetic, Multi = true, Power = 26f, ArcDeg = 75f, Radius = 5f,
                Blurb = "Cone knockback; threat on everything hit." });
            Add(new AbilityDef { Id = AbilityId.TitanForm, Name = "Titan Form", Cooldown = 55f, CastTime = 0.6f, Target = AbilityTarget.Self, Multi = true, Radius = 8f, Duration = 15f,
                Blurb = "Grows to titan size for 15 s: a living wall that stops charges and shelters kin from fire." });

            // --- Mender ---
            Add(new AbilityDef { Id = AbilityId.MendBeam, Name = "Mend Beam", Cooldown = 0.6f, CastTime = 0.1f, ChannelTime = 2f, Range = 14f, Target = AbilityTarget.Ally, Power = 30f,
                Blurb = "Channelled heal on one ally; needs line of sight." });
            Add(new AbilityDef { Id = AbilityId.RegenField, Name = "Regen Field", Cooldown = 18f, CastTime = 0.4f, Range = 12f, Target = AbilityTarget.Ground, Multi = true, Power = 9f, Radius = 5f, Duration = 8f,
                Blurb = "Heals allies inside over time." });
            Add(new AbilityDef { Id = AbilityId.Stabilize, Name = "Stabilize", Cooldown = 14f, CastTime = 0.1f, ChannelTime = 1.5f, Range = 10f, Target = AbilityTarget.DownedAlly,
                Blurb = "Gets a downed ally back up mid-fight (as Injured)." });

            // --- Bruiser ---
            Add(new AbilityDef { Id = AbilityId.Cleave, Name = "Cleave", Cooldown = 7f, CastTime = 0.3f, Range = 2.2f, MeleeReach = true, Target = AbilityTarget.Enemy, Type = DamageType.Kinetic, Multi = true, Power = 30f, ArcDeg = 140f,
                Blurb = "Hits everything in the swing arc." });
            Add(new AbilityDef { Id = AbilityId.GutHook, Name = "Gut Hook", Cooldown = 11f, CastTime = 0.3f, Range = 9f, Target = AbilityTarget.Enemy, Type = DamageType.Kinetic, Power = 12f, Duration = 5f,
                Blurb = "Pulls the target in; causes bleeding." });
            Add(new AbilityDef { Id = AbilityId.CorrodeEdge, Name = "Corrode Edge", Cooldown = 10f, CastTime = 0.3f, Range = 1.9f, MeleeReach = true, Target = AbilityTarget.Enemy, Type = DamageType.Corrosive, Power = 18f,
                Blurb = "Corrosive strike that strips armour." });

            // --- Siegewright ---
            Add(new AbilityDef { Id = AbilityId.BurstVolley, Name = "Burst Volley", Cooldown = 11f, CastTime = 0.2f, Range = 18f, Target = AbilityTarget.Enemy, Type = DamageType.Kinetic, Power = 26f,
                Blurb = "Three heavy rifle shots in quick succession." });
            Add(new AbilityDef { Id = AbilityId.BoltTurret, Name = "Bolt Turret", Cooldown = 18f, CastTime = 0.8f, Range = 7f, Target = AbilityTarget.Ground, Type = DamageType.Kinetic, Power = 10f, Radius = 16f, Duration = 30f,
                Blurb = "Auto-firing emplacement that hits one target at a time." });
            Add(new AbilityDef { Id = AbilityId.MortarPit, Name = "Mortar Pit", Cooldown = 24f, CastTime = 1f, Range = 7f, Target = AbilityTarget.Ground, Type = DamageType.Thermal, Multi = true, Power = 24f, Radius = 26f, Duration = 25f,
                Blurb = "Arcing artillery that lobs burning shells over walls." });

            // --- Warwright ---
            Add(new AbilityDef { Id = AbilityId.RaiseRampart, Name = "Raise Rampart", Cooldown = 14f, CastTime = 0.6f, Range = 12f, Target = AbilityTarget.Ground, Power = 400f, Radius = 2.5f, Duration = 25f,
                Blurb = "Pushes up a wall that blocks movement and shots for both sides." });
            Add(new AbilityDef { Id = AbilityId.SpikeField, Name = "Spike Field", Cooldown = 16f, CastTime = 0.5f, Range = 12f, Target = AbilityTarget.Ground, Type = DamageType.Kinetic, Multi = true, Power = 7f, Radius = 4f, Duration = 12f,
                Blurb = "Barbs that slow and wound; funnels chargers into the tank." });
            Add(new AbilityDef { Id = AbilityId.SnareCage, Name = "Snare Cage", Cooldown = 14f, CastTime = 0.3f, Range = 12f, Target = AbilityTarget.Enemy, Duration = 3.5f,
                Blurb = "Locks one enemy in place." });

            // --- Enemies ---
            Add(new AbilityDef { Id = AbilityId.Pounce, Name = "Pounce", Cooldown = 10f, CastTime = 0.35f, Range = 10f, Target = AbilityTarget.Enemy, Type = DamageType.Kinetic, Power = 30f, Duration = 0.4f,
                Blurb = "Leaps onto a soft target." });
            Add(new AbilityDef { Id = AbilityId.TailSlam, Name = "Tail Slam", Cooldown = 9f, CastTime = 1f, Range = 4.5f, Target = AbilityTarget.Enemy, Type = DamageType.Kinetic, Multi = true, Power = 55f, ArcDeg = 110f, Radius = 4.5f,
                Blurb = "Telegraphed cone slam with knockback." });
            Add(new AbilityDef { Id = AbilityId.WeldBeam, Name = "Weld Beam", Cooldown = 0.5f, CastTime = 0.1f, ChannelTime = 2f, Range = 10f, Target = AbilityTarget.Ally, Power = 16f,
                Blurb = "Welds an ally's plating back on." });
            Add(new AbilityDef { Id = AbilityId.RustmawSwipe, Name = "Plated Swipe", Cooldown = 2.4f, CastTime = 0.7f, Range = 2.6f, MeleeReach = true, Target = AbilityTarget.Enemy, Type = DamageType.Kinetic, Multi = true, Power = 95f, ArcDeg = 110f,
                Blurb = "Wide swipe in front." });
        }

        private static void Add(AbilityDef def) => Defs[def.Id] = def;
    }
}
