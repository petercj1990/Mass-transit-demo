using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Readable tells: a red outline of where the attack will land plus a fill that sweeps in as the
    /// tell runs out. Meteor strikes also show the rock falling.
    /// </summary>
    public sealed class TelegraphView
    {
        public readonly int Id;
        private readonly Decal _outline;
        private readonly Decal _fill;
        private readonly GameObject _meteor;
        private readonly Color _color;
        private bool _placed;

        public TelegraphView(Telegraph t, Transform parent)
        {
            Id = t.Id;
            _color = t.Label == "Fire Wave" ? Palette.Fire : (t.Label == "Burrow Eruption" ? new Color(0.85f, 0.45f, 0.2f) : Palette.Telegraph);
            var tex = t.Shape == TelegraphShape.Circle ? Tex.Disc : (t.Shape == TelegraphShape.Line ? Tex.Rect : Tex.Cone(t.ArcDeg));
            _outline = new Decal(parent, tex, new Color(_color.r, _color.g, _color.b, 0.4f), "Tell " + t.Label);
            _fill = new Decal(parent, tex, new Color(_color.r, _color.g, _color.b, 0.35f), "TellFill " + t.Label);
            if (t.Label == "Meteor Strike")
            {
                _meteor = Prim.Ball(parent, Vector3.zero, Vector3.one * 2.2f, Mats.Glow(Palette.Fire, 3f));
            }
        }

        public void Sync(World w, Telegraph t, bool visible)
        {
            _outline.SetActive(visible);
            _fill.SetActive(visible);
            if (_meteor != null) _meteor.SetActive(visible && t.Remaining > 0f);
            if (!visible) return;

            float p = Mathf.Clamp01(t.Progress);
            if (!_placed)
            {
                _placed = true;
                Place(_outline, w, t, 1f);
            }
            Place(_fill, w, t, Mathf.Max(0.05f, p));
            float pulse = 0.4f + Mathf.Sin(Time.time * (6f + p * 14f)) * 0.12f;
            float fade = t.Remaining < 0f ? Mathf.Clamp01(1f + t.Remaining * 2f) : 1f;
            _outline.SetColor(new Color(_color.r, _color.g, _color.b, pulse * fade));
            _fill.SetColor(new Color(_color.r, _color.g, _color.b, (0.25f + p * 0.25f) * fade));

            if (_meteor != null)
            {
                var ground = ToUnity.Ground(w, t.Pos);
                _meteor.transform.position = ground + new Vector3(-8f, 70f, 6f) * (1f - p);
            }
        }

        private static void Place(Decal d, World w, Telegraph t, float k)
        {
            switch (t.Shape)
            {
                case TelegraphShape.Circle:
                    d.Place(w, t.Pos, Vec2.Forward, t.Radius * k, t.Radius * k);
                    break;
                case TelegraphShape.Line:
                {
                    float len = t.Length * k;
                    d.Place(w, t.Pos + t.Dir * (len * 0.5f), t.Dir, t.Width * 0.5f, len * 0.5f);
                    break;
                }
                case TelegraphShape.Cone:
                {
                    float r = t.Radius * k;
                    d.Place(w, t.Pos + t.Dir * (r * 0.5f), t.Dir, r, r * 0.5f);
                    break;
                }
            }
        }

        public void Destroy()
        {
            _outline.Destroy();
            _fill.Destroy();
            if (_meteor != null) Object.Destroy(_meteor);
        }
    }
}
