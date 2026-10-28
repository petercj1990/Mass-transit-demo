using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Third-person camera that rides behind the kin-band, like following a WoW party through a dungeon.
    /// It follows the squad's banner; the commander orbits it (mouse drag, Q/E, right stick), tilts it
    /// and zooms between over-the-shoulder and a short tactical view. It never pulls out to show the map.
    /// </summary>
    public sealed class CameraRig
    {
        public const float MinDist = 6f;
        public const float MaxDist = 32f;
        public const float MinPitch = 10f;
        public const float MaxPitch = 62f;

        public readonly Camera Cam;
        public Vector3 Focus;
        public float Yaw;
        public float Pitch = 28f;
        public float Distance = 16f;

        private Vector3 _focusTarget;
        private float _distTarget = 16f;
        private float _yawTarget;
        private float _pitchTarget = 28f;
        private float _shake;

        public CameraRig(Camera cam)
        {
            Cam = cam;
        }

        public float ZoomT => Mathf.InverseLerp(MinDist, MaxDist, Distance);

        /// <summary>Flat forward and right of the view, for camera-relative movement.</summary>
        public Vector3 Forward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
        public Vector3 Right => Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;

        public void Follow(Vector3 target) => _focusTarget = target;

        public void FocusOn(Vector3 p, bool instant = false)
        {
            _focusTarget = p;
            if (instant) Focus = p;
        }

        /// <summary>Puts the camera behind a heading (degrees, Unity yaw).</summary>
        public void LookAlong(float yaw, bool instant = false)
        {
            _yawTarget = yaw;
            if (instant) Yaw = yaw;
        }

        public void Orbit(float yawDelta, float pitchDelta)
        {
            _yawTarget += yawDelta;
            _pitchTarget = Mathf.Clamp(_pitchTarget + pitchDelta, MinPitch, MaxPitch);
        }

        public void Zoom(float amount)
        {
            if (Mathf.Abs(amount) > 0.001f) _distTarget = Mathf.Clamp(_distTarget * (1f - amount * 0.12f), MinDist, MaxDist);
        }

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        public void Update(float dt, World w)
        {
            float k = 1f - Mathf.Exp(-9f * dt);
            Focus = Vector3.Lerp(Focus, _focusTarget, k);
            Distance = Mathf.Lerp(Distance, _distTarget, 1f - Mathf.Exp(-8f * dt));
            Yaw = Mathf.LerpAngle(Yaw, _yawTarget, 1f - Mathf.Exp(-14f * dt));
            Pitch = Mathf.Lerp(Pitch, _pitchTarget, 1f - Mathf.Exp(-12f * dt));

            float ground = w.HeightAt(new Vec2(Focus.x, Focus.z));
            var look = new Vector3(Focus.x, ground + 1.6f, Focus.z);
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            var pos = look + rot * new Vector3(0f, 0f, -Distance);
            // Keep the boom above the terrain along its length (crater rims, hills behind the squad).
            for (int i = 1; i <= 4; i++)
            {
                var p = Vector3.Lerp(look, pos, i / 4f);
                float minY = w.HeightAt(new Vec2(p.x, p.z)) + 1.2f;
                if (p.y < minY) pos.y += minY - p.y;
            }
            if (_shake > 0f)
            {
                pos += Random.insideUnitSphere * _shake * 0.35f;
                _shake = Mathf.Max(0f, _shake - dt * 2.5f);
            }
            Cam.transform.position = pos;
            Cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        public Ray ScreenRay(Vector2 screen) => Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));

        public bool GroundPoint(World w, Vector2 screen, out Vec2 hit)
        {
            var ray = ScreenRay(screen);
            return w.Height.Raycast(ray.origin.x, ray.origin.y, ray.origin.z, ray.direction.x, ray.direction.y, ray.direction.z, 400f, out hit);
        }

        /// <summary>World to IMGUI-space (top-left origin, unscaled pixels). Returns false behind the camera.</summary>
        public bool ToScreen(Vector3 world, out Vector2 gui)
        {
            var s = Cam.WorldToScreenPoint(world);
            gui = new Vector2(s.x, Screen.height - s.y);
            return s.z > 0f;
        }
    }
}
