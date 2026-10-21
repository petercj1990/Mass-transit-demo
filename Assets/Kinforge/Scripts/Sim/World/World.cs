using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    public sealed class Alert
    {
        public float Time;
        public string Text;
        public bool HasPos;
        public Vec2 Pos;
        public int Severity; // 0 info, 1 warning, 2 danger, 3 triumph
    }

    public sealed class Crater
    {
        public Vec2 Center;
        public float Radius;
        public float Depth;
        public float Rim;
        public bool FlatFloor;
    }

    /// <summary>
    /// The whole game state and the fixed-step tick. Engine-agnostic: Unity renders it, tests drive it.
    /// </summary>
    public sealed class World
    {
        public readonly int Seed;
        public readonly SimRng Rng;
        public int Tick;
        public float Time;

        public readonly Heightfield Height;
        public readonly NavGrid Nav;
        public readonly Visibility Fog;
        public Noise GroundNoise;

        public readonly List<Unit> Units = new List<Unit>();
        public readonly List<Structure> Structures = new List<Structure>();
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<Telegraph> Telegraphs = new List<Telegraph>();
        public readonly List<ResourceNode> Nodes = new List<ResourceNode>();
        public readonly List<CoreCache> Caches = new List<CoreCache>();
        public readonly List<StaticProp> Props = new List<StaticProp>();
        public readonly List<Crater> Craters = new List<Crater>();
        public readonly List<Vec2> WreckSites = new List<Vec2>();
        public const float WreckSiteRadius = 9f;
        public readonly List<Squad> Squads = new List<Squad>();
        public readonly List<EnemyGroup> Groups = new List<EnemyGroup>();
        public readonly List<SimEvent> Events = new List<SimEvent>();
        public readonly List<Alert> Alerts = new List<Alert>();
        public readonly List<BattleReport> Reports = new List<BattleReport>();
        public readonly List<PlayerOrder> PendingOrders = new List<PlayerOrder>();

        public readonly Stockpile Stock = new Stockpile();
        public readonly Bestiary Bestiary = new Bestiary();
        public PlayerBase Base;
        public RustmawBoss Boss;
        public Director Director;
        public Vec2 BasePos;
        public Vec2 ArenaCenter;
        public float ArenaRadius;
        public Vec2 ArenaGate;

        public bool Victory;
        public float VictoryAt = -1f;
        public int InspectedUnitId;
        public bool RevealMap;
        /// <summary>Switch for A/B balance runs: false and no Shieldbearer ever grows (per world, so tests can run in parallel).</summary>
        public bool TitanEnabled = true;
        /// <summary>Set the first time any Titan rises, for the one-time explainer alert.</summary>
        public bool TitanSeen;

        // Per-world scratch space, so several worlds can tick on different threads (tests, a future host).
        internal readonly BrainContext BrainCtx = new BrainContext();
        internal readonly List<Unit> ChainScratch = new List<Unit>(16);
        internal readonly List<Unit> AbilityScratch = new List<Unit>(16);
        internal readonly List<Unit> FieldScratch = new List<Unit>(16);
        internal int PathsThisTick;

        private readonly Dictionary<int, Entity> _entities = new Dictionary<int, Entity>();
        private int _nextId = 1;
        private readonly List<Unit> _scratchUnits = new List<Unit>(64);

        public World(int seed)
        {
            Seed = seed;
            Rng = new SimRng((ulong)seed * 7919UL + 17UL);
            Height = new Heightfield(Balance.MapSize);
            Nav = new NavGrid(Balance.MapSize);
            Fog = new Visibility(Balance.MapSize);
        }

        /// <summary>Builds a fresh Craters world with the starting squad, base, camps and Rustmaw's hold.</summary>
        public static World Create(int seed)
        {
            var w = new World(seed);
            WorldGen.Generate(w);
            return w;
        }

        public int NextId() => _nextId++;

        // ---------------------------------------------------------------------------------------
        // Registry
        // ---------------------------------------------------------------------------------------

        public void Register(Entity e)
        {
            if (e.Id == 0) e.Id = NextId();
            e.PrevPos = e.Pos;
            _entities[e.Id] = e;
            if (e is Unit u) Units.Add(u);
            else if (e is Structure s) Structures.Add(s);
        }

        public Entity FindEntity(int id) => id != 0 && _entities.TryGetValue(id, out var e) && !e.Removed ? e : null;
        public Unit FindUnit(int id) => FindEntity(id) as Unit;
        public Structure FindStructure(int id) => FindEntity(id) as Structure;

        /// <summary>Finds a unit even if it is dead or garrisoned (for rosters, cores and reports).</summary>
        public Unit FindUnitAny(int id) => id != 0 && _entities.TryGetValue(id, out var e) ? e as Unit : null;

        public Squad FindSquad(int id)
        {
            foreach (var s in Squads)
            {
                if (s.Id == id) return s;
            }
            return null;
        }

        public ResourceNode FindNode(int id)
        {
            foreach (var n in Nodes)
            {
                if (n.Id == id && !n.Removed) return n;
            }
            return null;
        }

        public CoreCache FindCache(int id)
        {
            foreach (var c in Caches)
            {
                if (c.Id == id && !c.Removed) return c;
            }
            return null;
        }

        // ---------------------------------------------------------------------------------------
        // Queries
        // ---------------------------------------------------------------------------------------

        public float HeightAt(Vec2 p) => Height.Sample(p);

        /// <summary>Fills results with active units of a faction within radius (edge-inclusive if addRadius).</summary>
        public void QueryUnits(Vec2 p, float r, Faction faction, List<Unit> results, bool addRadius = true)
        {
            results.Clear();
            foreach (var u in Units)
            {
                if (u.Faction != faction || !u.IsTargetable) continue;
                float rr = addRadius ? r + u.Radius : r;
                if (Vec2.DistanceSq(u.Pos, p) <= rr * rr) results.Add(u);
            }
        }

        public int CountUnits(Vec2 p, float r, Faction faction)
        {
            int n = 0;
            foreach (var u in Units)
            {
                if (u.Faction != faction || !u.IsTargetable) continue;
                float rr = r + u.Radius;
                if (Vec2.DistanceSq(u.Pos, p) <= rr * rr) n++;
            }
            return n;
        }

        public Unit NearestUnit(Vec2 p, Faction faction, float maxDist, Func<Unit, bool> filter = null)
        {
            Unit best = null;
            float bestD = maxDist * maxDist;
            foreach (var u in Units)
            {
                if (u.Faction != faction || !u.IsTargetable) continue;
                if (filter != null && !filter(u)) continue;
                float d = Vec2.DistanceSq(u.Pos, p);
                if (d < bestD)
                {
                    bestD = d;
                    best = u;
                }
            }
            return best;
        }

        public List<Unit> Scratch()
        {
            _scratchUnits.Clear();
            return _scratchUnits;
        }

        public bool InBase(Vec2 p) => Vec2.DistanceSq(p, BasePos) <= Balance.BaseRadius * Balance.BaseRadius;

        public bool InArena(Vec2 p, float pad = 0f) => Vec2.DistanceSq(p, ArenaCenter) <= (ArenaRadius + pad) * (ArenaRadius + pad);

        public bool IsVisibleToPlayer(Vec2 p) => RevealMap || Fog.IsVisible(p);

        public int CountStructures(Squad owner, StructureKind kind)
        {
            int n = 0;
            foreach (var s in Structures)
            {
                if (!s.Removed && s.Kind == kind && s.OwnerSquad == owner) n++;
            }
            return n;
        }

        // ---------------------------------------------------------------------------------------
        // Events and alerts
        // ---------------------------------------------------------------------------------------

        public void Emit(SimEventType type, int a = 0, int b = 0, Vec2 p = default, float v = 0f, DamageType dt = DamageType.Kinetic, AbilityId ability = AbilityId.None)
        {
            Events.Add(new SimEvent { Type = type, A = a, B = b, Pos = p, Value = v, Damage = dt, Ability = ability, Time = Time });
        }

        public void Alert(string text, int severity = 0) => Alerts.Add(new Alert { Time = Time, Text = text, Severity = severity });

        public void Alert(string text, Vec2 pos, int severity = 1) =>
            Alerts.Add(new Alert { Time = Time, Text = text, HasPos = true, Pos = pos, Severity = severity });

        public Telegraph AddTelegraph(Telegraph t)
        {
            t.Id = NextId();
            t.CreatedAt = Time;
            if (t.Remaining <= 0f) t.Remaining = t.Duration;
            Telegraphs.Add(t);
            return t;
        }

        public void Issue(PlayerOrder order) => PendingOrders.Add(order);

        // ---------------------------------------------------------------------------------------
        // Tick
        // ---------------------------------------------------------------------------------------

        public void Step()
        {
            Events.Clear();
            Tick++;
            Time += Balance.Dt;

            foreach (var u in Units) u.PrevPos = u.Pos;
            foreach (var p in Projectiles) p.PrevPos = p.Pos;

            Orders.Process(this);
            if (Tick % 5 == 0) UpdateVisibility();

            SquadSystem.Update(this);
            EnemyBrain.UpdateGroups(this);
            Boss?.Update(this);

            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.IsActive) continue;
                if ((Tick + u.ThinkPhase) % Balance.ThinkInterval != 0) continue;
                if (u.IsPlayer) PlayerBrain.Think(this, u);
                else EnemyBrain.Think(this, u);
            }

            ActionSystem.Update(this);
            UnitMotor.Update(this);
            ProjectileSystem.Update(this);
            StructureSystem.Update(this);
            StatusSystem.Update(this);
            UpdateTelegraphs();
            Base?.Update(this);
            Director?.Update(this);
            Cleanup();
        }

        private void UpdateVisibility()
        {
            Fog.BeginFrame();
            foreach (var u in Units)
            {
                if (u.Faction == Faction.Player && u.IsActive) Fog.Reveal(u.Pos, Balance.SquadSight);
            }
            foreach (var s in Structures)
            {
                if (s.Removed) continue;
                if (s.Kind == StructureKind.Building) Fog.Reveal(s.Pos, Balance.BaseSight * 0.6f);
                else if (s.Faction == Faction.Player && s.Kind == StructureKind.BoltTurret) Fog.Reveal(s.Pos, Balance.TurretSight);
            }
            Fog.Reveal(BasePos, Balance.BaseSight);
            Fog.EndFrame();
        }

        private void UpdateTelegraphs()
        {
            foreach (var t in Telegraphs)
            {
                if (t.Removed) continue;
                t.Remaining -= Balance.Dt;
                if (t.Remaining < -0.6f) t.Removed = true; // owners resolve at <= 0; keep a beat for the view
            }
        }

        private void Cleanup()
        {
            for (int i = Units.Count - 1; i >= 0; i--)
            {
                var u = Units[i];
                if (u.Faction == Faction.Enemy && u.Life == LifeState.Dead && Time - u.DiedAt > 4f) u.Removed = true;
                if (u.Removed)
                {
                    Units.RemoveAt(i);
                    _entities.Remove(u.Id);
                }
            }
            for (int i = Structures.Count - 1; i >= 0; i--)
            {
                var s = Structures[i];
                if (s.Removed)
                {
                    Structures.RemoveAt(i);
                    _entities.Remove(s.Id);
                }
            }
            Projectiles.RemoveAll(p => p.Removed);
            Telegraphs.RemoveAll(t => t.Removed);
            Caches.RemoveAll(c => c.Removed);
            if (Alerts.Count > 60) Alerts.RemoveRange(0, Alerts.Count - 60);
            if (Reports.Count > 30) Reports.RemoveRange(0, Reports.Count - 30);
        }
    }
}
