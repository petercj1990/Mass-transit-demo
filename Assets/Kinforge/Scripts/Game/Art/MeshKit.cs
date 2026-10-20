using System.Collections.Generic;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>Procedural meshes: terrain, terrain-hugging decals and simple shapes.</summary>
    public static class MeshKit
    {
        /// <summary>Full-resolution terrain mesh (1 m grid) with world-space UVs.</summary>
        public static Mesh Terrain(Heightfield h, float lift = 0f)
        {
            int n = h.Verts;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            var cols = new Color32[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                verts[i] = new Vector3(x, h.Get(x, z) + lift, z);
                uvs[i] = new Vector2((float)x / h.Size, (float)z / h.Size);
                cols[i] = new Color32(255, 255, 255, 255);
            }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int a = z * n + x;
                int b = a + 1;
                int c = a + n;
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var mesh = new Mesh { name = "Terrain" };
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A grid that follows the terrain, for ground decals. The rectangle is centred on <paramref name="center"/>,
        /// rotated so local +Z points along <paramref name="forward"/>, and spans +/-halfX by +/-halfZ.
        /// </summary>
        public static void Draped(Mesh mesh, World w, Vec2 center, Vec2 forward, float halfX, float halfZ, int res, float lift)
        {
            res = Mathf.Clamp(res, 2, 40);
            int n = res + 1;
            Vec2 f = forward.LengthSq > 1e-4f ? forward.Normalized : Vec2.Forward;
            Vec2 r = f.Right;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            var cols = new Color32[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / res;
                float v = (float)j / res;
                Vec2 p = center + r * ((u * 2f - 1f) * halfX) + f * ((v * 2f - 1f) * halfZ);
                int k = j * n + i;
                verts[k] = new Vector3(p.X, w.HeightAt(p) + lift, p.Z);
                uvs[k] = new Vector2(u, v);
                cols[k] = new Color32(255, 255, 255, 255);
            }
            var tris = new int[res * res * 6];
            int t = 0;
            for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                int a = j * n + i;
                int b = a + 1;
                int c = a + n;
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        private static Mesh _quad;

        /// <summary>Flat 1x1 quad on the XZ plane facing up, with vertex colours.</summary>
        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "FlatQuad" };
                _quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
                _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                _quad.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
                _quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                _quad.RecalculateNormals();
                _quad.RecalculateBounds();
                return _quad;
            }
        }

        private static Mesh _prism;

        /// <summary>Unit triangular prism (a roof or a horn tip).</summary>
        public static Mesh Prism
        {
            get
            {
                if (_prism != null) return _prism;
                var v = new List<Vector3>
                {
                    new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0f, 0.5f, -0.5f),
                    new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0f, 0.5f, 0.5f),
                };
                var verts = new List<Vector3>();
                var tris = new List<int>();
                void Face(params int[] idx)
                {
                    int start = verts.Count;
                    foreach (int i in idx) verts.Add(v[i]);
                    for (int k = 1; k + 1 < idx.Length; k++)
                    {
                        tris.Add(start);
                        tris.Add(start + k);
                        tris.Add(start + k + 1);
                    }
                }
                Face(0, 2, 1);
                Face(3, 4, 5);
                Face(0, 1, 4, 3);
                Face(1, 2, 5, 4);
                Face(2, 0, 3, 5);
                _prism = new Mesh { name = "Prism" };
                _prism.SetVertices(verts);
                _prism.SetTriangles(tris, 0);
                _prism.RecalculateNormals();
                _prism.RecalculateBounds();
                return _prism;
            }
        }
    }
}
