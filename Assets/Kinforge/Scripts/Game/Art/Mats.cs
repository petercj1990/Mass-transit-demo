using System.Collections.Generic;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Material factory. Lit materials are cloned from the default primitive material, so they work
    /// in the built-in pipeline and in URP alike; transparent effects use Sprites/Default, which is
    /// always included in builds.
    /// </summary>
    public static class Mats
    {
        private static Material _litTemplate;
        private static Shader _sprite;
        private static readonly Dictionary<long, Material> Cache = new Dictionary<long, Material>();
        // Textured materials are keyed by the texture object itself (no instance ids: newer Unity 6 releases reject GetInstanceID).
        private static readonly Dictionary<(Texture, long), Material> TexturedCache = new Dictionary<(Texture, long), Material>();

        public static void Init()
        {
            if (_litTemplate != null) return;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _litTemplate = new Material(tmp.GetComponent<Renderer>().sharedMaterial);
            Object.Destroy(tmp);
            _sprite = Shader.Find("Sprites/Default");
            if (_sprite == null) _sprite = Shader.Find("Unlit/Transparent");
            if (_sprite == null) _sprite = _litTemplate.shader;
        }

        private static long Key(Color c, int kind, float extra)
        {
            Color32 q = c;
            return ((long)kind << 40) | ((long)q.r << 32) | ((long)q.g << 24) | ((long)q.b << 16) | ((long)q.a << 8) | (long)(Mathf.Clamp01(extra) * 255f);
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        public static void SetEmission(Material m, Color c)
        {
            if (!m.HasProperty("_EmissionColor")) return;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", c);
        }

        public static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        }

        private static void SetSurface(Material m, float smoothness, float metallic)
        {
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        }

        /// <summary>Shared opaque lit material (cached by colour).</summary>
        public static Material Lit(Color c, float smoothness = 0.12f, float metallic = 0f)
        {
            long key = Key(c, metallic > 0.3f ? 2 : 1, smoothness);
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = NewLit(c, smoothness, metallic);
            Cache[key] = m;
            return m;
        }

        /// <summary>A fresh, unshared lit material (for per-object glow or tint changes).</summary>
        public static Material NewLit(Color c, float smoothness = 0.12f, float metallic = 0f)
        {
            var m = new Material(_litTemplate);
            m.enableInstancing = true;
            SetColor(m, c);
            SetSurface(m, smoothness, metallic);
            return m;
        }

        public static Material Glow(Color c, float intensity = 1.5f)
        {
            long key = Key(c, 3, intensity / 4f);
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = NewLit(c * 0.6f, 0.3f);
            SetEmission(m, c * intensity);
            Cache[key] = m;
            return m;
        }

        public static Material NewGlow(Color c, float intensity)
        {
            var m = NewLit(c * 0.6f, 0.3f);
            SetEmission(m, c * intensity);
            return m;
        }

        /// <summary>Unlit, alpha-blended, vertex-coloured. Used for decals, beams and effects.</summary>
        public static Material NewTransparent(Color c, Texture tex = null)
        {
            var m = new Material(_sprite);
            if (tex != null) SetTexture(m, tex);
            SetColor(m, c);
            m.renderQueue = 3000;
            return m;
        }

        public static Material Transparent(Color c, Texture tex = null)
        {
            long key = Key(c, 4, 0f);
            if (tex != null)
            {
                if (TexturedCache.TryGetValue((tex, key), out var tm) && tm != null) return tm;
                tm = NewTransparent(c, tex);
                TexturedCache[(tex, key)] = tm;
                return tm;
            }
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = NewTransparent(c);
            Cache[key] = m;
            return m;
        }
    }
}
