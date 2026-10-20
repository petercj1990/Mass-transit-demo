using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>Renders one sim unit: interpolated movement, poses, glow and status effects.</summary>
    public sealed class UnitView
    {
        public readonly int Id;
        public readonly UnitModel Model;
        public bool Visible { get; private set; }
        public Vector3 Position => Model.Root.transform.position;

        private float _yaw;
        private float _swing;
        private float _hurt;
        private float _tierPulse;
        private float _lastKin = -1f;
        private float _lastVent = -1f;
        private Color _lastBody = Color.clear;

        private GameObject _ring;
        private Material _ringMat;
        private GameObject _burn, _ice, _cage, _stun;
        private readonly GameObject[] _cores = new GameObject[4];

        public UnitView(Unit u, Transform parent)
        {
            Id = u.Id;
            Model = ModelFactory.BuildUnit(u, parent);
            Model.Root.SetActive(false);
            _yaw = u.Facing * Mathf.Rad2Deg;
        }

        public void Destroy()
        {
            if (Model.Root != null) Object.Destroy(Model.Root);
        }

        public void OnSwing() => _swing = 1f;
        public void OnHurt() => _hurt = 1f;
        public void OnTierUp() => _tierPulse = 1f;

        public void Sync(World w, Unit u, float alpha, float dt, bool selected, Color squadColor)
        {
            bool visible;
            if (u.IsPlayer) visible = !u.Garrisoned && (u.Life != LifeState.Dead || w.Time - u.DiedAt < 2.5f);
            else visible = w.IsVisibleToPlayer(u.Pos) && (u.Life == LifeState.Active || w.Time - u.DiedAt < 3f)
                           && !(u.Untargetable && u.Kind == UnitKind.Rustmaw);
            if (visible != Visible)
            {
                Visible = visible;
                Model.Root.SetActive(visible);
            }
            if (!visible) return;

            float now = Time.time;
            Vec2 p = Vec2.Lerp(u.PrevPos, u.Pos, alpha);
            float y = w.HeightAt(p) + u.HeightOffset;
            if (u.Def.Flying) y += Mathf.Sin(now * 2.2f + Id) * 0.15f;
            var root = Model.Root.transform;
            root.position = new Vector3(p.X, y, p.Z);
            _yaw = Mathf.LerpAngle(_yaw, u.Facing * Mathf.Rad2Deg, 1f - Mathf.Exp(-14f * dt));
            root.rotation = Quaternion.Euler(0f, _yaw, 0f);

            Pose(w, u, dt, now);
            Glow(w, u, dt, now);
            StatusEffects(u, now);
            Cores(u, now);
            Ring(selected && u.Life != LifeState.Dead, squadColor, u);
        }

        private void Pose(World w, Unit u, float dt, float now)
        {
            Vector3 pos = Vector3.zero;
            Vector3 euler = Vector3.zero;
            _swing = Mathf.Max(0f, _swing - dt * 4f);
            _hurt = Mathf.Max(0f, _hurt - dt * 5f);

            if (u.Life == LifeState.Downed)
            {
                euler.x = -78f;
                pos.y = 0.25f;
                pos.z = -0.4f;
            }
            else if (u.Life == LifeState.Dead)
            {
                float t = w.Time - u.DiedAt;
                euler.x = u.IsPlayer ? -84f : 84f;
                pos.y = 0.2f - t * 0.35f;
            }
            else
            {
                bool flyer = u.Def.Flying;
                if (u.Moving && !flyer) pos.y += Mathf.Abs(Mathf.Sin(now * 9f + Id)) * 0.07f;
                float lunge = Mathf.Sin(_swing * Mathf.PI);
                pos.z += lunge * (u.Kind == UnitKind.Rustmaw ? 0.8f : 0.28f);
                euler.x += lunge * 14f;
                if (u.Action == UnitAction.Casting || u.Action == UnitAction.Channel) pos.y += 0.06f;
                if (u.Action == UnitAction.Resting)
                {
                    pos.y -= 0.32f;
                    euler.x -= 6f;
                }
                if (u.Action == UnitAction.Gathering) euler.x += 12f + Mathf.Sin(now * 7f + Id) * 12f;
                if (u.Status.StunTime > 0f || u.Status.FrozenTime > 0f) euler.z += Mathf.Sin(now * 20f) * 3f;
                if (u.Kind == UnitKind.Rustmaw && w.Boss != null && w.Boss.Staggered) euler.z += Mathf.Sin(now * 14f) * 5f;
            }
            Model.Rig.localPosition = pos;
            Model.Rig.localRotation = Quaternion.Euler(euler);
            float pulse = 1f + _hurt * 0.06f;
            float baseScale = u.Kind == UnitKind.Shieldbearer ? 1.08f : (u.Kind == UnitKind.ScrapHound ? 0.72f : 1f);
            Model.Rig.localScale = Vector3.one * (baseScale * pulse);
        }

        private void Glow(World w, Unit u, float dt, float now)
        {
            if (Model.KinMat != null)
            {
                // Veterans glow brighter; injured kin-marks dim.
                _tierPulse = Mathf.Max(0f, _tierPulse - dt * 0.6f);
                float k = 0.45f + 0.4f * (u.Doctrine != null ? u.Doctrine.MaxTier : 0);
                if (u.Injured) k *= 0.35f;
                if (u.Life != LifeState.Active) k = 0.05f;
                k += _tierPulse * 3f;
                if (Mathf.Abs(k - _lastKin) > 0.02f)
                {
                    _lastKin = k;
                    Mats.SetEmission(Model.KinMat, Palette.KinMark * k);
                }
            }
            if (Model.VentMat != null && w.Boss != null)
            {
                float v = 1.2f;
                if (w.Boss.VentsExposed) v = 4f + Mathf.Sin(now * 12f) * 1.2f;
                if (w.Boss.Overheated) v = Mathf.Max(v, 3f + Mathf.Sin(now * 6f));
                if (w.Boss.BlastPending) v = 6f + Mathf.Sin(now * 25f) * 2f;
                if (Mathf.Abs(v - _lastVent) > 0.05f)
                {
                    _lastVent = v;
                    Mats.SetEmission(Model.VentMat, Palette.Fire * v);
                }
                if (Model.BodyMat != null)
                {
                    Color body = w.Boss.Overheated ? new Color(0.55f, 0.22f, 0.12f) : new Color(0.36f, 0.24f, 0.19f);
                    if (body != _lastBody)
                    {
                        _lastBody = body;
                        Mats.SetColor(Model.BodyMat, body);
                        Mats.SetEmission(Model.BodyMat, w.Boss.Overheated ? new Color(0.5f, 0.12f, 0.03f) : Color.black);
                    }
                }
            }
        }

        private GameObject Fx(ref GameObject slot, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
        {
            if (slot == null)
            {
                slot = Prim.Make(type, Model.Root.transform, pos, scale, Mats.Transparent(c), default, false);
            }
            return slot;
        }

        private void StatusEffects(Unit u, float now)
        {
            bool active = u.Life == LifeState.Active;
            float h = Model.Height;

            bool burning = active && u.Status.BurnTime > 0f;
            if (burning || _burn != null)
            {
                Fx(ref _burn, PrimitiveType.Sphere, new Vector3(0f, h * 0.5f, 0f), Vector3.one, new Color(1f, 0.45f, 0.1f, 0.35f));
                _burn.SetActive(burning);
                if (burning) _burn.transform.localScale = Vector3.one * (h * 0.55f + Mathf.Sin(now * 30f + Id) * 0.08f);
            }
            bool frozen = active && u.Status.FrozenTime > 0f;
            if (frozen || _ice != null)
            {
                Fx(ref _ice, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, 0f), new Vector3(1.2f, h, 1.2f) * (u.Radius * 1.4f), new Color(0.6f, 0.9f, 1f, 0.45f));
                _ice.SetActive(frozen);
            }
            bool caged = active && u.Status.RootTime > 0f;
            if (caged || _cage != null)
            {
                if (_cage == null)
                {
                    _cage = new GameObject("Cage");
                    _cage.transform.SetParent(Model.Root.transform, false);
                    var bar = Mats.Glow(Palette.KinMark, 1.5f);
                    float r = Mathf.Max(0.7f, u.Radius + 0.25f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Prim.Box(_cage.transform, new Vector3(Mathf.Cos(a) * r, h * 0.5f, Mathf.Sin(a) * r), new Vector3(0.06f, h * 1.05f, 0.06f), bar, default, false);
                    }
                    Prim.Tube(_cage.transform, new Vector3(0f, h, 0f), new Vector3(r * 2.1f, 0.03f, r * 2.1f), bar, default, false);
                }
                _cage.SetActive(caged);
            }
            bool stunned = active && u.Status.StunTime > 0f && !frozen;
            if (stunned || _stun != null)
            {
                if (_stun == null)
                {
                    _stun = new GameObject("Stun");
                    _stun.transform.SetParent(Model.Root.transform, false);
                    var star = Mats.Glow(new Color(1f, 0.9f, 0.3f), 2f);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI * 2f / 3f;
                        Prim.Box(_stun.transform, new Vector3(Mathf.Cos(a) * 0.35f, 0f, Mathf.Sin(a) * 0.35f), Vector3.one * 0.1f, star, new Vector3(45f, 45f, 0f), false);
                    }
                }
                _stun.SetActive(stunned);
                if (stunned)
                {
                    _stun.transform.localPosition = new Vector3(0f, h + 0.3f, 0f);
                    _stun.transform.localRotation = Quaternion.Euler(0f, now * 360f, 0f);
                }
            }
        }

        private void Cores(Unit u, float now)
        {
            int n = u.Life == LifeState.Active ? Mathf.Min(u.CarriedCores.Count, _cores.Length) : 0;
            for (int i = 0; i < _cores.Length; i++)
            {
                bool on = i < n;
                if (on && _cores[i] == null)
                    _cores[i] = Prim.Ball(Model.Root.transform, Vector3.zero, Vector3.one * 0.22f, Mats.Glow(Palette.KinMark, 3f), default, false);
                if (_cores[i] == null) continue;
                _cores[i].SetActive(on);
                if (!on) continue;
                float a = now * 2f + i * Mathf.PI * 2f / Mathf.Max(1, n);
                _cores[i].transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, Model.Height + 0.45f, Mathf.Sin(a) * 0.45f);
            }
        }

        private void Ring(bool show, Color c, Unit u)
        {
            if (!show)
            {
                if (_ring != null && _ring.activeSelf) _ring.SetActive(false);
                return;
            }
            if (_ring == null)
            {
                _ringMat = Mats.NewTransparent(c, Tex.Ring);
                _ring = Prim.Mesh(MeshKit.Quad, Model.Root.transform, new Vector3(0f, 0.08f, 0f), Vector3.one * (u.Radius * 2.6f + 0.4f), _ringMat, default, false);
            }
            if (!_ring.activeSelf) _ring.SetActive(true);
            Mats.SetColor(_ringMat, new Color(c.r, c.g, c.b, u.Injured ? 0.55f : 0.9f));
            // keep the ring flat on the ground even while the rig tilts
            _ring.transform.localPosition = new Vector3(0f, 0.08f - u.HeightOffset, 0f);
        }
    }
}
