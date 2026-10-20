using System;

namespace Kinforge.Sim
{
    /// <summary>
    /// Ground-plane vector. X maps to Unity X, Z maps to Unity Z (Y is up and owned by the heightfield).
    /// Angles are yaw in radians: 0 faces +Z, positive turns toward +X (same as Unity's Euler Y).
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Z;

        public Vec2(float x, float z)
        {
            X = x;
            Z = z;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 Forward = new Vec2(0f, 1f);

        public float LengthSq => X * X + Z * Z;
        public float Length => MathF.Sqrt(X * X + Z * Z);

        public Vec2 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-5f ? new Vec2(X / len, Z / len) : Zero;
            }
        }

        /// <summary>Right-hand perpendicular when looking along this vector from above.</summary>
        public Vec2 Right => new Vec2(Z, -X);
        public Vec2 Left => new Vec2(-Z, X);

        public float ToAngle() => MathF.Atan2(X, Z);

        public static Vec2 FromAngle(float yaw) => new Vec2(MathF.Sin(yaw), MathF.Cos(yaw));

        public Vec2 Rotate(float yaw)
        {
            float c = MathF.Cos(yaw);
            float s = MathF.Sin(yaw);
            return new Vec2(X * c + Z * s, Z * c - X * s);
        }

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Z - a.Z * b.X;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float DistanceSq(Vec2 a, Vec2 b) => (a - b).LengthSq;

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        public static Vec2 MoveTowards(Vec2 from, Vec2 to, float maxStep)
        {
            Vec2 d = to - from;
            float len = d.Length;
            if (len <= maxStep || len < 1e-5f) return to;
            return from + d * (maxStep / len);
        }

        public Vec2 ClampLength(float max)
        {
            float lsq = LengthSq;
            if (lsq <= max * max) return this;
            float len = MathF.Sqrt(lsq);
            return this * (max / len);
        }

        /// <summary>Closest point to p on segment ab.</summary>
        public static Vec2 ClosestOnSegment(Vec2 a, Vec2 b, Vec2 p)
        {
            Vec2 ab = b - a;
            float lsq = ab.LengthSq;
            if (lsq < 1e-6f) return a;
            float t = Dot(p - a, ab) / lsq;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return a + ab * t;
        }

        public static float DistanceToSegment(Vec2 a, Vec2 b, Vec2 p) => Distance(ClosestOnSegment(a, b, p), p);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Z);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Z * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Z * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Z / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Z == b.Z;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public bool Equals(Vec2 other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Z);
        public override string ToString() => $"({X:0.0}, {Z:0.0})";
    }
}
