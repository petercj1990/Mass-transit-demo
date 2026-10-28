using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// WoW-style minimap in the top-right corner, centred on the kin-band and north-up, plus a full map
    /// (M). Both show only ground the kin-band has explored; enemies appear only while in sight.
    /// </summary>
    public sealed partial class Hud
    {
        private const float MiniSize = 190f;
        private const float MiniSpan = 110f; // metres across the minimap
        private readonly Texture2D _minimap;
        private Color32[] _miniPx;
        private float _minimapTimer;

        private void UpdateMinimap(float dt)
        {
            var w = Wd;
            if (w == null || _g.View == null) return;
            _minimapTimer -= dt;
            if (_minimapTimer > 0f) return;
            _minimapTimer = 0.25f;

            int s = TerrainView.MinimapSize;
            var basePx = _g.View.Terrain.MinimapPixels;
            if (_miniPx == null || _miniPx.Length != basePx.Length) _miniPx = new Color32[basePx.Length];
            float cell = (float)Balance.MapSize / s;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int i = y * s + x;
                var p = new Vec2((x + 0.5f) * cell, (y + 0.5f) * cell);
                byte f = w.RevealMap ? (byte)2 : w.Fog.At(p);
                Color32 c = basePx[i];
                if (f == 0) c = new Color32(6, 7, 9, 255);
                else if (f == 1) c = new Color32((byte)(c.r * 0.6f), (byte)(c.g * 0.6f), (byte)(c.b * 0.6f), 255);
                _miniPx[i] = c;
            }

            void Dot(Vec2 p, Color32 c, int r)
            {
                int cx = (int)(p.X / cell), cy = (int)(p.Z / cell);
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= s || y >= s) continue;
                    _miniPx[y * s + x] = c;
                }
            }
            bool Known(Vec2 p) => w.RevealMap || w.Fog.IsExplored(p);

            foreach (var n in w.Nodes)
            {
                if (!n.Removed && Known(n.Pos)) Dot(n.Pos, Palette.Resource(n.Kind) * 1.3f, 1);
            }
            foreach (var st in w.Structures)
            {
                if (st.Removed || !Known(st.Pos)) continue;
                if (st.Kind == StructureKind.Building) Dot(st.Pos, new Color32(220, 200, 160, 255), 2);
                else if (st.Kind == StructureKind.Hatch) Dot(st.Pos, new Color32(255, 160, 40, 255), 2);
            }
            foreach (var c in w.Caches)
            {
                if (!c.Removed) Dot(c.Pos, new Color32(255, 170, 40, 255), 2);
            }
            if (w.Boss != null && !w.Boss.Defeated && Known(w.ArenaCenter)) Dot(w.ArenaCenter, new Color32(200, 40, 30, 255), 3);
            foreach (var u in w.Units)
            {
                if (!u.IsActive) continue;
                if (u.IsPlayer) Dot(u.Pos, Palette.Squad(u.Squad != null ? u.Squad.ColorIndex : 0), 1);
                else if (w.IsVisibleToPlayer(u.Pos)) Dot(u.Pos, new Color32(255, 60, 50, 255), u.Kind == UnitKind.Rustmaw ? 3 : 1);
            }
            foreach (var a in w.Alerts)
            {
                if (a.HasPos && a.Severity >= 2 && w.Time - a.Time < 6f && ((int)(w.Time * 4f) % 2 == 0)) Dot(a.Pos, new Color32(255, 255, 255, 255), 2);
            }
            _minimap.SetPixels32(_miniPx);
            _minimap.Apply(false);
        }

        /// <summary>Where the minimap is centred: the kin-band's banner (or home while garrisoned).</summary>
        private Vec2 MiniCenter(World w)
        {
            var sq = _g.Commands.Squad;
            if (sq == null) return w.BasePos;
            return sq.Garrisoned ? w.BasePos : sq.Anchor;
        }

        private void DrawMinimap(World w)
        {
            var r = Panel(new Rect(W - MiniSize - 16, 34, MiniSize + 8, MiniSize + 8));
            var map = new Rect(r.x + 4, r.y + 4, MiniSize, MiniSize);
            Vec2 c = MiniCenter(w);
            float span = MiniSpan / Balance.MapSize;
            var uv = new Rect(c.X / Balance.MapSize - span / 2f, c.Z / Balance.MapSize - span / 2f, span, span);
            GUI.DrawTextureWithTexCoords(map, _minimap, uv);

            // The kin-band (centre) and where the camera looks.
            var mid = map.center;
            Swatch(new Rect(mid.x - 3, mid.y - 3, 6, 6), Palette.KinMark);
            var fwd = _g.Rig.Forward;
            for (int i = 1; i <= 5; i++)
                Swatch(new Rect(mid.x + fwd.x * i * 3f - 1, mid.y - fwd.z * i * 3f - 1, 2, 2), new Color(1f, 1f, 1f, 0.8f));
            GUI.Label(new Rect(map.x + 4, map.y + 2, 20, 16), "N", _tiny);

            var e = Event.current;
            if (e.type == EventType.MouseDown && map.Contains(e.mousePosition))
            {
                float k = MiniSpan / map.width;
                var world = new Vec2(c.X + (e.mousePosition.x - mid.x) * k, c.Z - (e.mousePosition.y - mid.y) * k);
                if (e.button == 1) MarchTo(w, world);
                else _showMap = true;
                e.Use();
            }
        }

        /// <summary>The map of everything explored so far (M). Right-click to march there.</summary>
        private void DrawWorldMap(World w)
        {
            float size = Mathf.Min(H - 140, W - 120, 680f);
            var r = Panel(new Rect(W / 2 - size / 2 - 6, 70, size + 12, size + 34));
            GUI.Label(new Rect(r.x + 10, r.y + 4, size, 20), "<b>Map</b>  <size=10>explored ground only.  Right-click to march there.  M or Esc closes.</size>", _small);
            var map = new Rect(r.x + 6, r.y + 28, size, size);
            GUI.DrawTexture(map, _minimap);
            Vec2 c = MiniCenter(w);
            var p = new Vector2(map.x + c.X / Balance.MapSize * map.width, map.yMax - c.Z / Balance.MapSize * map.height);
            Swatch(new Rect(p.x - 4, p.y - 4, 8, 8), Palette.KinMark);
            var bp = new Vector2(map.x + w.BasePos.X / Balance.MapSize * map.width, map.yMax - w.BasePos.Z / Balance.MapSize * map.height);
            GUI.Label(new Rect(bp.x + 6, bp.y - 8, 80, 16), "home", _tiny);

            var e = Event.current;
            if (e.type == EventType.MouseDown && map.Contains(e.mousePosition))
            {
                var world = new Vec2((e.mousePosition.x - map.x) / map.width * Balance.MapSize, (map.yMax - e.mousePosition.y) / map.height * Balance.MapSize);
                if (e.button == 1) MarchTo(w, world);
                e.Use();
            }
        }

        private void MarchTo(World w, Vec2 at)
        {
            if (_g.Selection.Squads.Count == 0) return;
            var o = PlayerOrder.For(w.InBase(at) ? OrderType.ReturnToBase : OrderType.Move, _g.Selection.Squads);
            o.Pos = at;
            w.Issue(o);
        }
    }
}
