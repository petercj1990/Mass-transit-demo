using System.Collections.Generic;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    public sealed class UnitModel
    {
        public GameObject Root;
        public Transform Rig;
        public Material KinMat;
        public Material VentMat;
        public Material BodyMat;
        /// <summary>Shieldbearer only: the war-form's extra parts, shown while it is a Titan.</summary>
        public GameObject TitanParts;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public float Height;
    }

    /// <summary>
    /// Low-poly models assembled from primitives. Silhouettes follow the design doc's readability
    /// rules: tanks carry tower shields, healers glowing core-packs, Bruisers blades, Siegewrights long
    /// rifles and turret packs, Warwrights heavy terraforming rigs. Kin-marks glow brighter with
    /// veterancy.
    /// </summary>
    public static class ModelFactory
    {
        private static Color Hide(int seed) => Palette.Hides[Mathf.Abs(seed) % Palette.Hides.Length];

        // =====================================================================================
        // Units
        // =====================================================================================

        public static UnitModel BuildUnit(Unit u, Transform parent)
        {
            var m = new UnitModel();
            m.Root = new GameObject(u.Name + " #" + u.Id);
            m.Root.transform.SetParent(parent, false);
            m.Rig = Prim.Node("Rig", m.Root.transform);
            m.Height = u.Def.Height;
            switch (u.Kind)
            {
                case UnitKind.Cratermaw: Cratermaw(m, u, 1f, new Color(0.58f, 0.43f, 0.31f), new Color(1f, 0.75f, 0.2f)); break;
                case UnitKind.ScrapHound: Cratermaw(m, u, 0.72f, new Color(0.44f, 0.45f, 0.48f), new Color(1f, 0.2f, 0.15f)); break;
                case UnitKind.Rockback: Rockback(m, u); break;
                case UnitKind.SalvageDrone: Drone(m, u, false); break;
                case UnitKind.ScrapMender: Drone(m, u, true); break;
                case UnitKind.Rustmaw: Rustmaw(m, u); break;
                default: Vhorn(m, u); break;
            }
            m.Root.GetComponentsInChildren(true, m.Renderers);
            return m;
        }

        private static void Vhorn(UnitModel m, Unit u)
        {
            var r = m.Rig;
            Color hide = Hide(u.Id * 7 + 3);
            var skin = Mats.Lit(hide);
            var dark = Mats.Lit(hide * 0.7f);
            var plate = Mats.Lit(Color.Lerp(hide, Palette.Bone, 0.45f));
            var bone = Mats.Lit(Palette.Bone);
            var alloy = Mats.Lit(Palette.Alloy, 0.4f, 0.5f);
            var darkAlloy = Mats.Lit(Palette.DarkAlloy, 0.3f, 0.4f);
            m.KinMat = Mats.NewGlow(Palette.KinMark, 1.2f);
            float s = u.Kind == UnitKind.Shieldbearer ? 1.08f : 1f;
            r.localScale = Vector3.one * s;

            // Legs (digitigrade-ish) and padded feet
            Prim.Pill(r, new Vector3(-0.21f, 0.42f, -0.05f), new Vector3(0.27f, 0.42f, 0.27f), dark);
            Prim.Pill(r, new Vector3(0.21f, 0.42f, -0.05f), new Vector3(0.27f, 0.42f, 0.27f), dark);
            Prim.Box(r, new Vector3(-0.21f, 0.06f, 0.08f), new Vector3(0.24f, 0.12f, 0.36f), dark);
            Prim.Box(r, new Vector3(0.21f, 0.06f, 0.08f), new Vector3(0.24f, 0.12f, 0.36f), dark);
            Prim.Box(r, new Vector3(0f, 0.86f, -0.05f), new Vector3(0.62f, 0.3f, 0.42f), dark);
            // Massive hunched torso
            Prim.Pill(r, new Vector3(0f, 1.3f, 0.06f), new Vector3(0.88f, 0.62f, 0.64f), skin, new Vector3(20f, 0f, 0f));
            // Keratin shoulder plates
            Prim.Box(r, new Vector3(-0.47f, 1.6f, 0.05f), new Vector3(0.36f, 0.17f, 0.46f), plate, new Vector3(0f, 0f, 14f));
            Prim.Box(r, new Vector3(0.47f, 1.6f, 0.05f), new Vector3(0.36f, 0.17f, 0.46f), plate, new Vector3(0f, 0f, -14f));
            // Arms
            Prim.Pill(r, new Vector3(-0.58f, 1.1f, 0.2f), new Vector3(0.21f, 0.36f, 0.21f), skin, new Vector3(22f, 0f, 8f));
            Prim.Pill(r, new Vector3(0.58f, 1.1f, 0.2f), new Vector3(0.21f, 0.36f, 0.21f), skin, new Vector3(22f, 0f, -8f));
            // Head: broad beast muzzle, sweeping ridge-horns, lower tusks
            Prim.Ball(r, new Vector3(0f, 1.72f, 0.44f), new Vector3(0.45f, 0.42f, 0.5f), skin);
            Prim.Box(r, new Vector3(0f, 1.63f, 0.68f), new Vector3(0.28f, 0.21f, 0.26f), dark);
            Prim.Box(r, new Vector3(-0.19f, 1.9f, 0.32f), new Vector3(0.09f, 0.09f, 0.56f), bone, new Vector3(-28f, -38f, 0f));
            Prim.Box(r, new Vector3(0.19f, 1.9f, 0.32f), new Vector3(0.09f, 0.09f, 0.56f), bone, new Vector3(-28f, 38f, 0f));
            Prim.Box(r, new Vector3(-0.1f, 1.6f, 0.8f), new Vector3(0.05f, 0.15f, 0.05f), bone, new Vector3(-22f, 0f, 0f));
            Prim.Box(r, new Vector3(0.1f, 1.6f, 0.8f), new Vector3(0.05f, 0.15f, 0.05f), bone, new Vector3(-22f, 0f, 0f));
            // Glowing kin-marks
            Prim.Box(r, new Vector3(0f, 1.44f, 0.4f), new Vector3(0.44f, 0.05f, 0.04f), m.KinMat, new Vector3(20f, 0f, 0f), false);
            Prim.Box(r, new Vector3(0f, 1.25f, 0.44f), new Vector3(0.32f, 0.05f, 0.04f), m.KinMat, new Vector3(20f, 0f, 0f), false);
            Prim.Box(r, new Vector3(0f, 1.86f, 0.6f), new Vector3(0.2f, 0.04f, 0.04f), m.KinMat, default, false);
            Prim.Box(r, new Vector3(-0.47f, 1.69f, 0.05f), new Vector3(0.22f, 0.03f, 0.3f), m.KinMat, new Vector3(0f, 0f, 14f), false);
            Prim.Box(r, new Vector3(0.47f, 1.69f, 0.05f), new Vector3(0.22f, 0.03f, 0.3f), m.KinMat, new Vector3(0f, 0f, -14f), false);
            // Kin-band pennant in the squad colour
            var banner = Mats.Lit(Palette.Squad(u.Squad != null ? u.Squad.ColorIndex : 0));
            Prim.Tube(r, new Vector3(0.18f, 1.95f, -0.36f), new Vector3(0.03f, 0.42f, 0.03f), darkAlloy);
            Prim.Box(r, new Vector3(0.18f, 2.18f, -0.5f), new Vector3(0.03f, 0.26f, 0.26f), banner);

            switch (u.Kind)
            {
                case UnitKind.Shieldbearer:
                    Prim.Box(r, new Vector3(-0.66f, 1.05f, 0.45f), new Vector3(0.12f, 1.3f, 0.8f), darkAlloy, new Vector3(0f, -12f, 0f));
                    Prim.Box(r, new Vector3(-0.73f, 1.05f, 0.47f), new Vector3(0.04f, 1.1f, 0.12f), m.KinMat, new Vector3(0f, -12f, 0f), false);
                    Prim.Tube(r, new Vector3(0.62f, 1.0f, 0.5f), new Vector3(0.07f, 0.34f, 0.07f), darkAlloy, new Vector3(30f, 0f, 0f));
                    Prim.Ball(r, new Vector3(0.62f, 1.28f, 0.68f), new Vector3(0.22f, 0.22f, 0.22f), alloy);
                    TitanParts(m, bone);
                    break;
                case UnitKind.Mender:
                    Prim.Box(r, new Vector3(0f, 1.45f, -0.32f), new Vector3(0.44f, 0.5f, 0.2f), darkAlloy);
                    Prim.Ball(r, new Vector3(0f, 1.52f, -0.46f), new Vector3(0.34f, 0.34f, 0.34f), Mats.Glow(Palette.Heal, 2f), default, false);
                    Prim.Tube(r, new Vector3(0.58f, 1.15f, 0.36f), new Vector3(0.04f, 0.9f, 0.04f), darkAlloy);
                    Prim.Ball(r, new Vector3(0.58f, 2.08f, 0.36f), new Vector3(0.13f, 0.13f, 0.13f), Mats.Glow(Palette.Heal, 2f), default, false);
                    break;
                case UnitKind.Bruiser:
                    var steel = Mats.Lit(new Color(0.75f, 0.76f, 0.78f), 0.6f, 0.7f);
                    Prim.Box(r, new Vector3(-0.64f, 0.95f, 0.68f), new Vector3(0.05f, 0.09f, 0.98f), steel, new Vector3(10f, -8f, 0f));
                    Prim.Box(r, new Vector3(0.64f, 0.95f, 0.68f), new Vector3(0.05f, 0.09f, 0.98f), steel, new Vector3(10f, 8f, 0f));
                    break;
                case UnitKind.Siegewright:
                    Prim.Box(r, new Vector3(0.42f, 1.24f, 0.62f), new Vector3(0.08f, 0.1f, 1.3f), darkAlloy);
                    Prim.Box(r, new Vector3(0.42f, 1.33f, 0.45f), new Vector3(0.06f, 0.07f, 0.22f), alloy);
                    Prim.Box(r, new Vector3(0f, 1.48f, -0.42f), new Vector3(0.5f, 0.44f, 0.34f), alloy);
                    Prim.Tube(r, new Vector3(0f, 1.62f, -0.62f), new Vector3(0.08f, 0.18f, 0.08f), darkAlloy, new Vector3(90f, 0f, 0f));
                    break;
                case UnitKind.Warwright:
                    Prim.Box(r, new Vector3(0f, 1.5f, -0.48f), new Vector3(0.66f, 0.76f, 0.44f), darkAlloy);
                    Prim.Ball(r, new Vector3(0f, 1.56f, -0.72f), new Vector3(0.22f, 0.22f, 0.22f), Mats.Glow(Palette.Fire, 2f), default, false);
                    Prim.Box(r, new Vector3(-0.38f, 1.9f, -0.48f), new Vector3(0.08f, 0.5f, 0.08f), alloy, new Vector3(0f, 0f, 20f));
                    Prim.Box(r, new Vector3(0.38f, 1.9f, -0.48f), new Vector3(0.08f, 0.5f, 0.08f), alloy, new Vector3(0f, 0f, -20f));
                    Prim.Tube(r, new Vector3(0.62f, 1.0f, 0.46f), new Vector3(0.06f, 0.45f, 0.06f), darkAlloy, new Vector3(25f, 0f, 0f));
                    Prim.Box(r, new Vector3(0.62f, 1.38f, 0.64f), new Vector3(0.32f, 0.2f, 0.2f), alloy, new Vector3(25f, 0f, 0f));
                    break;
            }
        }

        /// <summary>Titan Form's war-form: a crown of forge-fire over the horns, a blazing shield rim and bone shoulder spikes.</summary>
        private static void TitanParts(UnitModel m, Material bone)
        {
            var t = Prim.Node("TitanParts", m.Rig);
            for (int i = 0; i < 4; i++)
            {
                float tilt = (i - 1.5f) * 16f;
                Prim.Box(t, new Vector3(-0.21f + i * 0.14f, 2.06f, 0.3f), new Vector3(0.06f, 0.3f, 0.06f), m.KinMat, new Vector3(-12f, 0f, -tilt), false);
            }
            Prim.Box(t, new Vector3(-0.73f, 1.72f, 0.47f), new Vector3(0.05f, 0.05f, 0.82f), m.KinMat, new Vector3(0f, -12f, 0f), false);
            Prim.Box(t, new Vector3(-0.73f, 0.4f, 0.47f), new Vector3(0.05f, 0.05f, 0.82f), m.KinMat, new Vector3(0f, -12f, 0f), false);
            Prim.Box(t, new Vector3(-0.52f, 1.8f, 0.02f), new Vector3(0.07f, 0.32f, 0.07f), bone, new Vector3(0f, 0f, 24f));
            Prim.Box(t, new Vector3(0.52f, 1.8f, 0.02f), new Vector3(0.07f, 0.32f, 0.07f), bone, new Vector3(0f, 0f, -24f));
            t.gameObject.SetActive(false);
            m.TitanParts = t.gameObject;
        }

        private static void Cratermaw(UnitModel m, Unit u, float scale, Color body, Color eye)
        {
            var r = m.Rig;
            r.localScale = Vector3.one * scale;
            var hide = Mats.Lit(Color.Lerp(body, Hide(u.Id), 0.15f));
            var dark = Mats.Lit(body * 0.65f);
            var bone = Mats.Lit(Palette.Bone);
            var eyes = Mats.Glow(eye, 2.5f);
            Prim.Pill(r, new Vector3(0f, 0.78f, 0f), new Vector3(0.62f, 0.66f, 0.6f), hide, new Vector3(90f, 0f, 0f));
            Prim.Ball(r, new Vector3(0f, 0.86f, 0.8f), new Vector3(0.52f, 0.44f, 0.64f), hide);
            Prim.Box(r, new Vector3(0f, 0.64f, 0.95f), new Vector3(0.38f, 0.13f, 0.44f), dark);
            Prim.Box(r, new Vector3(-0.12f, 0.72f, 1.12f), new Vector3(0.04f, 0.12f, 0.04f), bone);
            Prim.Box(r, new Vector3(0.12f, 0.72f, 1.12f), new Vector3(0.04f, 0.12f, 0.04f), bone);
            Prim.Ball(r, new Vector3(-0.14f, 0.97f, 1.04f), Vector3.one * 0.08f, eyes, default, false);
            Prim.Ball(r, new Vector3(0.14f, 0.97f, 1.04f), Vector3.one * 0.08f, eyes, default, false);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.27f : 0.27f;
                float z = i < 2 ? 0.45f : -0.42f;
                Prim.Pill(r, new Vector3(x, 0.32f, z), new Vector3(0.17f, 0.32f, 0.17f), dark);
            }
            for (int i = 0; i < 3; i++) Prim.Box(r, new Vector3(0f, 1.16f, 0.32f - i * 0.32f), new Vector3(0.06f, 0.28f, 0.13f), bone, new Vector3(-25f, 0f, 0f));
            Prim.Pill(r, new Vector3(0f, 0.82f, -0.88f), new Vector3(0.13f, 0.36f, 0.13f), hide, new Vector3(-70f, 0f, 0f));
        }

        private static void Rockback(UnitModel m, Unit u)
        {
            var r = m.Rig;
            var shell = Mats.Lit(new Color(0.46f, 0.45f, 0.43f));
            var stone = Mats.Lit(new Color(0.55f, 0.54f, 0.52f));
            var dark = Mats.Lit(new Color(0.30f, 0.27f, 0.25f));
            Prim.Ball(r, new Vector3(0f, 0.85f, 0f), new Vector3(1.95f, 1.05f, 2.35f), shell);
            var rng = new System.Random(u.Id);
            for (int i = 0; i < 5; i++)
            {
                var pos = new Vector3((float)(rng.NextDouble() - 0.5) * 1.1f, 1.25f + (float)rng.NextDouble() * 0.2f, (float)(rng.NextDouble() - 0.5) * 1.4f);
                Prim.Box(r, pos, new Vector3(0.75f, 0.32f, 0.65f), stone, new Vector3((float)rng.NextDouble() * 25f, (float)rng.NextDouble() * 90f, (float)rng.NextDouble() * 20f));
            }
            Prim.Ball(r, new Vector3(0f, 0.6f, 1.22f), new Vector3(0.62f, 0.46f, 0.6f), dark);
            Prim.Ball(r, new Vector3(-0.16f, 0.72f, 1.48f), Vector3.one * 0.08f, Mats.Glow(new Color(1f, 0.6f, 0.2f), 2f), default, false);
            Prim.Ball(r, new Vector3(0.16f, 0.72f, 1.48f), Vector3.one * 0.08f, Mats.Glow(new Color(1f, 0.6f, 0.2f), 2f), default, false);
            for (int i = 0; i < 6; i++)
            {
                float x = i % 2 == 0 ? -0.75f : 0.75f;
                float z = (i / 2 - 1) * 0.7f;
                Prim.Tube(r, new Vector3(x, 0.25f, z), new Vector3(0.28f, 0.25f, 0.28f), dark);
            }
            Prim.Box(r, new Vector3(0f, 0.65f, -1.35f), new Vector3(0.4f, 0.32f, 1.0f), shell, new Vector3(-10f, 0f, 0f));
        }

        private static void Drone(UnitModel m, Unit u, bool mender)
        {
            var r = m.Rig;
            var hull = Mats.Lit(mender ? new Color(0.70f, 0.62f, 0.32f) : Palette.Alloy, 0.5f, 0.6f);
            var dark = Mats.Lit(Palette.DarkAlloy, 0.4f, 0.5f);
            var eye = Mats.Glow(mender ? Palette.Heal : new Color(1f, 0.15f, 0.1f), 2.5f);
            Prim.Ball(r, Vector3.zero, new Vector3(0.62f, 0.55f, 0.62f), hull);
            Prim.Tube(r, Vector3.zero, new Vector3(1.15f, 0.03f, 1.15f), dark);
            Prim.Ball(r, new Vector3(0f, 0f, 0.28f), Vector3.one * 0.2f, eye, default, false);
            Prim.Box(r, new Vector3(0f, 0.38f, -0.05f), new Vector3(0.03f, 0.3f, 0.03f), dark);
            if (mender)
            {
                Prim.Box(r, new Vector3(0f, -0.25f, 0.25f), new Vector3(0.06f, 0.06f, 0.45f), dark, new Vector3(30f, 0f, 0f));
                Prim.Ball(r, new Vector3(0f, -0.4f, 0.48f), Vector3.one * 0.12f, eye, default, false);
                Prim.Box(r, Vector3.zero, new Vector3(1.0f, 0.06f, 0.12f), dark);
            }
        }

        private static void Rustmaw(UnitModel m, Unit u)
        {
            var r = m.Rig;
            m.BodyMat = Mats.NewLit(new Color(0.36f, 0.24f, 0.19f), 0.2f);
            var plates = Mats.Lit(new Color(0.55f, 0.31f, 0.17f), 0.45f, 0.6f);
            var dark = Mats.Lit(new Color(0.21f, 0.15f, 0.13f));
            var bone = Mats.Lit(Palette.Bone);
            m.VentMat = Mats.NewGlow(Palette.Fire, 1.2f);
            Prim.Pill(r, new Vector3(0f, 2.1f, 0f), new Vector3(2.3f, 1.95f, 2.2f), m.BodyMat, new Vector3(90f, 0f, 0f));
            var rng = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                float side = (i % 3 - 1) * 0.85f;
                float z = (i / 3 - 1) * 1.1f;
                Prim.Box(r, new Vector3(side, 3.0f - Mathf.Abs(side) * 0.45f, z), new Vector3(1.0f, 0.28f, 1.15f), plates,
                    new Vector3((float)rng.NextDouble() * 10f, (float)rng.NextDouble() * 12f, -side * 28f));
            }
            Prim.Box(r, new Vector3(0f, 2.25f, 2.35f), new Vector3(1.45f, 1.15f, 1.5f), m.BodyMat);
            Prim.Box(r, new Vector3(0f, 2.75f, 2.2f), new Vector3(1.3f, 0.3f, 1.3f), plates);
            Prim.Box(r, new Vector3(0f, 1.55f, 2.6f), new Vector3(1.25f, 0.36f, 1.25f), dark);
            Prim.Box(r, new Vector3(-0.45f, 1.5f, 3.1f), new Vector3(0.16f, 0.65f, 0.16f), bone, new Vector3(-30f, 0f, 0f));
            Prim.Box(r, new Vector3(0.45f, 1.5f, 3.1f), new Vector3(0.16f, 0.65f, 0.16f), bone, new Vector3(-30f, 0f, 0f));
            Prim.Ball(r, new Vector3(-0.42f, 2.45f, 3.1f), Vector3.one * 0.2f, m.VentMat, default, false);
            Prim.Ball(r, new Vector3(0.42f, 2.45f, 3.1f), Vector3.one * 0.2f, m.VentMat, default, false);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -1.05f : 1.05f;
                float z = i < 2 ? 1.2f : -1.2f;
                Prim.Tube(r, new Vector3(x, 0.9f, z), new Vector3(0.65f, 0.9f, 0.65f), dark);
            }
            // The rear vents: Rustmaw's weak point.
            Prim.Ball(r, new Vector3(0f, 2.65f, -1.75f), Vector3.one * 0.5f, m.VentMat, default, false);
            Prim.Ball(r, new Vector3(-0.65f, 2.35f, -1.6f), Vector3.one * 0.4f, m.VentMat, default, false);
            Prim.Ball(r, new Vector3(0.65f, 2.35f, -1.6f), Vector3.one * 0.4f, m.VentMat, default, false);
            Prim.Pill(r, new Vector3(0f, 1.8f, -2.5f), new Vector3(0.5f, 0.9f, 0.5f), m.BodyMat, new Vector3(-65f, 0f, 0f));
        }

        // =====================================================================================
        // Props, resources, caches
        // =====================================================================================

        public static GameObject BuildProp(StaticProp p, World w, Transform parent)
        {
            var go = new GameObject(p.Kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.position = ToUnity.Ground(w, p.Pos);
            var rng = new System.Random((int)(p.Pos.X * 131 + p.Pos.Z * 17));
            switch (p.Kind)
            {
                case PropKind.Rock:
                case PropKind.Boulder:
                {
                    var c = Color.Lerp(Palette.Stone, Palette.CraterRim, (float)rng.NextDouble()) * (0.85f + (float)rng.NextDouble() * 0.2f);
                    var mat = Mats.Lit(c, 0.05f);
                    float r = p.Radius;
                    Prim.Ball(go.transform, new Vector3(0f, r * 0.35f, 0f), new Vector3(r * 2.1f, r * 1.4f, r * 1.8f), mat,
                        new Vector3((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f));
                    if (p.Kind == PropKind.Boulder)
                        Prim.Ball(go.transform, new Vector3(r * 0.6f, r * 0.2f, r * 0.5f), new Vector3(r, r * 0.8f, r * 0.9f), mat,
                            new Vector3(0f, (float)rng.NextDouble() * 360f, 0f));
                    break;
                }
                default:
                {
                    var hull = Mats.Lit(rng.NextDouble() > 0.5 ? Palette.Rust : new Color(0.38f, 0.36f, 0.35f), 0.3f, 0.4f);
                    var dark = Mats.Lit(Palette.DarkAlloy, 0.3f, 0.4f);
                    float yaw = p.Dir.ToAngle() * Mathf.Rad2Deg;
                    bool spire = p.Kind == PropKind.WreckSpire;
                    float h = spire ? p.Height * 2.2f : p.Height;
                    var piece = Prim.Box(go.transform, new Vector3(0f, h * 0.4f, 0f), new Vector3(p.HalfWidth * 2f, h, p.HalfLen * 2f), hull,
                        new Vector3(spire ? 18f : (float)rng.NextDouble() * 6f, yaw, (float)(rng.NextDouble() - 0.5) * 14f));
                    Prim.Box(piece.transform, new Vector3(0.52f, 0.1f, 0.1f), new Vector3(0.08f, 0.5f, 0.6f), dark);
                    if (rng.NextDouble() > 0.6)
                        Prim.Ball(piece.transform, new Vector3(0.55f, 0.3f, -0.2f), new Vector3(0.12f, 0.1f, 0.06f), Mats.Glow(Palette.KinMark, 1.2f), default, false);
                    break;
                }
            }
            return go;
        }

        public static GameObject BuildNode(ResourceNode n, World w, Transform parent)
        {
            var go = new GameObject(n.Label);
            go.transform.SetParent(parent, false);
            go.transform.position = ToUnity.Ground(w, n.Pos);
            var rng = new System.Random(n.Id * 31);
            float r = n.Radius;
            switch (n.Kind)
            {
                case ResourceKind.Ferrite:
                {
                    var mat = Mats.Lit(Palette.Ferrite, 0.55f, 0.7f);
                    for (int i = 0; i < 3; i++)
                    {
                        var off = new Vector3((float)(rng.NextDouble() - 0.5) * r, r * 0.35f, (float)(rng.NextDouble() - 0.5) * r);
                        float s = r * (1.1f - i * 0.25f);
                        Prim.Box(go.transform, off, new Vector3(s, s * 0.8f, s * 0.9f), mat,
                            new Vector3((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f));
                    }
                    break;
                }
                case ResourceKind.Stone:
                {
                    var mat = Mats.Lit(new Color(0.6f, 0.59f, 0.57f), 0.05f);
                    for (int i = 0; i < 4; i++)
                    {
                        var off = new Vector3((float)(rng.NextDouble() - 0.5) * r * 1.2f, r * 0.3f, (float)(rng.NextDouble() - 0.5) * r * 1.2f);
                        float s = r * (1.2f - i * 0.2f);
                        Prim.Box(go.transform, off, new Vector3(s, s * 1.1f, s * 0.8f), mat,
                            new Vector3((float)rng.NextDouble() * 30f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f));
                    }
                    break;
                }
                case ResourceKind.Fiber:
                {
                    var mat = Mats.Lit(new Color(0.72f, 0.74f, 0.42f));
                    var mat2 = Mats.Lit(new Color(0.58f, 0.62f, 0.33f));
                    for (int i = 0; i < 9; i++)
                    {
                        var off = new Vector3((float)(rng.NextDouble() - 0.5) * r * 1.6f, 0.45f, (float)(rng.NextDouble() - 0.5) * r * 1.6f);
                        Prim.Box(go.transform, off, new Vector3(0.06f, 0.9f + (float)rng.NextDouble() * 0.4f, 0.06f), i % 2 == 0 ? mat : mat2,
                            new Vector3((float)(rng.NextDouble() - 0.5) * 30f, 0f, (float)(rng.NextDouble() - 0.5) * 30f), false);
                    }
                    break;
                }
                default:
                {
                    var hull = Mats.Lit(new Color(0.55f, 0.56f, 0.58f), 0.5f, 0.6f);
                    for (int i = 0; i < 3; i++)
                    {
                        var off = new Vector3((float)(rng.NextDouble() - 0.5) * r, 0.2f + i * 0.12f, (float)(rng.NextDouble() - 0.5) * r);
                        Prim.Box(go.transform, off, new Vector3(r * 1.2f, 0.12f, r * 0.8f), hull,
                            new Vector3((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f));
                    }
                    Prim.Ball(go.transform, new Vector3(0f, 0.45f, 0f), Vector3.one * 0.3f, Mats.Glow(Palette.KinMark, 1.6f), default, false);
                    break;
                }
            }
            return go;
        }

        public static GameObject BuildCache(CoreCache c, World w, Transform parent)
        {
            var go = new GameObject("Kin-core cache");
            go.transform.SetParent(parent, false);
            go.transform.position = ToUnity.Ground(w, c.Pos);
            var glow = Mats.Glow(Palette.KinMark, 3f);
            for (int i = 0; i < Mathf.Max(1, c.UnitIds.Count); i++)
            {
                float a = i * 2.4f;
                Prim.Ball(go.transform, new Vector3(Mathf.Cos(a) * 0.4f, 0.3f + i * 0.05f, Mathf.Sin(a) * 0.4f), Vector3.one * 0.28f, glow, default, false);
            }
            Prim.Tube(go.transform, new Vector3(0f, 8f, 0f), new Vector3(0.35f, 8f, 0.35f), Mats.Transparent(new Color(1f, 0.65f, 0.2f, 0.25f)), default, false);
            return go;
        }

        // =====================================================================================
        // Structures and base buildings
        // =====================================================================================

        public static GameObject BuildStructure(Structure s, World w, Transform parent, out Transform head)
        {
            head = null;
            var go = new GameObject(s.Kind == StructureKind.Building ? s.Building.ToString() : s.Kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.position = ToUnity.Ground(w, s.Pos);
            go.transform.rotation = ToUnity.Yaw(s.Dir);
            var t = go.transform;
            var dark = Mats.Lit(Palette.DarkAlloy, 0.3f, 0.4f);
            var alloy = Mats.Lit(Palette.Alloy, 0.4f, 0.5f);
            switch (s.Kind)
            {
                case StructureKind.Rampart:
                {
                    // Long axis is s.Dir, so the wall's length runs along local Z.
                    var earth = Mats.Lit(Palette.Earth);
                    var cap = Mats.Lit(new Color(0.55f, 0.52f, 0.48f));
                    float len = s.HalfLen * 2f;
                    Prim.Box(t, new Vector3(0f, 0.55f, 0f), new Vector3(1.15f, 2.3f, len), earth);
                    Prim.Box(t, new Vector3(0f, 1.72f, 0f), new Vector3(0.9f, 0.18f, len * 0.96f), cap);
                    for (int i = -1; i <= 1; i++) Prim.Box(t, new Vector3(0f, 1.0f, i * len * 0.33f), new Vector3(1.2f, 0.12f, 0.35f), alloy);
                    break;
                }
                case StructureKind.BoltTurret:
                {
                    Prim.Tube(t, new Vector3(0f, 0.2f, 0f), new Vector3(1f, 0.2f, 1f), dark);
                    Prim.Tube(t, new Vector3(0f, 0.6f, 0f), new Vector3(0.25f, 0.3f, 0.25f), alloy);
                    head = Prim.Node("Head", t, new Vector3(0f, 1.0f, 0f));
                    Prim.Box(head, Vector3.zero, new Vector3(0.55f, 0.38f, 0.62f), alloy);
                    Prim.Tube(head, new Vector3(0f, 0.02f, 0.6f), new Vector3(0.09f, 0.4f, 0.09f), dark, new Vector3(90f, 0f, 0f));
                    Prim.Ball(head, new Vector3(0f, 0.22f, 0.1f), Vector3.one * 0.12f, Mats.Glow(Palette.KinMark, 2f), default, false);
                    break;
                }
                case StructureKind.MortarPit:
                {
                    Prim.Tube(t, new Vector3(0f, 0.18f, 0f), new Vector3(1.9f, 0.2f, 1.9f), Mats.Lit(new Color(0.6f, 0.53f, 0.4f)));
                    head = Prim.Node("Tube", t, new Vector3(0f, 0.5f, 0f));
                    Prim.Tube(head, new Vector3(0f, 0.3f, 0.2f), new Vector3(0.26f, 0.5f, 0.26f), dark, new Vector3(35f, 0f, 0f));
                    break;
                }
                case StructureKind.Hatch:
                {
                    var rust = Mats.Lit(Palette.Rust, 0.3f, 0.5f);
                    float len = s.HalfLen * 2f;
                    Prim.Box(t, new Vector3(0f, 1.6f, 0f), new Vector3(1.4f, 3.4f, len), rust);
                    Prim.Box(t, new Vector3(0f, 1.8f, 0f), new Vector3(1.5f, 1.2f, 1.2f), Mats.Glow(Palette.KinMark, 1.5f), default, false);
                    break;
                }
                case StructureKind.Building:
                    BuildBuilding(s, t);
                    break;
            }
            return go;
        }

        private static void BuildBuilding(Structure s, Transform t)
        {
            float lx = s.HalfLen * 2f;
            float lz = s.HalfWidth * 2f;
            var wall = Mats.Lit(new Color(0.42f, 0.38f, 0.34f));
            var roof = Mats.Lit(new Color(0.30f, 0.24f, 0.21f));
            var dark = Mats.Lit(Palette.DarkAlloy, 0.3f, 0.4f);
            var alloy = Mats.Lit(Palette.Alloy, 0.4f, 0.5f);
            var amber = Mats.Glow(Palette.KinMark, 2f);
            // Local Z runs along s.Dir (the building's long side), so swap extents.
            switch (s.Building)
            {
                case BuildingKind.KinCradle:
                    Prim.Box(t, new Vector3(0f, 0.5f, 0f), new Vector3(lz, 1f, lx), wall);
                    Prim.Ball(t, new Vector3(0f, 1.2f, 0f), new Vector3(lz * 0.9f, 2.4f, lx * 0.7f), alloy);
                    Prim.Ball(t, new Vector3(0f, 2.3f, 0f), Vector3.one * 0.9f, amber, default, false);
                    for (int i = -1; i <= 1; i += 2) Prim.Pill(t, new Vector3(0f, 1.2f, i * lx * 0.42f), new Vector3(0.7f, 0.8f, 0.7f), Mats.Glow(Palette.KinMark, 0.8f));
                    break;
                case BuildingKind.WarHall:
                    Prim.Box(t, new Vector3(0f, 1.4f, 0f), new Vector3(lz, 2.8f, lx), wall);
                    Prim.Mesh(MeshKit.Prism, t, new Vector3(0f, 3.4f, 0f), new Vector3(lz * 1.15f, 1.3f, lx * 1.05f), roof);
                    Prim.Box(t, new Vector3(lz * 0.51f, 1.0f, 0f), new Vector3(0.1f, 2f, 1.4f), dark);
                    Prim.Tube(t, new Vector3(lz * 0.6f, 3.2f, lx * 0.4f), new Vector3(0.06f, 1.4f, 0.06f), dark);
                    Prim.Box(t, new Vector3(lz * 0.6f, 4.2f, lx * 0.4f - 0.4f), new Vector3(0.05f, 0.8f, 0.8f), Mats.Lit(Palette.SquadColors[0]));
                    break;
                case BuildingKind.Smelter:
                    Prim.Box(t, new Vector3(0f, 1.1f, 0f), new Vector3(lz, 2.2f, lx), Mats.Lit(new Color(0.35f, 0.32f, 0.30f)));
                    Prim.Tube(t, new Vector3(0f, 3.2f, lx * 0.25f), new Vector3(0.8f, 1.6f, 0.8f), dark);
                    Prim.Box(t, new Vector3(lz * 0.51f, 0.8f, 0f), new Vector3(0.1f, 0.9f, 1.2f), Mats.Glow(Palette.Fire, 2.2f), default, false);
                    break;
                case BuildingKind.Stockhouse:
                    Prim.Box(t, new Vector3(0f, 0.15f, 0f), new Vector3(lz, 0.3f, lx), dark);
                    for (int i = 0; i < 6; i++)
                    {
                        float z = (i % 3 - 1) * lx * 0.3f;
                        float x = (i / 3 - 0.5f) * lz * 0.5f;
                        Prim.Box(t, new Vector3(x, 0.75f + (i % 2) * 0.3f, z), Vector3.one * (0.9f + (i % 2) * 0.2f), i % 2 == 0 ? Mats.Lit(new Color(0.55f, 0.45f, 0.32f)) : alloy);
                    }
                    break;
                case BuildingKind.SalvageBench:
                    Prim.Box(t, new Vector3(0f, 0.5f, 0f), new Vector3(lz * 0.8f, 0.15f, lx), alloy);
                    for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j += 2)
                        Prim.Box(t, new Vector3(i * lz * 0.35f, 0.25f, j * lx * 0.42f), new Vector3(0.15f, 0.5f, 0.15f), dark);
                    Prim.Box(t, new Vector3(0f, 0.7f, 0.3f), new Vector3(0.6f, 0.3f, 0.9f), Mats.Lit(Palette.Ferrite, 0.5f, 0.6f));
                    Prim.Box(t, new Vector3(0f, 1.3f, -lx * 0.4f), new Vector3(lz * 0.8f, 1.6f, 0.15f), wall);
                    break;
                case BuildingKind.SiegeYard:
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Prim.Box(t, new Vector3(0f, 1.4f, i * lx * 0.45f), new Vector3(0.2f, 2.8f, 0.2f), dark);
                    }
                    Prim.Box(t, new Vector3(0f, 2.8f, 0f), new Vector3(0.2f, 0.2f, lx), dark);
                    Prim.Tube(t, new Vector3(0f, 0.3f, 0f), new Vector3(1.2f, 0.3f, 1.2f), dark);
                    Prim.Box(t, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.5f, 0.8f), alloy);
                    Prim.Tube(t, new Vector3(0f, 0.9f, 0.7f), new Vector3(0.12f, 0.6f, 0.12f), dark, new Vector3(90f, 0f, 0f));
                    break;
                case BuildingKind.PowerHub:
                    Prim.Tube(t, new Vector3(0f, 1.3f, 0f), new Vector3(lz, 1.3f, lz), dark);
                    for (int i = 0; i < 3; i++)
                        Prim.Tube(t, new Vector3(0f, 0.5f + i * 0.8f, 0f), new Vector3(lz * 1.06f, 0.06f, lz * 1.06f), Mats.Glow(new Color(0.4f, 0.8f, 1f), 1.6f), default, false);
                    break;
            }
        }
    }
}
