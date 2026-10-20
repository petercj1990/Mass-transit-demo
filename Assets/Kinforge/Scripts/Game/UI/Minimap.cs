using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    public sealed partial class Hud
    {
        private const float MiniSize = 184f;
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
                if (f == 0) c = new Color32(12, 13, 16, 255);
                else if (f == 1) c = new Color32((byte)(c.r * 0.55f), (byte)(c.g * 0.55f), (byte)(c.b * 0.55f), 255);
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

            foreach (var n in w.Nodes)
            {
                if (!n.Removed && (w.RevealMap || w.Fog.IsExplored(n.Pos))) Dot(n.Pos, Palette.Resource(n.Kind) * 1.3f, 0);
            }
            foreach (var st in w.Structures)
            {
                if (st.Removed) continue;
                if (st.Kind == StructureKind.Building) Dot(st.Pos, new Color32(220, 200, 160, 255), 1);
                else if (st.Kind == StructureKind.Hatch) Dot(st.Pos, new Color32(255, 160, 40, 255), 1);
            }
            foreach (var c in w.Caches) Dot(c.Pos, new Color32(255, 170, 40, 255), 1);
            if (w.Boss != null && !w.Boss.Defeated && (w.RevealMap || w.Fog.IsExplored(w.ArenaCenter))) Dot(w.ArenaCenter, new Color32(200, 40, 30, 255), 2);
            foreach (var u in w.Units)
            {
                if (!u.IsActive) continue;
                if (u.IsPlayer) Dot(u.Pos, Palette.Squad(u.Squad != null ? u.Squad.ColorIndex : 0), 1);
                else if (w.IsVisibleToPlayer(u.Pos)) Dot(u.Pos, new Color32(255, 60, 50, 255), u.Kind == UnitKind.Rustmaw ? 2 : 0);
            }
            foreach (var a in w.Alerts)
            {
                if (a.HasPos && a.Severity >= 2 && w.Time - a.Time < 6f && ((int)(w.Time * 4f) % 2 == 0)) Dot(a.Pos, new Color32(255, 255, 255, 255), 2);
            }
            _minimap.SetPixels32(_miniPx);
            _minimap.Apply(false);
        }

        private void DrawMinimap(World w)
        {
            var r = Panel(new Rect(8, H - MiniSize - 12, MiniSize + 8, MiniSize + 8));
            var map = new Rect(r.x + 4, r.y + 4, MiniSize, MiniSize);
            // Texture rows go bottom-up (z = 0 at the bottom), which matches GUI drawing of a texture.
            GUI.DrawTexture(map, _minimap);

            // Camera focus marker
            var f = _g.Rig.Focus;
            var fp = new Vector2(map.x + f.x / Balance.MapSize * map.width, map.yMax - f.z / Balance.MapSize * map.height);
            float half = Mathf.Lerp(6f, 30f, _g.Rig.ZoomT);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.DrawTexture(new Rect(fp.x - half, fp.y - half, half * 2, 1), Tex.White);
            GUI.DrawTexture(new Rect(fp.x - half, fp.y + half, half * 2, 1), Tex.White);
            GUI.DrawTexture(new Rect(fp.x - half, fp.y - half, 1, half * 2), Tex.White);
            GUI.DrawTexture(new Rect(fp.x + half, fp.y - half, 1, half * 2 + 1), Tex.White);
            GUI.color = old;

            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && map.Contains(e.mousePosition))
            {
                var world = new Vec2((e.mousePosition.x - map.x) / map.width * Balance.MapSize, (map.yMax - e.mousePosition.y) / map.height * Balance.MapSize);
                if (e.button == 0)
                {
                    _g.Rig.FocusOn(ToUnity.Ground(w, world), e.type == EventType.MouseDrag);
                    e.Use();
                }
                else if (e.button == 1 && e.type == EventType.MouseDown && _g.Selection.Squads.Count > 0)
                {
                    var o = PlayerOrder.For(OrderType.Move, _g.Selection.Squads);
                    o.Pos = world;
                    w.Issue(o);
                    e.Use();
                }
            }
        }
    }
}
