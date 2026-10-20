using System;

namespace Kinforge.Sim
{
    /// <summary>Terrain heights on a 1 m vertex grid. Index (x, z) is the vertex at world (x, z).</summary>
    public sealed class Heightfield
    {
        public readonly int Size;   // map extent in metres
        public readonly int Verts;  // vertices per side (Size + 1)
        public readonly float[] Heights;

        public Heightfield(int size)
        {
            Size = size;
            Verts = size + 1;
            Heights = new float[Verts * Verts];
        }

        public float Get(int x, int z)
        {
            x = KMath.Clamp(x, 0, Verts - 1);
            z = KMath.Clamp(z, 0, Verts - 1);
            return Heights[z * Verts + x];
        }

        public void Set(int x, int z, float h) => Heights[z * Verts + x] = h;

        public float Sample(Vec2 p) => Sample(p.X, p.Z);

        public float Sample(float x, float z)
        {
            x = KMath.Clamp(x, 0f, Size - 0.0001f);
            z = KMath.Clamp(z, 0f, Size - 0.0001f);
            int x0 = (int)x;
            int z0 = (int)z;
            float tx = x - x0;
            float tz = z - z0;
            float a = Get(x0, z0);
            float b = Get(x0 + 1, z0);
            float c = Get(x0, z0 + 1);
            float d = Get(x0 + 1, z0 + 1);
            return KMath.Lerp(KMath.Lerp(a, b, tx), KMath.Lerp(c, d, tx), tz);
        }

        /// <summary>Gradient magnitude (rise over run) at p.</summary>
        public float Slope(Vec2 p)
        {
            float dx = Sample(p.X + 0.5f, p.Z) - Sample(p.X - 0.5f, p.Z);
            float dz = Sample(p.X, p.Z + 0.5f) - Sample(p.X, p.Z - 0.5f);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Intersects a ray with the terrain by marching and refining. Used for mouse picking so the
        /// presentation layer needs no colliders.
        /// </summary>
        public bool Raycast(float ox, float oy, float oz, float dx, float dy, float dz, float maxDist, out Vec2 hit)
        {
            hit = Vec2.Zero;
            float step = 0.5f;
            float prevT = 0f;
            float prevAbove = oy - Sample(ox, oz);
            if (prevAbove < 0f) return false;
            for (float t = step; t <= maxDist; t += step)
            {
                float x = ox + dx * t;
                float y = oy + dy * t;
                float z = oz + dz * t;
                float above = y - Sample(x, z);
                if (above <= 0f)
                {
                    float lo = prevT;
                    float hi = t;
                    for (int i = 0; i < 12; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        float my = oy + dy * mid;
                        float ma = my - Sample(ox + dx * mid, oz + dz * mid);
                        if (ma > 0f) lo = mid;
                        else hi = mid;
                    }
                    hit = new Vec2(ox + dx * hi, oz + dz * hi);
                    return true;
                }
                prevT = t;
                prevAbove = above;
            }
            return false;
        }
    }
}
