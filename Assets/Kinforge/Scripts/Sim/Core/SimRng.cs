using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>Deterministic xorshift64* generator so a seed always replays the same world and fights.</summary>
    public sealed class SimRng
    {
        private ulong _state;
        private bool _hasSpare;
        private float _spare;

        public SimRng(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            for (int i = 0; i < 4; i++) NextU64();
        }

        public ulong NextU64()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 2685821657736338717UL;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Float() => (NextU64() >> 40) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Float();

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextU64() % (ulong)(maxExclusive - min));
        }

        public bool Chance(float p) => Float() < p;

        public float Gaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }
            float u, v, s;
            do
            {
                u = Float() * 2f - 1f;
                v = Float() * 2f - 1f;
                s = u * u + v * v;
            } while (s >= 1f || s == 0f);
            float mul = MathF.Sqrt(-2f * MathF.Log(s) / s);
            _spare = v * mul;
            _hasSpare = true;
            return u * mul;
        }

        public Vec2 InsideCircle(float radius)
        {
            float a = Float() * KMath.TwoPi;
            float r = radius * MathF.Sqrt(Float());
            return Vec2.FromAngle(a) * r;
        }

        public Vec2 OnCircle(float radius) => Vec2.FromAngle(Float() * KMath.TwoPi) * radius;

        public T Pick<T>(IList<T> list) => list[Range(0, list.Count)];
    }
}
