using System.Collections.Generic;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>Ramparts rise out of the ground and sink when they expire; turrets track targets; fields pulse.</summary>
    public sealed class StructureView
    {
        public readonly int Id;
        public readonly GameObject Root;
        private readonly Transform _head;
        private readonly Decal _field;
        private readonly List<Transform> _spikes = new List<Transform>();
        private readonly float _baseY;
        private readonly StructureKind _kind;

        public Vector3 Position => Root.transform.position;

        public StructureView(Structure s, World w, Transform parent)
        {
            Id = s.Id;
            _kind = s.Kind;
            if (s.IsField)
            {
                Root = new GameObject(s.Kind.ToString());
                Root.transform.SetParent(parent, false);
                bool spikes = s.Kind == StructureKind.SpikeField;
                var c = spikes ? new Color(0.75f, 0.35f, 0.2f, 0.35f) : new Color(0.3f, 1f, 0.5f, 0.3f);
                _field = new Decal(Root.transform, Tex.Disc, c, s.Kind.ToString());
                _field.Place(w, s.Pos, Vec2.Forward, s.AreaRadius, s.AreaRadius, 0.1f);
                if (spikes)
                {
                    var mat = Mats.Lit(new Color(0.55f, 0.5f, 0.45f), 0.3f, 0.5f);
                    var rng = new System.Random(s.Id);
                    for (int i = 0; i < 16; i++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float r = Mathf.Sqrt((float)rng.NextDouble()) * s.AreaRadius * 0.9f;
                        var p = s.Pos + new Vec2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                        var spike = Prim.Box(Root.transform, ToUnity.Ground(w, p, 0.2f), new Vector3(0.07f, 0.55f, 0.07f), mat,
                            new Vector3((float)(rng.NextDouble() - 0.5) * 40f, (float)rng.NextDouble() * 360f, (float)(rng.NextDouble() - 0.5) * 40f));
                        _spikes.Add(spike.transform);
                    }
                }
                return;
            }
            Root = ModelFactory.BuildStructure(s, w, parent, out _head);
            _baseY = Root.transform.position.y;
        }

        public void Sync(World w, Structure s, float dt)
        {
            float age = w.Time - s.BuiltAt;
            if (_field != null)
            {
                bool spikes = _kind == StructureKind.SpikeField;
                float fade = s.Life >= 0f ? Mathf.Clamp01(s.Life / 1.5f) : 1f;
                float pulse = spikes ? 0.35f : 0.25f + Mathf.Sin(Time.time * 4f) * 0.08f;
                var c = spikes ? new Color(0.75f, 0.35f, 0.2f, pulse * fade) : new Color(0.3f, 1f, 0.5f, pulse * fade);
                _field.SetColor(c);
                float grow = Mathf.Clamp01(age / 0.3f);
                foreach (var sp in _spikes)
                {
                    var lp = sp.localScale;
                    sp.localScale = new Vector3(lp.x, 0.55f * grow * fade, lp.z);
                }
                return;
            }

            var t = Root.transform;
            float y = _baseY;
            if (s.Kind == StructureKind.Rampart || s.Kind == StructureKind.BoltTurret || s.Kind == StructureKind.MortarPit)
            {
                float rise = Mathf.Clamp01(age / 0.45f);
                rise = 1f - (1f - rise) * (1f - rise);
                y -= (1f - rise) * 2.4f;
                if (s.Life >= 0f && s.Life < 1.2f) y -= (1.2f - s.Life) * 2f;
            }
            t.position = new Vector3(t.position.x, y, t.position.z);
            if (_head != null && (s.Kind == StructureKind.BoltTurret || s.Kind == StructureKind.MortarPit))
            {
                var want = Quaternion.Euler(0f, s.Dir.ToAngle() * Mathf.Rad2Deg, 0f);
                _head.rotation = Quaternion.Slerp(_head.rotation, want, 1f - Mathf.Exp(-10f * dt));
            }
        }

        public void Destroy()
        {
            _field?.Destroy();
            if (Root != null) Object.Destroy(Root);
        }
    }
}
