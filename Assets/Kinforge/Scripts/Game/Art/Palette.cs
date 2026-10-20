using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Colours from the design doc: grey stone and pale green-gold grass, rust-brown ferrite, slate,
    /// ochre and rust Vhorn hides with glowing amber kin-marks.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Sky = new Color(0.63f, 0.67f, 0.70f);
        public static readonly Color Sun = new Color(1f, 0.95f, 0.86f);
        public static readonly Color Ambient = new Color(0.46f, 0.48f, 0.53f);

        public static readonly Color Grass = new Color(0.56f, 0.57f, 0.36f);
        public static readonly Color GrassDark = new Color(0.43f, 0.46f, 0.30f);
        public static readonly Color Stone = new Color(0.48f, 0.48f, 0.47f);
        public static readonly Color CraterBed = new Color(0.26f, 0.24f, 0.23f);
        public static readonly Color CraterRim = new Color(0.53f, 0.51f, 0.48f);
        public static readonly Color BaseGround = new Color(0.46f, 0.39f, 0.31f);
        public static readonly Color ArenaGround = new Color(0.24f, 0.19f, 0.17f);
        public static readonly Color WreckGround = new Color(0.42f, 0.30f, 0.21f);

        public static readonly Color KinMark = new Color(1f, 0.62f, 0.15f);
        public static readonly Color Bone = new Color(0.86f, 0.82f, 0.70f);
        public static readonly Color Alloy = new Color(0.45f, 0.46f, 0.49f);
        public static readonly Color DarkAlloy = new Color(0.22f, 0.23f, 0.25f);
        public static readonly Color Ferrite = new Color(0.55f, 0.29f, 0.16f);
        public static readonly Color Rust = new Color(0.50f, 0.28f, 0.17f);
        public static readonly Color Earth = new Color(0.47f, 0.40f, 0.33f);

        public static readonly Color[] Hides =
        {
            new Color(0.40f, 0.41f, 0.43f), // slate
            new Color(0.58f, 0.47f, 0.30f), // ochre
            new Color(0.52f, 0.33f, 0.24f), // rust
            new Color(0.46f, 0.43f, 0.40f),
        };

        public static readonly Color[] SquadColors =
        {
            new Color(0.95f, 0.62f, 0.15f), // amber
            new Color(0.20f, 0.75f, 0.78f), // teal
            new Color(0.86f, 0.25f, 0.30f), // crimson
            new Color(0.55f, 0.75f, 0.30f),
            new Color(0.70f, 0.45f, 0.90f),
            new Color(0.90f, 0.90f, 0.90f),
        };

        public static readonly Color Enemy = new Color(0.92f, 0.25f, 0.20f);
        public static readonly Color Heal = new Color(0.35f, 0.95f, 0.55f);
        public static readonly Color Telegraph = new Color(0.95f, 0.18f, 0.12f);
        public static readonly Color Fire = new Color(1f, 0.45f, 0.10f);

        public static Color Squad(int index) => SquadColors[Mathf.Abs(index) % SquadColors.Length];

        public static Color Damage(DamageType t)
        {
            switch (t)
            {
                case DamageType.Thermal: return new Color(1f, 0.55f, 0.15f);
                case DamageType.Cryo: return new Color(0.55f, 0.85f, 1f);
                case DamageType.Arc: return new Color(0.55f, 0.65f, 1f);
                case DamageType.Corrosive: return new Color(0.65f, 0.95f, 0.25f);
                case DamageType.Psionic: return new Color(0.85f, 0.45f, 1f);
                default: return new Color(1f, 0.93f, 0.80f);
            }
        }

        public static Color Resource(ResourceKind k)
        {
            switch (k)
            {
                case ResourceKind.Scrap: return new Color(0.62f, 0.64f, 0.68f);
                case ResourceKind.Ferrite: return Ferrite;
                case ResourceKind.Stone: return new Color(0.58f, 0.57f, 0.55f);
                case ResourceKind.Fiber: return new Color(0.70f, 0.74f, 0.40f);
                case ResourceKind.Hide: return new Color(0.66f, 0.48f, 0.34f);
                default: return new Color(0.72f, 0.48f, 0.38f);
            }
        }

        public static Color GroundColor(GroundType g)
        {
            switch (g)
            {
                case GroundType.Stone: return Stone;
                case GroundType.CraterBed: return CraterBed;
                case GroundType.CraterRim: return CraterRim;
                case GroundType.Base: return BaseGround;
                case GroundType.Arena: return ArenaGround;
                case GroundType.Wreckage: return WreckGround;
                default: return Grass;
            }
        }
    }
}
