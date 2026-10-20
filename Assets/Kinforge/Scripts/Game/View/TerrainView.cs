using Kinforge.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kinforge.Game
{
    /// <summary>
    /// The Craters: a heightfield mesh with a baked ground texture (grass, stone, crater beds, rims,
    /// wreckage, the base and the scorched arena) plus a fog-of-war sheet that hugs the terrain.
    /// </summary>
    public sealed class TerrainView
    {
        public const int TextureSize = 1024;

        private readonly World _w;
        private readonly Mesh _fogMesh;
        private readonly Color32[] _fogColors;
        private readonly float[] _fogAlpha;
        private readonly float[] _fogTarget;
        private int _fogVersion = -1;
        private float _fogTimer;

        public Texture2D Albedo { get; }
        public Color32[] MinimapPixels { get; private set; }
        public const int MinimapSize = 128;

        public TerrainView(World w, Transform parent)
        {
            _w = w;
            var mesh = MeshKit.Terrain(w.Height);
            Albedo = BakeAlbedo(w);
            var mat = Mats.NewLit(Color.white, 0.05f);
            Mats.SetTexture(mat, Albedo);
            var go = Prim.Mesh(mesh, parent, Vector3.zero, Vector3.one, mat);
            go.name = "Terrain";
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            _fogMesh = MeshKit.Terrain(w.Height, 0.18f);
            _fogMesh.name = "FogOfWar";
            _fogMesh.MarkDynamic();
            int n = w.Height.Verts * w.Height.Verts;
            _fogColors = new Color32[n];
            _fogAlpha = new float[n];
            _fogTarget = new float[n];
            for (int i = 0; i < n; i++)
            {
                _fogAlpha[i] = 0.9f;
                _fogTarget[i] = 0.9f;
                _fogColors[i] = new Color32(8, 9, 12, 230);
            }
            _fogMesh.colors32 = _fogColors;
            var fogGo = Prim.Mesh(_fogMesh, parent, Vector3.zero, Vector3.one, Mats.NewTransparent(Color.white), default, false);
            fogGo.name = "Fog";
            fogGo.GetComponent<MeshRenderer>().sortingOrder = -10;
            BuildMinimapBase();
        }

        private static Texture2D BakeAlbedo(World w)
        {
            // Classify and shade once per 1 m terrain vertex (the expensive part), then fill the
            // texture by bilinear blending plus a cheap per-pixel speckle.
            int n = w.Height.Verts;
            var vcol = new Color[n * n];
            var noise = w.GroundNoise ?? new Noise(1);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vec2(x, z);
                var g = WorldGen.Ground(w, p);
                Color c = Palette.GroundColor(g);
                float n1 = noise.Fbm(p.X * 0.08f, p.Z * 0.08f, 3);
                if (g == GroundType.Grass)
                {
                    c = Color.Lerp(Palette.GrassDark, Palette.Grass, n1 * 1.3f - 0.1f);
                    if (noise.Value(p.X * 0.35f + 300f, p.Z * 0.35f) > 0.78f) c = Color.Lerp(c, Palette.Stone, 0.35f);
                }
                else if (g == GroundType.CraterBed)
                {
                    c = Color.Lerp(c, new Color(0.33f, 0.29f, 0.26f), n1 * 0.6f);
                }
                float slope = w.Height.Slope(p);
                c *= 1f - Mathf.Clamp01(slope * 0.35f) * 0.35f;
                c *= 0.92f + Mathf.Clamp(w.Height.Get(x, z), -4f, 4f) * 0.02f;
                vcol[z * n + x] = c;
            }

            int size = TextureSize;
            float m2px = (float)w.Height.Size / size;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float fz = (y + 0.5f) * m2px;
                int iz = Mathf.Min((int)fz, n - 2);
                float tz = fz - iz;
                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f) * m2px;
                    int ix = Mathf.Min((int)fx, n - 2);
                    float tx = fx - ix;
                    int i0 = iz * n + ix;
                    Color a = Color.LerpUnclamped(vcol[i0], vcol[i0 + 1], tx);
                    Color b = Color.LerpUnclamped(vcol[i0 + n], vcol[i0 + n + 1], tx);
                    Color c = Color.LerpUnclamped(a, b, tz);
                    unchecked
                    {
                        uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                        h = (h ^ (h >> 13)) * 1274126177u;
                        float speckle = ((h ^ (h >> 16)) & 0xFF) / 255f;
                        c *= 0.9f + 0.18f * speckle;
                    }
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.Apply(true, false);
            return tex;
        }

        private void BuildMinimapBase()
        {
            int s = MinimapSize;
            MinimapPixels = new Color32[s * s];
            float step = (float)_w.Height.Size / s;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                var p = new Vec2((x + 0.5f) * step, (y + 0.5f) * step);
                Color c = Palette.GroundColor(WorldGen.Ground(_w, p));
                if (_w.Nav.BlocksMoveAt(p)) c *= 0.55f;
                c.a = 1f;
                MinimapPixels[y * s + x] = c;
            }
        }

        /// <summary>Fades the fog sheet toward the current visibility (explored terrain stays mapped).</summary>
        public void UpdateFog(float dt)
        {
            var fog = _w.Fog;
            int n = _w.Height.Verts;
            if (fog.Version != _fogVersion || _w.RevealMap)
            {
                _fogVersion = fog.Version;
                int cells = fog.Cells;
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    // Average the four fog cells touching this vertex for soft edges.
                    float sum = 0f;
                    for (int dz = -1; dz <= 0; dz++)
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        int cx = Mathf.Clamp((int)(x / Visibility.CellSize) + dx, 0, cells - 1);
                        int cz = Mathf.Clamp((int)(z / Visibility.CellSize) + dz, 0, cells - 1);
                        byte s = _w.RevealMap ? (byte)2 : fog.State[cz * cells + cx];
                        sum += s == 2 ? 0f : (s == 1 ? 0.48f : 0.9f);
                    }
                    _fogTarget[z * n + x] = sum * 0.25f;
                }
            }

            _fogTimer += dt;
            if (_fogTimer < 0.08f) return;
            float step = Mathf.Clamp01(_fogTimer * 3f);
            _fogTimer = 0f;
            bool changed = false;
            for (int i = 0; i < _fogAlpha.Length; i++)
            {
                float a = _fogAlpha[i];
                float t = _fogTarget[i];
                if (Mathf.Abs(a - t) < 0.004f) continue;
                a = Mathf.Lerp(a, t, step);
                _fogAlpha[i] = a;
                _fogColors[i].a = (byte)(a * 255f);
                changed = true;
            }
            if (changed) _fogMesh.colors32 = _fogColors;
        }
    }
}
