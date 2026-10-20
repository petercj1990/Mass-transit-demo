using System.Collections.Generic;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Mirrors the sim into GameObjects every frame: creates and destroys views as entities come and
    /// go, interpolates movement between ticks, and turns sim events into effects and floating text.
    /// </summary>
    public sealed class WorldView
    {
        private readonly KinforgeGame _game;
        private readonly World _w;
        private readonly Transform _root;
        private readonly Transform _unitRoot, _structRoot, _nodeRoot, _propRoot, _fxRoot;

        public readonly TerrainView Terrain;
        public readonly Vfx Vfx;

        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, StructureView> _structures = new Dictionary<int, StructureView>();
        private readonly Dictionary<int, GameObject> _nodes = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _caches = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, TelegraphView> _telegraphs = new Dictionary<int, TelegraphView>();
        private readonly Dictionary<int, LineRenderer> _beams = new Dictionary<int, LineRenderer>();
        private readonly Stack<GameObject> _projectilePool = new Stack<GameObject>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _drop = new List<int>();
        private readonly Material _beamMat;

        public WorldView(KinforgeGame game, World w, Transform root)
        {
            _game = game;
            _w = w;
            _root = root;
            _unitRoot = Prim.Node("Units", root);
            _structRoot = Prim.Node("Structures", root);
            _nodeRoot = Prim.Node("Resources", root);
            _propRoot = Prim.Node("Props", root);
            _fxRoot = Prim.Node("Effects", root);
            Terrain = new TerrainView(w, root);
            Vfx = new Vfx(_fxRoot);
            _beamMat = Mats.NewTransparent(Color.white);
            foreach (var p in w.Props) ModelFactory.BuildProp(p, w, _propRoot);
        }

        public UnitView UnitViewFor(int id) => _units.TryGetValue(id, out var v) ? v : null;

        public void Sync(float dt)
        {
            float alpha = _game.InterpAlpha;
            Terrain.UpdateFog(dt);
            SyncUnits(alpha, dt);
            SyncStructures(dt);
            SyncNodes();
            SyncCaches();
            SyncProjectiles(alpha);
            SyncTelegraphs();
            SyncBeams();
            foreach (var e in _game.FrameEvents) OnEvent(e);
            Vfx.Update(dt);
        }

        // ------------------------------------------------------------------------------------

        private void SyncUnits(float alpha, float dt)
        {
            _seen.Clear();
            var sel = _game.Selection;
            foreach (var u in _w.Units)
            {
                _seen.Add(u.Id);
                if (!_units.TryGetValue(u.Id, out var v))
                {
                    v = new UnitView(u, _unitRoot);
                    _units[u.Id] = v;
                }
                bool selected = u.Squad != null && sel.IsSelected(u.Squad.Id);
                Color c = u.Squad != null ? Palette.Squad(u.Squad.ColorIndex) : Palette.Enemy;
                v.Sync(_w, u, alpha, dt, selected || sel.InspectUnitId == u.Id, c);
            }
            Prune(_units, v => v.Destroy());
        }

        private void SyncStructures(float dt)
        {
            _seen.Clear();
            foreach (var s in _w.Structures)
            {
                if (s.Removed) continue;
                _seen.Add(s.Id);
                if (!_structures.TryGetValue(s.Id, out var v))
                {
                    v = new StructureView(s, _w, _structRoot);
                    _structures[s.Id] = v;
                }
                v.Sync(_w, s, dt);
            }
            Prune(_structures, v => v.Destroy());
        }

        private void SyncNodes()
        {
            _seen.Clear();
            foreach (var n in _w.Nodes)
            {
                if (n.Removed) continue;
                _seen.Add(n.Id);
                if (!_nodes.TryGetValue(n.Id, out var go))
                {
                    go = ModelFactory.BuildNode(n, _w, _nodeRoot);
                    _nodes[n.Id] = go;
                }
                float k = n.MaxAmount > 0f ? Mathf.Clamp01(n.Amount / n.MaxAmount) : 1f;
                go.transform.localScale = Vector3.one * (0.45f + 0.55f * k);
            }
            Prune(_nodes, go => Object.Destroy(go));
        }

        private void SyncCaches()
        {
            _seen.Clear();
            foreach (var c in _w.Caches)
            {
                if (c.Removed) continue;
                _seen.Add(c.Id);
                if (!_caches.ContainsKey(c.Id)) _caches[c.Id] = ModelFactory.BuildCache(c, _w, _nodeRoot);
            }
            Prune(_caches, go => Object.Destroy(go));
        }

        private void SyncProjectiles(float alpha)
        {
            _seen.Clear();
            foreach (var p in _w.Projectiles)
            {
                if (p.Removed) continue;
                _seen.Add(p.Id);
                bool visible = p.Faction == Faction.Player || _w.IsVisibleToPlayer(p.Pos);
                if (!_projectiles.TryGetValue(p.Id, out var go))
                {
                    go = _projectilePool.Count > 0 ? _projectilePool.Pop() : MakeProjectile();
                    go.SetActive(true);
                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = Mats.Glow(ProjectileColor(p), 3f);
                    float s = p.Visual == ProjectileVisual.Shell ? 0.45f : (p.Visual == ProjectileVisual.Rivet ? 0.14f : 0.18f);
                    go.transform.localScale = p.Arcing ? Vector3.one * s : new Vector3(s, s, s * 4f);
                    _projectiles[p.Id] = go;
                }
                if (go.activeSelf != visible) go.SetActive(visible);
                Vec2 pos = Vec2.Lerp(p.PrevPos, p.Pos, alpha);
                float y;
                if (p.Arcing)
                {
                    float k = p.ArcProgress;
                    float start = _w.HeightAt(p.Start) + p.StartHeight;
                    float end = _w.HeightAt(p.ArcTarget) + 0.5f;
                    y = Mathf.Lerp(start, end, k) + 4f * 7f * k * (1f - k);
                }
                else
                {
                    float k = p.MaxRange > 0f ? Mathf.Clamp01(p.Traveled / (p.MaxRange * 0.75f)) : 0f;
                    y = Mathf.Max(_w.HeightAt(pos) + 0.4f, _w.HeightAt(p.Start) + Mathf.Lerp(p.StartHeight, 1f, k));
                }
                go.transform.position = new Vector3(pos.X, y, pos.Z);
                if (!p.Arcing) go.transform.rotation = Quaternion.LookRotation(new Vector3(p.Dir.X, 0f, p.Dir.Z));
            }
            _drop.Clear();
            foreach (var kv in _projectiles)
            {
                if (!_seen.Contains(kv.Key)) _drop.Add(kv.Key);
            }
            foreach (int id in _drop)
            {
                var go = _projectiles[id];
                go.SetActive(false);
                _projectilePool.Push(go);
                _projectiles.Remove(id);
            }
        }

        private GameObject MakeProjectile()
        {
            var go = Prim.Ball(_fxRoot, Vector3.zero, Vector3.one * 0.2f, Mats.Glow(Color.white), default, false);
            go.name = "Projectile";
            return go;
        }

        private static Color ProjectileColor(Projectile p)
        {
            switch (p.Visual)
            {
                case ProjectileVisual.SearBolt: return new Color(1f, 0.55f, 0.15f);
                case ProjectileVisual.Rivet: return new Color(1f, 0.25f, 0.2f);
                case ProjectileVisual.Spark: return new Color(0.5f, 0.75f, 1f);
                case ProjectileVisual.Shell: return new Color(1f, 0.5f, 0.1f);
                case ProjectileVisual.TurretBolt: return new Color(1f, 0.85f, 0.35f);
                default: return new Color(1f, 0.78f, 0.35f);
            }
        }

        private void SyncTelegraphs()
        {
            _seen.Clear();
            foreach (var t in _w.Telegraphs)
            {
                if (t.Removed) continue;
                _seen.Add(t.Id);
                if (!_telegraphs.TryGetValue(t.Id, out var v))
                {
                    v = new TelegraphView(t, _fxRoot);
                    _telegraphs[t.Id] = v;
                }
                bool visible = _w.IsVisibleToPlayer(t.Pos);
                v.Sync(_w, t, visible);
            }
            Prune(_telegraphs, v => v.Destroy());
        }

        /// <summary>Mend Beam and Weld Beam are drawn for as long as the channel lasts.</summary>
        private void SyncBeams()
        {
            _seen.Clear();
            foreach (var u in _w.Units)
            {
                if (!u.IsActive || u.Action != UnitAction.Channel) continue;
                if (u.ActionAbility != AbilityId.MendBeam && u.ActionAbility != AbilityId.WeldBeam) continue;
                var target = _w.FindUnit(u.ActionTargetId);
                if (target == null) continue;
                if (!u.IsPlayer && !_w.IsVisibleToPlayer(u.Pos)) continue;
                _seen.Add(u.Id);
                if (!_beams.TryGetValue(u.Id, out var lr))
                {
                    var go = new GameObject("Beam");
                    go.transform.SetParent(_fxRoot, false);
                    lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = _beamMat;
                    lr.positionCount = 2;
                    lr.useWorldSpace = true;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _beams[u.Id] = lr;
                }
                var uv = UnitViewFor(u.Id);
                var tv = UnitViewFor(target.Id);
                if (uv == null || tv == null) continue;
                Color c = u.ActionAbility == AbilityId.MendBeam ? Palette.Heal : new Color(1f, 0.85f, 0.3f);
                c.a = 0.55f + Mathf.Sin(Time.time * 20f) * 0.2f;
                lr.startColor = c;
                lr.endColor = c;
                lr.startWidth = 0.12f;
                lr.endWidth = 0.22f;
                lr.SetPosition(0, uv.Position + Vector3.up * (u.Def.Height * 0.75f));
                lr.SetPosition(1, tv.Position + Vector3.up * (target.Def.Height * 0.55f));
                if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
            }
            foreach (var kv in _beams)
            {
                if (!_seen.Contains(kv.Key) && kv.Value.gameObject.activeSelf) kv.Value.gameObject.SetActive(false);
            }
        }

        private void Prune<T>(Dictionary<int, T> map, System.Action<T> destroy)
        {
            _drop.Clear();
            foreach (var kv in map)
            {
                if (!_seen.Contains(kv.Key)) _drop.Add(kv.Key);
            }
            foreach (int id in _drop)
            {
                destroy(map[id]);
                map.Remove(id);
            }
        }

        // ------------------------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------------------------

        private Vector3 At(SimEvent e, float lift = 1f) => ToUnity.Ground(_w, e.Pos, lift);

        private Vector3 UnitPos(int id, float liftFrac = 0.6f)
        {
            var u = _w.FindUnitAny(id);
            var v = UnitViewFor(id);
            if (u == null || v == null) return Vector3.zero;
            return v.Position + Vector3.up * (u.Def.Height * liftFrac);
        }

        private bool Seen(Vec2 p) => _w.IsVisibleToPlayer(p);

        private void OnEvent(SimEvent e)
        {
            var hud = _game.Hud;
            switch (e.Type)
            {
                case SimEventType.Swing:
                    UnitViewFor(e.A)?.OnSwing();
                    break;

                case SimEventType.Hit:
                {
                    if (!Seen(e.Pos)) break;
                    var target = _w.FindUnitAny(e.B);
                    Vector3 p = target != null ? UnitPos(e.B) : At(e, 1.2f);
                    if (p == Vector3.zero) p = At(e, 1.2f);
                    Color c = Palette.Damage(e.Damage);
                    Vfx.Burst(p, new Color(c.r, c.g, c.b, 0.7f), 0.5f + Mathf.Min(1.5f, e.Value / 60f), 0.22f);
                    if (target != null)
                    {
                        UnitViewFor(e.B)?.OnHurt();
                        if (e.Value >= 4f)
                        {
                            Color tc = target.IsPlayer ? new Color(1f, 0.45f, 0.4f) : c;
                            hud.Float(p + Vector3.up * 0.6f, Mathf.RoundToInt(e.Value).ToString(), tc, target.Kind == UnitKind.Rustmaw ? 1.1f : 0.85f);
                        }
                    }
                    break;
                }

                case SimEventType.Heal:
                    if (e.Value >= 3f)
                    {
                        var p = UnitPos(e.B);
                        if (p != Vector3.zero)
                        {
                            hud.Float(p + Vector3.up * 0.8f, "+" + Mathf.RoundToInt(e.Value), Palette.Heal, 0.85f);
                            Vfx.Burst(p, new Color(0.3f, 1f, 0.5f, 0.45f), 0.9f, 0.4f);
                        }
                    }
                    break;

                case SimEventType.Miss:
                    if (Seen(e.Pos)) Vfx.Burst(At(e, 1f), new Color(0.8f, 0.75f, 0.6f, 0.35f), 0.35f, 0.18f);
                    break;

                case SimEventType.AbilityCast:
                {
                    var caster = _w.FindUnitAny(e.A);
                    if (caster == null) break;
                    if (!caster.IsPlayer && !Seen(caster.Pos)) break;
                    var p = UnitPos(e.A, 0.9f);
                    if (p != Vector3.zero) Vfx.Burst(p, new Color(1f, 0.75f, 0.3f, 0.35f), 0.7f, 0.25f);
                    if (caster.IsPlayer && e.Ability != AbilityId.MendBeam)
                        hud.Float(p + Vector3.up * 0.9f, AbilityDefs.Get(e.Ability).Name, new Color(1f, 0.85f, 0.55f), 0.7f);
                    break;
                }

                case SimEventType.AbilityImpact:
                {
                    if (!Seen(e.Pos)) break;
                    var p = At(e, 0.3f);
                    switch (e.Ability)
                    {
                        case AbilityId.TauntRoar:
                            Vfx.Ring(p, new Color(1f, 0.6f, 0.15f, 0.8f), e.Value, 0.6f);
                            break;
                        case AbilityId.ShockwaveSlam:
                        case AbilityId.TailSlam:
                        case AbilityId.RustmawSwipe:
                            Vfx.Ring(p, new Color(0.9f, 0.8f, 0.6f, 0.7f), Mathf.Max(2f, e.Value), 0.35f);
                            Vfx.Debris(p, Palette.Earth, 5, 4f);
                            break;
                        case AbilityId.Cleave:
                            Vfx.Ring(p, new Color(1f, 1f, 1f, 0.6f), 2.5f, 0.25f);
                            break;
                        case AbilityId.RaiseRampart:
                            Vfx.Debris(p, Palette.Earth, 8, 5f);
                            break;
                        case AbilityId.SnareCage:
                            Vfx.Ring(p, new Color(1f, 0.6f, 0.15f, 0.9f), 1.2f, 0.4f);
                            break;
                        case AbilityId.GutHook:
                        {
                            var from = UnitPos(e.A, 0.6f);
                            if (from != Vector3.zero) Vfx.Line(from, p + Vector3.up, new Color(0.8f, 0.8f, 0.85f, 0.9f), 0.06f, 0.25f);
                            break;
                        }
                        case AbilityId.Pounce:
                            Vfx.Debris(p, Palette.Earth, 4, 3f);
                            break;
                    }
                    break;
                }

                case SimEventType.Explosion:
                    if (!Seen(e.Pos)) break;
                    Vfx.Burst(At(e, 0.6f), new Color(1f, 0.5f, 0.1f, 0.8f), e.Value * 2f, 0.45f);
                    Vfx.Debris(At(e, 0.2f), Palette.Earth, 6, 6f);
                    break;

                case SimEventType.Meteor:
                    Vfx.Burst(At(e, 0.5f), new Color(1f, 0.55f, 0.15f, 0.9f), e.Value * 2.4f, 0.7f);
                    Vfx.Ring(At(e, 0.1f), new Color(1f, 0.6f, 0.3f, 0.8f), e.Value * 1.6f, 0.8f);
                    Vfx.Debris(At(e, 0.3f), Palette.Ferrite, 18, 9f);
                    if (Seen(e.Pos)) _game.Rig.Shake(0.6f);
                    break;

                case SimEventType.ArcChain:
                {
                    var a = UnitPos(e.A);
                    var b = UnitPos(e.B);
                    if (a != Vector3.zero && b != Vector3.zero) Vfx.Line(a, b, new Color(0.6f, 0.75f, 1f, 1f), 0.08f, 0.2f, true);
                    break;
                }

                case SimEventType.WeakPoint:
                    hud.Float(At(e, 3.5f), "WEAK POINT!", new Color(1f, 0.85f, 0.2f), 1.2f);
                    Vfx.Burst(At(e, 2.6f), new Color(1f, 0.8f, 0.2f, 0.9f), 1.6f, 0.3f);
                    break;

                case SimEventType.Interrupt:
                {
                    var u = _w.FindUnitAny(e.B);
                    if (u != null && (u.IsPlayer || Seen(u.Pos))) hud.Float(UnitPos(e.B, 1.1f), "Interrupted!", new Color(0.7f, 0.85f, 1f), 0.9f);
                    break;
                }

                case SimEventType.Downed:
                    hud.Float(UnitPos(e.A, 0.4f) + Vector3.up, "DOWNED", new Color(1f, 0.35f, 0.3f), 1.1f);
                    break;
                case SimEventType.Killed:
                    hud.Float(At(e, 2.2f), "KILLED", new Color(1f, 0.2f, 0.2f), 1.25f);
                    Vfx.Burst(At(e, 1f), new Color(1f, 0.6f, 0.2f, 0.8f), 2f, 0.6f);
                    break;
                case SimEventType.Revived:
                {
                    var u = _w.FindUnitAny(e.A);
                    if (u == null) break;
                    hud.Float(UnitPos(e.A, 1.1f), e.Value > 0.5f ? "Recovered" : "Back up (Injured)", Palette.Heal, 0.9f);
                    break;
                }
                case SimEventType.Regrown:
                    Vfx.Ring(At(e, 0.2f), new Color(1f, 0.65f, 0.2f, 0.9f), 3f, 0.9f);
                    Vfx.Burst(At(e, 1f), new Color(1f, 0.7f, 0.3f, 0.8f), 2.5f, 0.7f);
                    break;
                case SimEventType.CorePickup:
                    hud.Float(UnitPos(e.A, 1.2f), "Kin-core recovered", Palette.KinMark, 0.8f);
                    break;
                case SimEventType.CacheDropped:
                    Vfx.Ring(At(e, 0.2f), new Color(1f, 0.65f, 0.2f, 0.9f), 4f, 1f);
                    break;
                case SimEventType.TierUp:
                {
                    var v = UnitViewFor(e.A);
                    v?.OnTierUp();
                    var cmd = CommandInfo.Get((CommandType)e.B);
                    hud.Float(UnitPos(e.A, 1.25f), $"{cmd.Name} tier {e.Value:0}!", Palette.KinMark, 1f);
                    break;
                }
                case SimEventType.Deposit:
                    if (e.Value >= 1f) hud.Float(At(e, 2.4f), $"+{e.Value:0} stored", new Color(0.85f, 0.85f, 0.7f), 0.75f);
                    break;
                case SimEventType.StructureDestroyed:
                    if (Seen(e.Pos)) Vfx.Debris(At(e, 0.8f), Palette.Earth, 10, 6f);
                    break;
                case SimEventType.Knockback:
                    if (Seen(e.Pos)) Vfx.Burst(At(e, 0.2f), new Color(0.7f, 0.65f, 0.55f, 0.4f), 1.2f, 0.3f);
                    break;
                case SimEventType.BossPhase:
                    _game.Rig.Shake(0.5f);
                    Vfx.Ring(At(e, 0.3f), new Color(1f, 0.4f, 0.1f, 0.9f), 14f, 1.2f);
                    break;
                case SimEventType.BossCharge:
                    _game.Rig.Shake(0.25f);
                    break;
                case SimEventType.BossCrash:
                    _game.Rig.Shake(0.9f);
                    hud.Float(At(e, 4f), "CRASH! STAGGERED", new Color(1f, 0.8f, 0.2f), 1.4f);
                    Vfx.Debris(At(e, 1f), Palette.Earth, 20, 9f);
                    Vfx.Ring(At(e, 0.2f), new Color(1f, 0.85f, 0.4f, 0.9f), 6f, 0.6f);
                    break;
                case SimEventType.BossBurrow:
                    Vfx.Debris(At(e, 0.5f), Palette.Earth, 24, 8f);
                    break;
                case SimEventType.BossSurface:
                    _game.Rig.Shake(0.7f);
                    Vfx.Debris(At(e, 0.5f), Palette.Earth, 26, 10f);
                    Vfx.Ring(At(e, 0.2f), new Color(0.9f, 0.6f, 0.3f, 0.9f), e.Value * 1.4f, 0.6f);
                    break;
                case SimEventType.BossBlast:
                    _game.Rig.Shake(1f);
                    Vfx.Ring(At(e, 0.4f), new Color(1f, 0.45f, 0.1f, 0.95f), e.Value * 1.1f, 0.8f);
                    Vfx.Burst(At(e, 2f), new Color(1f, 0.5f, 0.1f, 0.6f), e.Value * 1.2f, 0.6f);
                    break;
                case SimEventType.Victory:
                    _game.Rig.Shake(1f);
                    Vfx.Ring(At(e, 0.4f), new Color(1f, 0.75f, 0.25f, 1f), 25f, 2f);
                    Vfx.Debris(At(e, 2f), Palette.Ferrite, 40, 12f);
                    break;
            }
        }
    }
}
