using System;

namespace Kinforge.Sim
{
    public static class KMath
    {
        public const float Pi = MathF.PI;
        public const float TwoPi = MathF.PI * 2f;
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Wraps an angle to (-pi, pi].</summary>
        public static float WrapAngle(float a)
        {
            while (a > Pi) a -= TwoPi;
            while (a <= -Pi) a += TwoPi;
            return a;
        }

        public static float AngleDelta(float from, float to) => WrapAngle(to - from);

        public static float RotateTowards(float current, float target, float maxStep)
        {
            float d = AngleDelta(current, target);
            if (Math.Abs(d) <= maxStep) return target;
            return WrapAngle(current + Math.Sign(d) * maxStep);
        }

        public static float MoveTowards(float current, float target, float maxStep)
        {
            if (Math.Abs(target - current) <= maxStep) return target;
            return current + Math.Sign(target - current) * maxStep;
        }
    }
}
