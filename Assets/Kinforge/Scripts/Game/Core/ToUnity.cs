using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>Conversions between the sim's ground plane and Unity world space.</summary>
    public static class ToUnity
    {
        public static Vector3 Ground(World w, Vec2 p, float lift = 0f) => new Vector3(p.X, w.HeightAt(p) + lift, p.Z);

        public static Vector3 Flat(Vec2 p, float y = 0f) => new Vector3(p.X, y, p.Z);

        public static Vec2 Sim(Vector3 v) => new Vec2(v.x, v.z);

        public static Quaternion Yaw(float radians) => Quaternion.Euler(0f, radians * Mathf.Rad2Deg, 0f);

        public static Quaternion Yaw(Vec2 dir) => Quaternion.Euler(0f, dir.ToAngle() * Mathf.Rad2Deg, 0f);

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => Vec2.Lerp(a, b, t);
    }
}
