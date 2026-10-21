using System.Collections.Generic;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    public enum PickKind
    {
        None,
        PlayerUnit,
        Enemy,
        Node,
        Cache,
        Building,
        Ground,
    }

    public struct Pick
    {
        public PickKind Kind;
        public int Id;
        public Vec2 Ground;
        public bool HasGround;
        public BuildingKind Building;
    }

    /// <summary>
    /// Mouse and keyboard: select squads (click, box, number keys), give orders by right-clicking the
    /// world, set combat commands with hotkeys, and drive the camera. The player never casts unit
    /// abilities; they only set intent.
    /// </summary>
    public sealed class CommandInput
    {
        private readonly KinforgeGame _game;
        private bool _boxing;
        private Vector2 _boxStart;
        private float _lastClickTime;
        private int _lastClickUnit;
        private float _lastNumberTime;
        private int _lastNumber = -1;

        public Pick Hover { get; private set; }
        public bool Boxing => _boxing;
        public Rect BoxRect { get; private set; }

        public static readonly KeyCode[] CommandKeys =
        {
            KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N, KeyCode.M, KeyCode.G, KeyCode.R, KeyCode.T,
        };

        public static readonly CommandType[] CommandForKey =
        {
            CommandType.Charge, CommandType.DefensiveFormation, CommandType.HoldPosition, CommandType.Kite, CommandType.FocusFire,
            CommandType.SpreadOut, CommandType.Stack, CommandType.FallBack, CommandType.Rest, CommandType.Fortify,
        };

        public CommandInput(KinforgeGame game)
        {
            _game = game;
        }

        private World W => _game.World;
        private Selection Sel => _game.Selection;

        public void Update(float dt)
        {
            if (W == null || Keys.Broken) return;
            Vector2 mouse = Keys.Mouse;
            bool overUi = _game.Hud.IsPointerOverUi(mouse);

            CameraInput(dt, mouse, overUi);
            Hover = overUi ? default : PickAt(mouse);
            Hotkeys();

            if (_game.Hud.BlocksWorld) return;

            // Left mouse: click or box select
            if (Keys.MouseDown(0) && !overUi)
            {
                _boxing = true;
                _boxStart = mouse;
            }
            if (_boxing)
            {
                var min = Vector2.Min(_boxStart, mouse);
                var max = Vector2.Max(_boxStart, mouse);
                BoxRect = new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
                if (Keys.MouseUp(0) || !Keys.MouseHeld(0))
                {
                    _boxing = false;
                    if ((mouse - _boxStart).magnitude > 8f) BoxSelect(min, max);
                    else Click(PickAt(mouse));
                }
            }

            if (Keys.MouseDown(1) && !overUi) Order(PickAt(mouse));
        }

        private void CameraInput(float dt, Vector2 mouse, bool overUi)
        {
            var rig = _game.Rig;
            Vector2 pan = Vector2.zero;
            if (Keys.Held(KeyCode.W) || Keys.Held(KeyCode.UpArrow)) pan.y += 1f;
            if (Keys.Held(KeyCode.S) || Keys.Held(KeyCode.DownArrow)) pan.y -= 1f;
            if (Keys.Held(KeyCode.D) || Keys.Held(KeyCode.RightArrow)) pan.x += 1f;
            if (Keys.Held(KeyCode.A) || Keys.Held(KeyCode.LeftArrow)) pan.x -= 1f;
            if (rig.EdgeScroll && Application.isFocused)
            {
                const float edge = 6f;
                if (mouse.x <= edge) pan.x -= 1f;
                if (mouse.x >= Screen.width - edge) pan.x += 1f;
                if (mouse.y <= edge) pan.y -= 1f;
                if (mouse.y >= Screen.height - edge) pan.y += 1f;
            }
            float rotate = 0f;
            if (Keys.Held(KeyCode.Q)) rotate -= 1f;
            if (Keys.Held(KeyCode.E)) rotate += 1f;
            float zoom = overUi ? 0f : Keys.Scroll;
            bool middle = Keys.MouseHeld(2);
            bool alt = Keys.Held(KeyCode.LeftAlt) || Keys.Held(KeyCode.RightAlt);
            rig.Control(dt, Vector2.ClampMagnitude(pan, 1f), rotate, zoom, middle, mouse, alt);
        }

        // ------------------------------------------------------------------------------------
        // Picking
        // ------------------------------------------------------------------------------------

        public Pick PickAt(Vector2 mouse)
        {
            var w = W;
            var pick = new Pick { Kind = PickKind.None };
            var rig = _game.Rig;
            if (rig.GroundPoint(w, mouse, out var g))
            {
                pick.Ground = g;
                pick.HasGround = true;
                pick.Kind = PickKind.Ground;
            }

            // Units: screen-space picking works for flyers and the boss alike.
            Unit best = null;
            float bestScore = float.MaxValue;
            var gui = new Vector2(mouse.x, Screen.height - mouse.y);
            foreach (var u in w.Units)
            {
                var v = _game.View.UnitViewFor(u.Id);
                if (v == null || !v.Visible || u.Life == LifeState.Dead) continue;
                var center = v.Position + Vector3.up * (v.VisualHeight * 0.5f);
                if (!rig.ToScreen(center, out var sp)) continue;
                rig.ToScreen(center + rig.Cam.transform.right * (u.Radius + 0.35f), out var edge);
                float radius = Mathf.Max(14f, (edge - sp).magnitude * 1.2f);
                float d = (sp - gui).magnitude;
                if (d > radius) continue;
                float score = d / radius;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = u;
                }
            }
            if (best != null)
            {
                pick.Kind = best.IsPlayer ? PickKind.PlayerUnit : PickKind.Enemy;
                pick.Id = best.Id;
                return pick;
            }
            if (!pick.HasGround) return pick;

            foreach (var c in w.Caches)
            {
                if (!c.Removed && Vec2.Distance(c.Pos, g) < 2.8f)
                {
                    pick.Kind = PickKind.Cache;
                    pick.Id = c.Id;
                    return pick;
                }
            }
            foreach (var n in w.Nodes)
            {
                if (!n.Removed && Vec2.Distance(n.Pos, g) < n.Radius + 1.3f)
                {
                    pick.Kind = PickKind.Node;
                    pick.Id = n.Id;
                    return pick;
                }
            }
            foreach (var s in w.Structures)
            {
                if (s.Removed || s.Kind != StructureKind.Building) continue;
                Vec2 d = g - s.Pos;
                if (Mathf.Abs(Vec2.Dot(d, s.Dir)) <= s.HalfLen + 1f && Mathf.Abs(Vec2.Dot(d, s.Dir.Right)) <= s.HalfWidth + 1f)
                {
                    pick.Kind = PickKind.Building;
                    pick.Id = s.Id;
                    pick.Building = s.Building;
                    return pick;
                }
            }
            return pick;
        }

        // ------------------------------------------------------------------------------------
        // Selection
        // ------------------------------------------------------------------------------------

        private void Click(Pick p)
        {
            var w = W;
            switch (p.Kind)
            {
                case PickKind.PlayerUnit:
                {
                    var u = w.FindUnitAny(p.Id);
                    if (u?.Squad == null) break;
                    if (Keys.Shift) Sel.Toggle(u.Squad.Id);
                    else Sel.SelectOnly(u.Squad.Id);
                    Sel.InspectUnitId = u.Id;
                    if (_lastClickUnit == u.Id && Time.unscaledTime - _lastClickTime < 0.35f) FocusSelection();
                    _lastClickUnit = u.Id;
                    _lastClickTime = Time.unscaledTime;
                    break;
                }
                case PickKind.Enemy:
                    Sel.InspectUnitId = p.Id;
                    break;
                case PickKind.Building:
                    _game.Hud.OpenBase(p.Building);
                    break;
                default:
                    if (!Keys.Shift)
                    {
                        Sel.Squads.Clear();
                        Sel.InspectUnitId = 0;
                    }
                    break;
            }
        }

        private void BoxSelect(Vector2 min, Vector2 max)
        {
            var w = W;
            var rig = _game.Rig;
            var found = new List<int>();
            foreach (var sq in w.Squads)
            {
                foreach (var m in sq.Members)
                {
                    if (!m.IsActive) continue;
                    var v = _game.View.UnitViewFor(m.Id);
                    if (v == null) continue;
                    var s = rig.Cam.WorldToScreenPoint(v.Position + Vector3.up);
                    if (s.z > 0f && s.x >= min.x && s.x <= max.x && s.y >= min.y && s.y <= max.y)
                    {
                        found.Add(sq.Id);
                        break;
                    }
                }
            }
            if (!Keys.Shift) Sel.Squads.Clear();
            foreach (int id in found) Sel.Add(id);
            if (found.Count > 0)
            {
                var sq = w.FindSquad(found[0]);
                if (sq != null && sq.Members.Count > 0) Sel.InspectUnitId = sq.Members[0].Id;
            }
        }

        public void SelectSquadIndex(int index, bool add)
        {
            var w = W;
            if (index < 0 || index >= w.Squads.Count) return;
            int id = w.Squads[index].Id;
            if (add) Sel.Toggle(id);
            else Sel.SelectOnly(id);
            if (_lastNumber == index && Time.unscaledTime - _lastNumberTime < 0.35f) FocusSelection();
            _lastNumber = index;
            _lastNumberTime = Time.unscaledTime;
        }

        public void FocusSelection()
        {
            var w = W;
            Vec2 sum = Vec2.Zero;
            int n = 0;
            foreach (int id in Sel.Squads)
            {
                var sq = w.FindSquad(id);
                if (sq == null) continue;
                sum += sq.Garrisoned ? w.BasePos : sq.Centroid;
                n++;
            }
            if (n == 0) return;
            _game.Rig.FocusOn(ToUnity.Ground(w, sum / n));
        }

        // ------------------------------------------------------------------------------------
        // Orders
        // ------------------------------------------------------------------------------------

        private void Order(Pick p)
        {
            var w = W;
            if (Sel.Squads.Count == 0 || !p.HasGround && p.Kind != PickKind.Enemy) return;
            PlayerOrder o;
            Color marker = new Color(0.4f, 1f, 0.5f, 0.9f);
            Vec2 at = p.Ground;
            switch (p.Kind)
            {
                case PickKind.Enemy:
                {
                    o = PlayerOrder.For(OrderType.Attack, Sel.Squads);
                    o.TargetId = p.Id;
                    var e = w.FindUnit(p.Id);
                    if (e != null) at = e.Pos;
                    marker = new Color(1f, 0.3f, 0.25f, 0.9f);
                    break;
                }
                case PickKind.Node:
                    o = PlayerOrder.For(OrderType.Gather, Sel.Squads);
                    o.TargetId = p.Id;
                    marker = new Color(1f, 0.8f, 0.3f, 0.9f);
                    break;
                case PickKind.Cache:
                    o = PlayerOrder.For(OrderType.Recover, Sel.Squads);
                    o.TargetId = p.Id;
                    marker = Palette.KinMark;
                    break;
                case PickKind.Building:
                    o = PlayerOrder.For(OrderType.ReturnToBase, Sel.Squads);
                    break;
                default:
                    if (w.InBase(p.Ground))
                    {
                        o = PlayerOrder.For(OrderType.ReturnToBase, Sel.Squads);
                        break;
                    }
                    o = PlayerOrder.For(OrderType.Move, Sel.Squads);
                    o.Pos = p.Ground;
                    break;
            }
            w.Issue(o);
            _game.View.Vfx.Ring(ToUnity.Ground(w, at, 0.1f), marker, 1.4f, 0.5f);
        }

        public void IssueCommand(CommandType c)
        {
            if (Sel.Squads.Count == 0) return;
            var o = PlayerOrder.For(OrderType.SetCommand, Sel.Squads);
            o.Command = c;
            W.Issue(o);
        }

        public void IssueSimple(OrderType t)
        {
            if (Sel.Squads.Count == 0) return;
            W.Issue(PlayerOrder.For(t, Sel.Squads));
        }

        private void Hotkeys()
        {
            var hud = _game.Hud;
            for (int i = 0; i < CommandKeys.Length; i++)
            {
                if (Keys.Down(CommandKeys[i])) IssueCommand(CommandForKey[i]);
            }
            if (Keys.Down(KeyCode.H)) IssueSimple(OrderType.ReturnToBase);
            if (Keys.Down(KeyCode.J)) IssueSimple(OrderType.Garrison);
            if (Keys.Down(KeyCode.K)) IssueSimple(OrderType.Stop);

            for (int i = 0; i < 6; i++)
            {
                if (Keys.Down(KeyCode.Alpha1 + i)) SelectSquadIndex(i, Keys.Shift);
            }
            if (Keys.Down(KeyCode.Tab) && W.Squads.Count > 0)
            {
                int cur = Sel.Squads.Count > 0 ? W.Squads.FindIndex(s => s.Id == Sel.Squads[0]) : -1;
                SelectSquadIndex((cur + 1) % W.Squads.Count, false);
            }
            if (Keys.Down(KeyCode.Space)) FocusSelection();

            if (Keys.Down(KeyCode.F1)) hud.ToggleHelp();
            if (Keys.Down(KeyCode.F2)) hud.ToggleBase();
            if (Keys.Down(KeyCode.F3)) hud.ToggleLog();
            if (Keys.Down(KeyCode.Escape)) hud.Escape();

            // Playtest helpers
            if (Keys.Down(KeyCode.F7))
            {
                // Playtest drill: selected squads jump to Doctrine tier 3 (opens the Shieldbearer's Titan Form).
                foreach (int id in _game.Selection.Squads)
                {
                    var sq = W.FindSquad(id);
                    if (sq != null) PlayerUnits.DebugDrill(W, sq, Balance.FourthAbilityTier);
                }
            }
            if (Keys.Down(KeyCode.F8)) _game.Rig.EdgeScroll = !_game.Rig.EdgeScroll;
            if (Keys.Down(KeyCode.F9)) _game.CycleSpeed();
            if (Keys.Down(KeyCode.F10) && W.Boss != null && W.Boss.Active) W.Boss.DebugSkipToPhase(W, W.Boss.Phase + 1);
            if (Keys.Down(KeyCode.F11)) W.RevealMap = !W.RevealMap;
        }
    }
}
