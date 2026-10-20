using System.Collections.Generic;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Immediate-mode HUD. Drawn in a virtual space scaled to the window height so it reads the same
    /// on any resolution. Panels register their rects so world clicks underneath are ignored.
    /// </summary>
    public sealed partial class Hud
    {
        private struct FloatText
        {
            public Vector3 Pos;
            public string Text;
            public Color Color;
            public float Age;
            public float Size;
        }

        private readonly KinforgeGame _g;
        private readonly List<Rect> _uiRects = new List<Rect>();
        private readonly List<FloatText> _floats = new List<FloatText>(64);
        private float _scale = 1f;
        private float W => Screen.width / _scale;
        private float H => Screen.height / _scale;

        private bool _showHelp = true;
        private bool _showBase;
        private bool _showLog;
        private bool _victoryDismissed;
        private bool _showObjectives = true;
        private Vector2 _logScroll;
        private int _reportSeen;
        private float _reportShownAt = -100f;

        private GUIStyle _panel, _label, _small, _tiny, _title, _big, _button, _buttonOn, _buttonOff, _bar, _center, _floatStyle, _tooltip, _hidden;
        private GUIStyle _tinyBold, _tinyCenterBold, _tinyCenterWhite, _smallRight, _wrapTiny, _wrapSmall, _wrapLabel;
        private int _lastCardClickSquad;
        private float _lastCardClickTime;
        private bool _stylesReady;

        public Hud(KinforgeGame g)
        {
            _g = g;
            _minimap = new Texture2D(TerrainView.MinimapSize, TerrainView.MinimapSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        private World Wd => _g.World;

        public bool BlocksWorld => Wd != null && Wd.Victory && !_victoryDismissed;

        public void OnNewWorld()
        {
            _floats.Clear();
            _reportSeen = 0;
            _victoryDismissed = false;
            _minimapTimer = 0f;
        }

        public void ToggleHelp() => _showHelp = !_showHelp;
        public void ToggleBase() => _showBase = !_showBase;
        public void ToggleLog() => _showLog = !_showLog;

        public void OpenBase(BuildingKind k)
        {
            _showBase = true;
        }

        public void Escape()
        {
            if (_showHelp) _showHelp = false;
            else if (_showBase) _showBase = false;
            else if (_showLog) _showLog = false;
            else _g.Selection.InspectUnitId = 0;
        }

        public bool IsPointerOverUi(Vector2 mouse)
        {
            var p = new Vector2(mouse.x, Screen.height - mouse.y);
            foreach (var r in _uiRects)
            {
                if (r.Contains(p)) return true;
            }
            return false;
        }

        public void Float(Vector3 world, string text, Color c, float size = 1f)
        {
            if (_floats.Count > 70) _floats.RemoveAt(0);
            _floats.Add(new FloatText { Pos = world + Random.insideUnitSphere * 0.25f, Text = text, Color = c, Size = size });
        }

        public void Tick(float dt)
        {
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                f.Age += dt;
                f.Pos += Vector3.up * dt * 1.4f;
                if (f.Age > 1.3f) _floats.RemoveAt(i);
                else _floats[i] = f;
            }
            UpdateMinimap(dt);
        }

        // =====================================================================================
        // Styles and helpers
        // =====================================================================================

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;
            var font = GUI.skin.label.font;
            _panel = new GUIStyle { normal = { background = Tex.Solid(new Color(0.06f, 0.065f, 0.075f, 0.86f)) }, padding = new RectOffset(8, 8, 6, 6) };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(0.92f, 0.92f, 0.9f) }, wordWrap = false, richText = true, clipping = TextClipping.Clip };
            _small = new GUIStyle(_label) { fontSize = 11, normal = { textColor = new Color(0.75f, 0.76f, 0.78f) } };
            _tiny = new GUIStyle(_label) { fontSize = 10, normal = { textColor = new Color(0.7f, 0.72f, 0.74f) } };
            _title = new GUIStyle(_label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Palette.KinMark } };
            _big = new GUIStyle(_label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Palette.KinMark } };
            _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12, wordWrap = true, richText = true, alignment = TextAnchor.MiddleCenter,
                normal = { background = Tex.Solid(new Color(0.16f, 0.17f, 0.19f, 0.95f)), textColor = new Color(0.9f, 0.9f, 0.88f) },
                hover = { background = Tex.Solid(new Color(0.25f, 0.26f, 0.29f, 0.95f)), textColor = Color.white },
                active = { background = Tex.Solid(new Color(0.4f, 0.3f, 0.15f, 0.95f)), textColor = Color.white },
                padding = new RectOffset(4, 4, 3, 3),
            };
            _buttonOn = new GUIStyle(_button)
            {
                normal = { background = Tex.Solid(new Color(0.55f, 0.36f, 0.1f, 0.97f)), textColor = Color.white },
                hover = { background = Tex.Solid(new Color(0.65f, 0.43f, 0.12f, 0.97f)), textColor = Color.white },
            };
            _buttonOff = new GUIStyle(_button)
            {
                normal = { background = Tex.Solid(new Color(0.12f, 0.12f, 0.13f, 0.9f)), textColor = new Color(0.5f, 0.5f, 0.5f) },
                hover = { background = Tex.Solid(new Color(0.12f, 0.12f, 0.13f, 0.9f)), textColor = new Color(0.55f, 0.55f, 0.55f) },
            };
            _bar = new GUIStyle { normal = { background = Tex.White } };
            _floatStyle = new GUIStyle(_label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _tooltip = new GUIStyle(_label) { fontSize = 12, wordWrap = true, normal = { background = Tex.Solid(new Color(0.03f, 0.03f, 0.04f, 0.92f)), textColor = Color.white }, padding = new RectOffset(6, 6, 4, 4) };
            _hidden = new GUIStyle();
            _tinyBold = new GUIStyle(_tiny) { fontStyle = FontStyle.Bold };
            _tinyCenterBold = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _tinyCenterWhite = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _smallRight = new GUIStyle(_small) { alignment = TextAnchor.UpperRight };
            _wrapTiny = new GUIStyle(_tiny) { wordWrap = true };
            _wrapSmall = new GUIStyle(_small) { wordWrap = true, fontSize = 12, normal = { textColor = new Color(0.88f, 0.88f, 0.86f) } };
            _wrapLabel = new GUIStyle(_label) { wordWrap = true };
        }

        private Rect Panel(Rect r)
        {
            GUI.Box(r, GUIContent.none, _panel);
            _uiRects.Add(new Rect(r.x * _scale, r.y * _scale, r.width * _scale, r.height * _scale));
            return r;
        }

        private void Bar(Rect r, float frac, Color fill, Color back)
        {
            var old = GUI.color;
            GUI.color = back;
            GUI.DrawTexture(r, Tex.White);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Tex.White);
            GUI.color = old;
        }

        private void Swatch(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Tex.White);
            GUI.color = old;
        }

        private void Shadow(Rect r, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private static string Clock(float t)
        {
            int s = Mathf.FloorToInt(t);
            return $"{s / 60:00}:{s % 60:00}";
        }

        private bool ToGui(Vector3 world, out Vector2 p)
        {
            bool ok = _g.Rig.ToScreen(world, out var px);
            p = px / _scale;
            return ok;
        }

        // =====================================================================================
        // Frame
        // =====================================================================================

        public void Draw()
        {
            var w = Wd;
            if (Event.current.type == EventType.Layout) _uiRects.Clear();
            _scale = Mathf.Clamp(Screen.height / 900f, 0.75f, 2.2f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_scale, _scale, 1f));
            EnsureStyles();

            if (Keys.Broken)
            {
                var r = Panel(new Rect(W / 2 - 300, H / 2 - 70, 600, 140));
                GUI.Label(new Rect(r.x + 12, r.y + 10, r.width - 24, r.height - 20),
                    "<b>Input is disabled.</b>\nThis prototype uses Unity's classic Input Manager. Open <b>Edit > Project Settings > Player > Other Settings</b> and set <b>Active Input Handling</b> to <b>Both</b> (or Input Manager (Old)), then restart Play mode.",
                    _wrapLabel);
                return;
            }

            DrawWorldOverlays(w);
            DrawTopBar(w);
            DrawAlerts(w);
            DrawBossBar(w);
            DrawSquadCards(w);
            DrawMinimap(w);
            DrawCommandBar(w);
            DrawObjectives(w);
            DrawInspect(w);
            DrawReportToast(w);
            if (_showBase) DrawBasePanel(w);
            if (_showLog) DrawLog(w);
            if (_showHelp) DrawHelp();
            if (w.Victory && !_victoryDismissed) DrawVictory(w);
            DrawTooltip(w);
        }

        private void DrawWorldOverlays(World w)
        {
            var sel = _g.Selection;
            // Health bars
            foreach (var u in w.Units)
            {
                if (u.Life == LifeState.Dead || u.Garrisoned) continue;
                var v = _g.View.UnitViewFor(u.Id);
                if (v == null || !v.Visible) continue;
                if (u.Kind == UnitKind.Rustmaw) continue;
                bool player = u.IsPlayer;
                if (!player && u.HpFraction >= 0.999f && u.Group != null && u.Group.State == GroupState.Idle) continue;
                if (!ToGui(v.Position + Vector3.up * (u.Def.Height + 0.55f), out var p)) continue;
                if (p.x < -50 || p.y < -50 || p.x > W + 50 || p.y > H + 50) continue;
                float wBar = player ? 30f : 34f;
                var r = new Rect(p.x - wBar / 2, p.y, wBar, 4f);
                if (u.Life == LifeState.Downed)
                {
                    Shadow(new Rect(p.x - 30, p.y - 8, 60, 16), "DOWN", _tinyCenterBold, new Color(1f, 0.4f, 0.35f));
                    continue;
                }
                Color fill = player ? (u.Injured ? new Color(0.95f, 0.65f, 0.2f) : new Color(0.35f, 0.9f, 0.4f)) : new Color(0.95f, 0.25f, 0.2f);
                Bar(r, u.HpFraction, fill, new Color(0f, 0f, 0f, 0.6f));
                if (player && u.Squad != null && sel.IsSelected(u.Squad.Id))
                    Swatch(new Rect(r.x, r.y - 2f, r.width, 1.5f), Palette.Squad(u.Squad.ColorIndex));
                if (u.Action == UnitAction.Casting || (u.Action == UnitAction.Channel && u.ActionTotal > 0.2f))
                    Bar(new Rect(r.x, r.y + 5f, r.width, 2f), 1f - u.ActionTimer / Mathf.Max(0.01f, u.ActionTotal), new Color(1f, 0.8f, 0.3f), new Color(0f, 0f, 0f, 0.4f));
            }

            // Floating combat text
            foreach (var f in _floats)
            {
                if (!ToGui(f.Pos, out var p)) continue;
                float a = 1f - Mathf.Clamp01((f.Age - 0.8f) / 0.5f);
                _floatStyle.fontSize = Mathf.RoundToInt(13 * f.Size);
                Shadow(new Rect(p.x - 80, p.y - 12, 160, 24), f.Text, _floatStyle, new Color(f.Color.r, f.Color.g, f.Color.b, a));
            }

            // Box selection
            var input = _g.Commands;
            if (input.Boxing && input.BoxRect.width > 8f)
            {
                var b = input.BoxRect;
                var r = new Rect(b.x / _scale, b.y / _scale, b.width / _scale, b.height / _scale);
                var old = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.3f, 0.15f);
                GUI.DrawTexture(r, Tex.White);
                GUI.color = new Color(1f, 0.8f, 0.3f, 0.8f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), Tex.White);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), Tex.White);
                GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), Tex.White);
                GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), Tex.White);
                GUI.color = old;
            }
        }

        private void DrawTopBar(World w)
        {
            var r = Panel(new Rect(0, 0, W, 28));
            float x = 10;
            GUI.Label(new Rect(x, 5, 220, 20), "<b><color=#" + Hex(Palette.KinMark) + ">KINFORGE</color></b>  The Craters", _label);
            x += 210;
            for (int i = 0; i < Stockpile.KindCount; i++)
            {
                var k = (ResourceKind)i;
                Swatch(new Rect(x, 9, 10, 10), Palette.Resource(k));
                GUI.Label(new Rect(x + 14, 5, 110, 20), $"{Stockpile.Name(k)} <b>{w.Stock.Whole(k)}</b>", _small);
                x += k == ResourceKind.Alloy ? 125 : (k == ResourceKind.Stone || k == ResourceKind.Fiber ? 118 : 92);
            }
            int squads = w.Squads.Count;
            int cap = w.Base != null ? w.Base.SquadCap : 1;
            string right = $"Kin-bands {squads}/{cap}    {Clock(w.Time)}    seed {_g.Seed}";
            if (_g.SimSpeed > 1f) right += $"    <color=#ffcc55>speed x{_g.SimSpeed:0} (F9)</color>";
            if (w.RevealMap) right += "    <color=#ffcc55>map revealed (F11)</color>";
            GUI.Label(new Rect(W - 520, 5, 510, 20), right, _smallRight);
        }

        private void DrawAlerts(World w)
        {
            float y = 34;
            if (w.Boss != null && w.Boss.Active) y += 74;
            int shown = 0;
            for (int i = w.Alerts.Count - 1; i >= 0 && shown < 4; i--)
            {
                var a = w.Alerts[i];
                float age = w.Time - a.Time;
                if (age > 10f) break;
                float alpha = Mathf.Clamp01((10f - age) / 2f);
                Color c = a.Severity == 3 ? new Color(1f, 0.82f, 0.35f) : a.Severity == 2 ? new Color(1f, 0.45f, 0.4f) : a.Severity == 1 ? new Color(1f, 0.8f, 0.5f) : new Color(0.88f, 0.9f, 0.92f);
                var size = _label.CalcSize(new GUIContent(a.Text));
                float width = Mathf.Min(W - 640, Mathf.Max(260, size.x + 30));
                var r = new Rect(W / 2 - width / 2, y, width, 22);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Box(r, GUIContent.none, _panel);
                if (a.HasPos)
                {
                    _uiRects.Add(new Rect(r.x * _scale, r.y * _scale, r.width * _scale, r.height * _scale));
                    if (GUI.Button(r, GUIContent.none, _hidden)) _g.Rig.FocusOn(ToUnity.Ground(w, a.Pos));
                }
                GUI.color = old;
                Shadow(new Rect(r.x + 10, r.y + 2, r.width - 20, 20), a.Text + (a.HasPos ? "  <size=10>(click to view)</size>" : ""), _label, new Color(c.r, c.g, c.b, alpha));
                y += 25;
                shown++;
            }
        }

        private void DrawBossBar(World w)
        {
            var boss = w.Boss;
            if (boss == null || !boss.Active) return;
            var body = boss.Body;
            var r = Panel(new Rect(W / 2 - 280, 32, 560, 70));
            GUI.Label(new Rect(r.x + 10, r.y + 4, 540, 20), $"<b>RUSTMAW</b>   {boss.PhaseName}", _title);
            Bar(new Rect(r.x + 10, r.y + 26, 540, 12), body.HpFraction, boss.Overheated ? new Color(1f, 0.4f, 0.1f) : new Color(0.85f, 0.25f, 0.2f), new Color(0f, 0f, 0f, 0.6f));
            Swatch(new Rect(r.x + 10 + 540 * 0.7f, r.y + 24, 2, 16), new Color(1f, 1f, 1f, 0.6f));
            Swatch(new Rect(r.x + 10 + 540 * 0.35f, r.y + 24, 2, 16), new Color(1f, 1f, 1f, 0.6f));
            GUI.Label(new Rect(r.x + 10, r.y + 24, 540, 16), $"{body.Hp:0} / {body.MaxHp:0}", _tinyCenterWhite);
            string hint;
            if (boss.Burrowed) hint = "Burrowed! Scrap hounds incoming. Watch for where it erupts.";
            else if (boss.Staggered) hint = "<color=#ffd34d>STAGGERED: burst it now, hit the vents!</color>";
            else if (boss.BlastPending) hint = "<color=#ff7a3d>FIRE WAVE: get behind a rampart (Stack)!</color>";
            else if (boss.VentsExposed) hint = "<color=#ffd34d>Rear vents exposed: hits from behind deal double damage</color>";
            else if (boss.Phase == 3) hint = $"Overheat: takes double damage.  Next fire wave in {boss.NextBlastIn:0}s";
            else if (boss.Phase == 2 && boss.MendersAlive > 0) hint = $"<color=#ffd34d>{boss.MendersAlive} Scrap Mender(s) welding its plating: right-click them, Focus Fire!</color>";
            else hint = boss.NextChargeIn > 0f ? $"Next line charge in ~{boss.NextChargeIn:0}s  (Spread Out; Warwright ramparts make it crash)" : "Line charge!";
            if (boss.PhaseTime > 80f) hint += "   <color=#ff6050>Enraged</color>";
            GUI.Label(new Rect(r.x + 10, r.y + 44, 540, 20), hint, _small);
        }

        private void DrawTooltip(World w)
        {
            var hover = _g.Commands.Hover;
            string text = null;
            switch (hover.Kind)
            {
                case PickKind.PlayerUnit:
                case PickKind.Enemy:
                {
                    var u = w.FindUnitAny(hover.Id);
                    if (u == null) break;
                    text = u.IsPlayer
                        ? $"<b>{u.Name}</b>  {u.Def.Name}{(u.Injured ? "  <color=#ffb040>Injured</color>" : "")}\n{u.Hp:0}/{u.MaxHp:0} hp"
                        : $"<b>{u.Def.Name}</b>  {u.Hp:0}/{u.MaxHp:0}\n<size=10>{u.Def.Blurb}</size>\n<size=10>Right-click to attack (focus).</size>";
                    break;
                }
                case PickKind.Node:
                {
                    var n = w.FindNode(hover.Id);
                    if (n != null) text = $"<b>{n.Label}</b>\n{Stockpile.Name(n.Kind)}: {n.Amount:0} left\n<size=10>Right-click with a squad selected to gather.</size>";
                    break;
                }
                case PickKind.Cache:
                    text = "<b>Kin-core cache</b>\nA wiped squad's cores. Right-click with another squad to recover, then bring them home.";
                    break;
                case PickKind.Building:
                    text = $"<b>{BuildingName(hover.Building)}</b>\n<size=10>Click to open the base panel (F2).</size>";
                    break;
            }
            if (text == null) return;
            Vector2 m = Keys.Mouse;
            var p = new Vector2(m.x / _scale + 16, (Screen.height - m.y) / _scale + 16);
            float width = 260;
            float height = _tooltip.CalcHeight(new GUIContent(text), width);
            if (p.x + width > W) p.x = W - width - 4;
            if (p.y + height > H) p.y = H - height - 4;
            GUI.Label(new Rect(p.x, p.y, width, height), text, _tooltip);
        }

        public static string BuildingName(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.KinCradle: return "Kin Cradle";
                case BuildingKind.WarHall: return "War Hall";
                case BuildingKind.SalvageBench: return "Salvage Bench";
                case BuildingKind.SiegeYard: return "Siege Yard";
                case BuildingKind.PowerHub: return "Power Hub";
                case BuildingKind.Stockhouse: return "Stockhouse";
                case BuildingKind.Smelter: return "Smelter";
            }
            return k.ToString();
        }
    }
}
