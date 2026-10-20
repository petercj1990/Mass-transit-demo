using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Free RTS camera with no avatar: pan (WASD / arrows / screen edge / middle-drag), rotate (Q/E or
    /// Alt+middle-drag), zoom from a strategic view down to over-the-shoulder (mouse wheel). The pitch
    /// flattens as you zoom in so close-ups look along the ground.
    /// </summary>
    public sealed class CameraRig
    {
        public const float MinDist = 9f;
        public const float MaxDist = 130f;

        public readonly Camera Cam;
        public Vector3 Focus;
        public float Yaw;
        public float Distance = 60f;
        public bool EdgeScroll;

        private Vector3 _focusTarget;
        private float _distTarget = 60f;
        private float _yawTarget;
        private float _shake;
        private Vector3 _dragAnchor;
        private bool _dragging;

        public CameraRig(Camera cam)
        {
            Cam = cam;
        }

        public float ZoomT => Mathf.InverseLerp(MinDist, MaxDist, Distance);
        public float Pitch => Mathf.Lerp(30f, 64f, Mathf.SmoothStep(0f, 1f, ZoomT));

        public void FocusOn(Vector3 p, bool instant = false)
        {
            _focusTarget = new Vector3(p.x, 0f, p.z);
            if (instant) Focus = _focusTarget;
        }

        public void SetZoom(float dist) => _distTarget = Mathf.Clamp(dist, MinDist, MaxDist);

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        /// <summary>Called by the input layer each frame with already-filtered camera input.</summary>
        public void Control(float dt, Vector2 pan, float rotate, float zoom, bool middleHeld, Vector2 mouse, bool rotateDrag)
        {
            float speed = Mathf.Lerp(18f, 90f, ZoomT);
            var fwd = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
            _focusTarget += (fwd * pan.y + right * pan.x) * speed * dt;
            _yawTarget += rotate * 110f * dt;
            if (Mathf.Abs(zoom) > 0.01f) _distTarget = Mathf.Clamp(_distTarget * (1f - zoom * 0.12f), MinDist, MaxDist);

            // Middle-drag pans by grabbing the ground; Alt+middle-drag rotates.
            if (middleHeld)
            {
                if (!_dragging)
                {
                    _dragging = true;
                    _dragAnchor = mouse;
                }
                Vector2 delta = (Vector2)mouse - (Vector2)_dragAnchor;
                _dragAnchor = mouse;
                if (rotateDrag) _yawTarget += delta.x * 0.25f;
                else
                {
                    float k = Distance / Mathf.Max(200f, Screen.height) * 1.4f;
                    _focusTarget -= (right * delta.x + fwd * delta.y) * k;
                }
            }
            else _dragging = false;
        }

        public void Update(float dt, World w)
        {
            float size = Balance.MapSize;
            _focusTarget.x = Mathf.Clamp(_focusTarget.x, 0f, size);
            _focusTarget.z = Mathf.Clamp(_focusTarget.z, 0f, size);
            float k = 1f - Mathf.Exp(-10f * dt);
            Focus = Vector3.Lerp(Focus, _focusTarget, k);
            Distance = Mathf.Lerp(Distance, _distTarget, 1f - Mathf.Exp(-8f * dt));
            Yaw = Mathf.LerpAngle(Yaw, _yawTarget, 1f - Mathf.Exp(-12f * dt));

            float ground = w.HeightAt(new Vec2(Focus.x, Focus.z));
            var look = new Vector3(Focus.x, ground + 1.2f, Focus.z);
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            var pos = look + rot * new Vector3(0f, 0f, -Distance);
            float minY = w.HeightAt(new Vec2(pos.x, pos.z)) + 2f;
            if (pos.y < minY) pos.y = minY;
            if (_shake > 0f)
            {
                pos += Random.insideUnitSphere * _shake * 0.4f;
                _shake = Mathf.Max(0f, _shake - dt * 2.5f);
            }
            Cam.transform.position = pos;
            Cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        public Ray ScreenRay(Vector2 screen) => Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));

        public bool GroundPoint(World w, Vector2 screen, out Vec2 hit)
        {
            var ray = ScreenRay(screen);
            return w.Height.Raycast(ray.origin.x, ray.origin.y, ray.origin.z, ray.direction.x, ray.direction.y, ray.direction.z, 1200f, out hit);
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
