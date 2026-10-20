using System.Collections.Generic;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>Small generated textures for decals, rings, UI and effects.</summary>
    public static class Tex
    {
        private static Texture2D _white, _disc, _ring, _rect, _glow, _dot;
        private static readonly Dictionary<int, Texture2D> Cones = new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Texture2D> Solids = new Dictionary<int, Texture2D>();

        private static Texture2D Make(int size, System.Func<float, float, float> alpha, bool mip = true)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, mip);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                byte a = (byte)(Mathf.Clamp01(alpha(u, v)) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.Apply(mip, false);
            return t;
        }

        public static Texture2D White => _white != null ? _white : (_white = Make(4, (u, v) => 1f, false));

        /// <summary>Filled disc with a bright rim.</summary>
        public static Texture2D Disc => _disc != null ? _disc : (_disc = Make(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r > 1f) return 0f;
            float rim = Mathf.SmoothStep(0.86f, 0.97f, r) * (1f - Mathf.SmoothStep(0.97f, 1f, r));
            return 0.35f + rim * 0.65f;
        }));

        public static Texture2D Ring => _ring != null ? _ring : (_ring = Make(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return Mathf.SmoothStep(0.72f, 0.84f, r) * (1f - Mathf.SmoothStep(0.92f, 1f, r));
        }));

        /// <summary>Filled rectangle with a bright frame.</summary>
        public static Texture2D Rect => _rect != null ? _rect : (_rect = Make(64, (u, v) =>
        {
            float e = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
            float frame = Mathf.SmoothStep(0.86f, 0.95f, e);
            return 0.35f + frame * 0.65f;
        }));

        public static Texture2D Glow => _glow != null ? _glow : (_glow = Make(64, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
        }));

        public static Texture2D Dot => _dot != null ? _dot : (_dot = Make(32, (u, v) => 1f - Mathf.SmoothStep(0.75f, 1f, Mathf.Sqrt(u * u + v * v))));

        /// <summary>A cone (pie slice) of the given arc, apex at the bottom-centre of the texture.</summary>
        public static Texture2D Cone(float arcDeg)
        {
            int key = Mathf.RoundToInt(arcDeg);
            if (Cones.TryGetValue(key, out var t) && t != null) return t;
            float half = arcDeg * 0.5f * Mathf.Deg2Rad;
            t = Make(128, (u, v) =>
            {
                // The decal quad is 2R wide and R tall with the apex at its bottom centre:
                // x in [-1,1] and y in [0,1] are both in units of the radius R.
                float x = u;
                float y = (v + 1f) * 0.5f;
                float r = Mathf.Sqrt(x * x + y * y);
                if (r > 1f) return 0f;
                float ang = Mathf.Abs(Mathf.Atan2(x, y));
                if (ang > half) return 0f;
                float edge = Mathf.Max(Mathf.SmoothStep(0.9f, 1f, r), Mathf.SmoothStep(half - 0.08f, half, ang));
                return 0.35f + edge * 0.65f;
            });
            Cones[key] = t;
            return t;
        }

        /// <summary>1x1-ish solid colour texture for IMGUI backgrounds.</summary>
        public static Texture2D Solid(Color c)
        {
            Color32 q = c;
            int key = (q.r << 24) | (q.g << 16) | (q.b << 8) | q.a;
            if (Solids.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.SetPixels32(new[] { q, q, q, q });
            t.Apply();
            t.hideFlags = HideFlags.DontSave;
            Solids[key] = t;
            return t;
        }
    }
}
