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
    /// Direct control of the kin-band, like leading a WoW party: WASD / left stick walks the squad's
    /// banner relative to the camera, the mouse (drag) or right stick orbits the camera, number keys or
    /// the bumpers set the strategy, and right-click / A give context orders (attack, gather, recover
    /// kin-cores, go home). The Vhorn still choose their own targets, positions and abilities inside the
    /// strategy; the player never casts for them.
    /// </summary>
    public sealed class CommandInput
    {
        private readonly KinforgeGame _game;
        private readonly Vector2[] _pressPos = new Vector2[2];
        private readonly bool[] _pressed = new bool[2];
        private readonly bool[] _dragging = new bool[2];
        private Vector2 _lastMouse;
        private bool _wasSteering;
        private int _dpadX, _dpadY;

        public Pick Hover { get; private set; }
        public bool Boxing => false;
        public Rect BoxRect => default;
        public bool UsingPad { get; private set; }

        /// <summary>Strategies in hotbar order: number keys 1-0 and the letter hotkeys pick the same slots.</summary>
        public static readonly CommandType[] CommandForKey =
        {
            CommandType.Charge, CommandType.DefensiveFormation, CommandType.HoldPosition, CommandType.Kite, CommandType.FocusFire,
            CommandType.SpreadOut, CommandType.Stack, CommandType.FallBack, CommandType.Rest, CommandType.Fortify,
        };

        public static readonly KeyCode[] CommandKeys =
        {
            // Stack has no letter any more: M opens the map.
            KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N, KeyCode.None, KeyCode.G, KeyCode.R, KeyCode.T,
        };

        public static readonly KeyCode[] NumberKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
        };

        public CommandInput(KinforgeGame game)
        {
            _game = game;
        }

        private World W => _game.World;
        private Selection Sel => _game.Selection;
        public Squad Squad => W != null && W.Squads.Count > 0 ? W.Squads[0] : null;

        public void Update(float dt)
        {
            if (W == null || Keys.Broken) return;
            var sq = Squad;
            if (sq != null && (Sel.Squads.Count != 1 || Sel.Squads[0] != sq.Id)) Sel.SelectOnly(sq.Id);

            Vector2 mouse = Keys.Mouse;
            bool overUi = _game.Hud.IsPointerOverUi(mouse);
            Hover = overUi ? default : PickAt(mouse);
            Hotkeys();
            Gamepad(dt);

            if (_game.Hud.BlocksWorld)
            {
                ReleaseSteer();
                return;
            }
            Steering();
            CameraControl(dt, mouse, overUi);
            MouseButtons(mouse, overUi);
            _lastMouse = mouse;

            // The camera rides with the banner (the base while garrisoned).
            if (sq != null) _game.Rig.Follow(ToUnity.Ground(W, sq.Garrisoned ? W.BasePos : sq.Anchor));
        }

        // ------------------------------------------------------------------------------------
        // Movement and camera
        // ------------------------------------------------------------------------------------

        private void Steering()
        {
            var sq = Squad;
            if (sq == null) return;
            Vector2 v = Vector2.zero;
            if (Keys.Held(KeyCode.W) || Keys.Held(KeyCode.UpArrow)) v.y += 1f;
            if (Keys.Held(KeyCode.S) || Keys.Held(KeyCode.DownArrow)) v.y -= 1f;
            if (Keys.Held(KeyCode.D) || Keys.Held(KeyCode.RightArrow)) v.x += 1f;
            if (Keys.Held(KeyCode.A) || Keys.Held(KeyCode.LeftArrow)) v.x -= 1f;
            var stick = Keys.Stick("KF Move X", "KF Move Y");
            if (stick != Vector2.zero)
            {
                v = stick;
                UsingPad = true;
            }
            else if (v != Vector2.zero) UsingPad = false;
            v = Vector2.ClampMagnitude(v, 1f);
            if (v.sqrMagnitude < 0.02f)
            {
                ReleaseSteer();
                return;
            }
            // Camera-relative: forward is where the camera looks.
            var rig = _game.Rig;
            var world = rig.Right * v.x + rig.Forward * v.y;
            var o = PlayerOrder.For(OrderType.Steer, Sel.Squads);
            o.Pos = new Vec2(world.x, world.z);
            W.Issue(o);
            _wasSteering = true;
        }

        private void ReleaseSteer()
        {
            if (!_wasSteering) return;
            _wasSteering = false;
            var o = PlayerOrder.For(OrderType.Steer, Sel.Squads);
            o.Pos = Vec2.Zero;
            W.Issue(o);
        }

        private void CameraControl(float dt, Vector2 mouse, bool overUi)
        {
            var rig = _game.Rig;
            float rotate = 0f;
            if (Keys.Held(KeyCode.Q)) rotate -= 1f;
            if (Keys.Held(KeyCode.E)) rotate += 1f;
            rig.Orbit(rotate * 110f * dt, 0f);
            if (!overUi) rig.Zoom(Keys.Scroll);

            var look = Keys.Stick("KF Look X", "KF Look Y");
            if (look != Vector2.zero)
            {
                rig.Orbit(look.x * 160f * dt, -look.y * 90f * dt);
                UsingPad = true;
            }
            float zoom = Keys.Axis("KF Trigger R") - Keys.Axis("KF Trigger L");
            if (Mathf.Abs(zoom) > 0.1f) rig.Zoom(zoom * dt * 10f);

            if (Keys.Down(KeyCode.Space) || Keys.PadDown(Keys.PadRStick)) CameraBehindSquad();
        }

        /// <summary>Swings the camera behind the squad's heading.</summary>
        public void CameraBehindSquad()
        {
            var sq = Squad;
            if (sq == null) return;
            var f = sq.Forward;
            _game.Rig.LookAlong(Mathf.Atan2(f.X, f.Z) * Mathf.Rad2Deg);
        }

        /// <summary>Kept for old callers: the camera always follows the kin-band now.</summary>
        public void FocusSelection() => CameraBehindSquad();

        /// <summary>
        /// WoW-style mouse: drag with either button to orbit the camera; a left click inspects (or opens
        /// a building), a right click gives a context order.
        /// </summary>
        private void MouseButtons(Vector2 mouse, bool overUi)
        {
            for (int b = 0; b < 2; b++)
            {
                if (Keys.MouseDown(b) && !overUi)
                {
                    _pressed[b] = true;
                    _dragging[b] = false;
                    _pressPos[b] = mouse;
                }
                if (!_pressed[b]) continue;
                if (Keys.MouseHeld(b))
                {
                    if (!_dragging[b] && (mouse - _pressPos[b]).magnitude > 6f) _dragging[b] = true;
                    if (_dragging[b])
                    {
                        var d = mouse - _lastMouse;
                        _game.Rig.Orbit(d.x * 0.3f, -d.y * 0.2f);
                    }
                }
                else
                {
                    _pressed[b] = false;
                    if (_dragging[b]) continue;
                    var pick = PickAt(mouse);
                    if (b == 0) Click(pick);
                    else Order(pick);
                }
            }
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
        // Clicks and orders
        // ------------------------------------------------------------------------------------

        private void Click(Pick p)
        {
            switch (p.Kind)
            {
                case PickKind.PlayerUnit:
                case PickKind.Enemy:
                    Sel.InspectUnitId = p.Id;
                    break;
                case PickKind.Building:
                    _game.Hud.OpenBase(p.Building);
                    break;
                default:
                    Sel.InspectUnitId = 0;
                    break;
            }
        }

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

        private void CycleStrategy(int step)
        {
            var sq = Squad;
            if (sq == null) return;
            int i = System.Array.IndexOf(CommandForKey, sq.Stance);
            if (i < 0) i = 1;
            int n = CommandForKey.Length;
            IssueCommand(CommandForKey[((i + step) % n + n) % n]);
        }

        private void CycleInspect(int step)
        {
            var sq = Squad;
            if (sq == null || sq.Members.Count == 0) return;
            int i = sq.Members.FindIndex(m => m.Id == Sel.InspectUnitId);
            int n = sq.Members.Count;
            i = i < 0 ? (step > 0 ? 0 : n - 1) : ((i + step) % n + n) % n;
            Sel.InspectUnitId = sq.Members[i].Id;
        }

        /// <summary>Gamepad A: gather, recover kin-cores or garrison, whichever is right by the banner.</summary>
        private void Interact()
        {
            var sq = Squad;
            if (sq == null) return;
            var w = W;
            Vec2 at = sq.Anchor;
            foreach (var c in w.Caches)
            {
                if (c.Removed || Vec2.Distance(c.Pos, at) > 10f) continue;
                var o = PlayerOrder.For(OrderType.Recover, Sel.Squads);
                o.TargetId = c.Id;
                w.Issue(o);
                return;
            }
            ResourceNode best = null;
            float bestD = 12f;
            foreach (var n in w.Nodes)
            {
                if (n.Removed) continue;
                float d = Vec2.Distance(n.Pos, at) - n.Radius;
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }
            if (best != null)
            {
                var o = PlayerOrder.For(OrderType.Gather, Sel.Squads);
                o.TargetId = best.Id;
                w.Issue(o);
                return;
            }
            if (w.InBase(at))
            {
                IssueSimple(OrderType.Garrison);
                return;
            }
            w.Alert("Nothing to gather or recover here. Walk the kin-band up to a resource or a kin-core cache.", 0);
        }

        /// <summary>Gamepad X: focus the visible enemy closest to the middle of the screen.</summary>
        private void FocusAhead()
        {
            var w = W;
            var rig = _game.Rig;
            var mid = new Vector2(Screen.width * 0.5f, Screen.height * 0.55f);
            Unit best = null;
            float bestD = float.MaxValue;
            foreach (var u in w.Units)
            {
                if (u.Faction != Faction.Enemy || !u.IsTargetable || !w.IsVisibleToPlayer(u.Pos)) continue;
                var v = _game.View.UnitViewFor(u.Id);
                if (v == null || !v.Visible || !rig.ToScreen(v.Position, out var sp)) continue;
                float d = (sp - mid).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = u;
                }
            }
            if (best == null) return;
            var o = PlayerOrder.For(OrderType.Attack, Sel.Squads);
            o.TargetId = best.Id;
            w.Issue(o);
            Sel.InspectUnitId = best.Id;
            _game.View.Vfx.Ring(ToUnity.Ground(w, best.Pos, 0.1f), new Color(1f, 0.3f, 0.25f, 0.9f), 1.4f, 0.5f);
        }

        private void Hotkeys()
        {
            var hud = _game.Hud;
            for (int i = 0; i < CommandKeys.Length; i++)
            {
                if ((CommandKeys[i] != KeyCode.None && Keys.Down(CommandKeys[i])) || Keys.Down(NumberKeys[i])) IssueCommand(CommandForKey[i]);
            }
            if (Keys.Down(KeyCode.H)) IssueSimple(OrderType.ReturnToBase);
            if (Keys.Down(KeyCode.J)) IssueSimple(OrderType.Garrison);
            if (Keys.Down(KeyCode.K)) IssueSimple(OrderType.Stop);
            if (Keys.Down(KeyCode.Tab)) CycleInspect(Keys.Shift ? -1 : 1);
            if (Keys.Down(KeyCode.M)) hud.ToggleMap();

            if (Keys.Down(KeyCode.F1)) hud.ToggleHelp();
            if (Keys.Down(KeyCode.F2)) hud.ToggleBase();
            if (Keys.Down(KeyCode.F3)) hud.ToggleLog();
            if (Keys.Down(KeyCode.Escape)) hud.Escape();

            // Playtest helpers
            if (Keys.Down(KeyCode.F7) && Sel.Squads.Count > 0)
            {
                // Playtest drill: the kin-band jumps to Doctrine tier 3 (opens the Shieldbearer's Titan Form).
                // Sent as an order so its unlock events reach the view on the next tick.
                var drill = new PlayerOrder { Type = OrderType.DebugDrill, Arg = Balance.FourthAbilityTier };
                drill.Squads.AddRange(Sel.Squads);
                W.Issue(drill);
            }
            if (Keys.Down(KeyCode.F9)) _game.CycleSpeed();
            if (Keys.Down(KeyCode.F10) && W.Boss != null && W.Boss.Active) W.Boss.DebugSkipToPhase(W, W.Boss.Phase + 1);
            if (Keys.Down(KeyCode.F11)) W.RevealMap = !W.RevealMap;
        }

        private void Gamepad(float dt)
        {
            var hud = _game.Hud;
            bool any = false;
            if (Keys.PadDown(Keys.PadLB)) { CycleStrategy(-1); any = true; }
            if (Keys.PadDown(Keys.PadRB)) { CycleStrategy(1); any = true; }
            if (Keys.PadDown(Keys.PadA)) { Interact(); any = true; }
            if (Keys.PadDown(Keys.PadX)) { FocusAhead(); any = true; }
            if (Keys.PadDown(Keys.PadY)) { IssueSimple(OrderType.ReturnToBase); any = true; }
            if (Keys.PadDown(Keys.PadB)) { IssueCommand(CommandType.FallBack); any = true; }
            if (Keys.PadDown(Keys.PadLStick)) { IssueCommand(CommandType.Rest); any = true; }
            if (Keys.PadDown(Keys.PadBack)) { hud.ToggleBase(); any = true; }
            if (Keys.PadDown(Keys.PadStart)) { hud.ToggleHelp(); any = true; }

            // D-pad (an axis pair in the classic Input Manager): left/right picks a Vhorn to inspect, up/down zooms.
            int dx = Mathf.RoundToInt(Keys.Axis("KF DPad X"));
            int dy = Mathf.RoundToInt(Keys.Axis("KF DPad Y"));
            if (dx != 0 && dx != _dpadX) { CycleInspect(dx); any = true; }
            if (dy != 0) _game.Rig.Zoom(dy * dt * 6f);
            _dpadX = dx;
            _dpadY = dy;
            if (any) UsingPad = true;
        }
    }
}
