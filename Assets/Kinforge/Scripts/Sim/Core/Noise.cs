using System;

namespace Kinforge.Sim
{
    /// <summary>Seeded value noise with fractal octaves; used for terrain relief and ground colour variation.</summary>
    public sealed class Noise
    {
        private readonly uint _seed;

        public Noise(uint seed)
        {
            _seed = seed * 747796405u + 2891336453u;
        }

        private float Hash(int x, int z)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)z * 668265263u + _seed;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Smooth value noise in [0, 1].</summary>
        public float Value(float x, float z)
        {
            int x0 = (int)MathF.Floor(x);
            int z0 = (int)MathF.Floor(z);
            float tx = x - x0;
            float tz = z - z0;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Hash(x0, z0);
            float b = Hash(x0 + 1, z0);
            float c = Hash(x0, z0 + 1);
            float d = Hash(x0 + 1, z0 + 1);
            return KMath.Lerp(KMath.Lerp(a, b, tx), KMath.Lerp(c, d, tx), tz);
        }

        /// <summary>Fractal sum of value noise, normalised to roughly [0, 1].</summary>
        public float Fbm(float x, float z, int octaves)
        {
            float sum = 0f;
            float amp = 1f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x * freq + i * 17.3f, z * freq - i * 9.1f) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }
    }
}
