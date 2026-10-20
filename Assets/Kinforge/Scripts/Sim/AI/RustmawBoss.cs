using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// Rustmaw, boss of Breach Hold, following the design doc's worked example:
    ///   1. The Charge (100-70%): telegraphed line charges; its rear vents are exposed after each
    ///      one, and a Warwright rampart in the way makes it crash and stagger.
    ///   2. Scrap Brood (70-35%): burrows and spawns scrap hounds plus two Scrap Menders that weld
    ///      its plating back on.
    ///   3. Overheat (35-0%): plating peels off so it takes double damage, but its vents blast fire
    ///      waves; walls between it and a unit block the blast.
    /// Soft enrage ramps its damage if a phase drags on.
    /// </summary>
    public sealed class RustmawBoss
    {
        private enum State
        {
            Dormant,
            Chase,
            ChargeWindup,
            Charging,
            Stagger,
            Burrowing,
            Burrowed,
            Surfacing,
            BlastWindup,
            Dead,
        }

        public Unit Body;
        public EnemyGroup Group;
        public Vec2 Center;
        public float ArenaRadius;
        public Structure Gate;
        public int Phase;
        public bool Defeated;
        public bool GateOpen;

        private State _state = State.Dormant;
        private float _stateTimer;
        private float _phaseTime;
        private float _chargeCd;
        private float _blastCd;
        private float _ventsTime;
        private float _noPlayersTime;
        private float _houndTimer;
        private int _wavesSpawned;
        private float _lastWeakPointEmit;
        private Vec2 _chargeDir;
        private float _chargeLeft;
        private readonly HashSet<int> _chargeHit = new HashSet<int>();
        private int _telegraphId;
        private Vec2 _surfaceAt;
        private readonly List<Unit> _scratch = new List<Unit>(16);

        public bool Active => Phase >= 1 && Phase <= 3 && !Defeated;
        public bool VentsExposed => _ventsTime > 0f;
        public bool Staggered => _state == State.Stagger;
        public bool Overheated => Phase == 3;
        public bool BlastPending => _state == State.BlastWindup;
        /// <summary>Seconds until the next fire wave starts its tell (phase 3), for the HUD.</summary>
        public float NextBlastIn => Phase == 3 && _state != State.BlastWindup ? Math.Max(0f, _blastCd) : 0f;
        public float NextChargeIn => (Phase == 1 || Phase == 2) && _state == State.Chase ? Math.Max(0f, _chargeCd) : 0f;
        public bool Burrowed => _state == State.Burrowed || _state == State.Burrowing;
        public float PhaseTime => _phaseTime;
        public string StateName => _state.ToString();

        public int MendersAlive
        {
            get
            {
                int n = 0;
                foreach (var m in Group.Members)
                {
                    if (m.Kind == UnitKind.ScrapMender && m.Life == LifeState.Active) n++;
                }
                return n;
            }
        }

        public float WeldRate => Body.MaxHp * 0.0075f;

        public float DamageDealtMultiplier => 1f + 0.1f * Math.Max(0f, (_phaseTime - 80f) / 10f);

        public string PhaseName
        {
            get
            {
                switch (Phase)
                {
                    case 1: return "Phase 1: The Charge";
                    case 2: return "Phase 2: Scrap Brood";
                    case 3: return "Phase 3: Overheat";
                    case 4: return "Defeated";
                }
                return "Dormant";
            }
        }

        public float DamageTakenMultiplier(World w, Entity src, HitFlags flags)
        {
            float m = 1f;
            if (Phase == 3) m *= 2f;
            if (_state == State.Stagger) m *= 1.5f;
            if (VentsExposed && src != null)
            {
                Vec2 toSrc = src.Pos - Body.Pos;
                float ang = Math.Abs(KMath.AngleDelta(Body.Facing, toSrc.ToAngle()));
                if (ang > 110f * KMath.Deg2Rad)
                {
                    m *= 2f;
                    if (w.Time - _lastWeakPointEmit > 0.4f)
                    {
                        _lastWeakPointEmit = w.Time;
                        w.Emit(SimEventType.WeakPoint, src.Id, Body.Id, Body.Pos - Vec2.FromAngle(Body.Facing) * Body.Radius);
                    }
                }
            }
            return m;
        }

        /// <summary>Playtest helper: jump straight to a phase (the fight must have started).</summary>
        public void DebugSkipToPhase(World w, int phase)
        {
            if (Phase == 0 || Defeated) return;
            if (phase == 2 && Phase == 1)
            {
                Body.Hp = Body.MaxHp * 0.69f;
                StartPhase2(w);
            }
            else if (phase == 3 && Phase <= 2)
            {
                Body.Untargetable = false;
                Body.Hp = Body.MaxHp * 0.34f;
                if (_state == State.Burrowed || _state == State.Burrowing || _state == State.Surfacing) Body.Pos = Center;
                StartPhase3(w);
            }
        }

        public void OpenGate(World w)
        {
            if (GateOpen) return;
            GateOpen = true;
            if (Gate != null) Combat.DestroyStructure(w, Gate);
            w.Alert("The Breach Sigil opens Breach Hold. Rustmaw waits inside. Bring at least two squads.", Center, 3);
        }

        public void Update(World w)
        {
            if (Defeated || Body == null || Body.Life != LifeState.Active) return;
            float dt = Balance.Dt;

            int inArena = 0;
            foreach (var u in w.Units)
            {
                if (u.IsPlayer && u.IsActive && w.InArena(u.Pos, 4f)) inArena++;
            }

            if (Phase == 0)
            {
                Body.Hp = Body.MaxHp;
                Body.Intent = new Intent { Kind = IntentKind.Move, Point = Center };
                foreach (var u in w.Units)
                {
                    if (u.IsPlayer && u.IsActive && w.InArena(u.Pos, -1f))
                    {
                        Begin(w, u);
                        break;
                    }
                }
                return;
            }

            _phaseTime += dt;
            if (inArena == 0)
            {
                _noPlayersTime += dt;
                if (_noPlayersTime > 10f)
                {
                    ResetFight(w);
                    return;
                }
            }
            else _noPlayersTime = 0f;

            if (_ventsTime > 0f) _ventsTime -= dt;

            if (Phase == 1 && Body.HpFraction <= 0.70f && _state != State.Charging) StartPhase2(w);
            else if (Phase == 2 && Body.HpFraction <= 0.35f && !Burrowed) StartPhase3(w);

            _stateTimer -= dt;
            switch (_state)
            {
                case State.Chase:
                    Chase(w, dt);
                    break;
                case State.ChargeWindup:
                    Body.Intent = default;
                    Body.Facing = KMath.RotateTowards(Body.Facing, _chargeDir.ToAngle(), 4f * dt);
                    if (_stateTimer <= 0f)
                    {
                        _state = State.Charging;
                        _chargeHit.Clear();
                        Body.Facing = _chargeDir.ToAngle();
                        w.Emit(SimEventType.BossCharge, Body.Id, 0, Body.Pos);
                    }
                    break;
                case State.Charging:
                    Charge(w, dt);
                    break;
                case State.Stagger:
                    Body.Intent = default;
                    if (_stateTimer <= 0f) _state = State.Chase;
                    break;
                case State.Burrowing:
                    Body.Intent = default;
                    if (_stateTimer <= 0f)
                    {
                        _state = State.Burrowed;
                        _stateTimer = 14f;
                        Body.Untargetable = true;
                        _wavesSpawned = 0;
                        _houndTimer = 0.5f;
                        w.Emit(SimEventType.BossBurrow, Body.Id, 0, Body.Pos);
                    }
                    break;
                case State.Burrowed:
                    Burrowed_(w, dt);
                    break;
                case State.Surfacing:
                    if (_stateTimer <= 0f) Surface(w);
                    break;
                case State.BlastWindup:
                    Body.Intent = default;
                    if (_stateTimer <= 0f) Blast(w);
                    break;
            }

            // Phase 2 keeps the brood coming once the menders are down.
            if (Phase == 2 && !Burrowed && _state != State.Surfacing)
            {
                _houndTimer -= dt;
                if (_houndTimer <= 0f)
                {
                    _houndTimer = 28f;
                    SpawnHounds(w, 4);
                }
            }
        }

        private void Begin(World w, Unit trigger)
        {
            Phase = 1;
            _phaseTime = 0f;
            _state = State.Chase;
            _chargeCd = 5f;
            Group.State = GroupState.Engaged;
            Body.Threat.Add(trigger.Id, 5f);
            w.Emit(SimEventType.BossPhase, Body.Id, 0, Body.Pos, 1f);
            w.Alert("Rustmaw awakens! Phase 1, The Charge: dodge its line charges, raise ramparts in its path, hit the vents on its back.", Center, 2);
            foreach (var sq in w.Squads)
            {
                if (sq.Fight != null) sq.Fight.KindsSeen.Add(UnitKind.Rustmaw);
            }
        }

        private void ResetFight(World w)
        {
            Phase = 0;
            _state = State.Dormant;
            Body.Untargetable = false;
            Body.ArmorOverride = -1f;
            Body.Pos = Center;
            Body.Hp = Body.MaxHp;
            Body.Status = default;
            Body.Threat.Clear();
            Body.Intent = default;
            Body.Action = UnitAction.None;
            _ventsTime = 0f;
            Group.State = GroupState.Idle;
            foreach (var m in Group.Members)
            {
                if (m != Body && m.Life == LifeState.Active)
                {
                    m.Life = LifeState.Dead;
                    m.Removed = true;
                }
            }
            Group.Members.RemoveAll(m => m != Body);
            AbilitySystem.RemoveTelegraph(w, _telegraphId);
            ReturnRaidCores(w);
            w.Alert("Rustmaw retreats into its lair and recovers.", Center, 1);
        }

        /// <summary>
        /// "Killed units stay out until the raid ends": when it ends, kin-cores dropped inside the
        /// arena find their way back to the Kin Cradle.
        /// </summary>
        private void ReturnRaidCores(World w)
        {
            int n = 0;
            foreach (var c in w.Caches)
            {
                if (c.Removed || !w.InArena(c.Pos, 4f)) continue;
                foreach (int id in c.UnitIds)
                {
                    w.Base?.ReceiveCore(w, id);
                    n++;
                }
                c.Removed = true;
            }
            if (n > 0) w.Alert($"The raid is over: {n} kin-core(s) left in Breach Hold return to the Kin Cradle.", w.BasePos, 3);
        }

        private Unit SelectTarget(World w)
        {
            if (Body.Status.TauntTime > 0f && EnemyBrain.ValidTarget(w, Body.Status.TauntBy))
            {
                var t = w.FindUnit(Body.Status.TauntBy);
                if (t != null && w.InArena(t.Pos, 3f)) return t;
            }
            int top = Body.Threat.Top(id => EnemyBrain.ValidTarget(w, id) && w.InArena(w.FindEntity(id).Pos, 3f));
            var pick = w.FindUnit(top);
            if (pick == null)
            {
                pick = w.NearestUnit(Body.Pos, Faction.Player, ArenaRadius * 2f, u => w.InArena(u.Pos, 2f));
                if (pick != null) Body.Threat.Add(pick.Id, 1f);
            }
            Body.CurrentTargetId = pick?.Id ?? 0;
            return pick;
        }

        private void Chase(World w, float dt)
        {
            var target = SelectTarget(w);
            if (Phase == 1 || Phase == 2)
            {
                _chargeCd -= dt;
                if (_chargeCd <= 0f && !Body.IsBusy)
                {
                    StartChargeWindup(w);
                    return;
                }
            }
            if (Phase == 3)
            {
                _blastCd -= dt;
                if (_blastCd <= 0f && !Body.IsBusy)
                {
                    StartBlast(w);
                    return;
                }
            }
            if (target == null)
            {
                Body.Intent = new Intent { Kind = IntentKind.Move, Point = Center };
                return;
            }
            float reach = Body.ReachTo(target, AbilityDefs.Get(AbilityId.RustmawSwipe).Range);
            if (Body.AbilityReady(AbilityId.RustmawSwipe) && Vec2.Distance(Body.Pos, target.Pos) <= reach)
                Body.Intent = new Intent { Kind = IntentKind.Ability, Ability = AbilityId.RustmawSwipe, TargetId = target.Id, Point = target.Pos };
            else
                Body.Intent = new Intent { Kind = IntentKind.Move, Point = target.Pos };
            if (!Body.IsBusy)
            {
                Vec2 d = target.Pos - Body.Pos;
                if (d.LengthSq > 0.1f) Body.Facing = KMath.RotateTowards(Body.Facing, d.ToAngle(), UnitMotor.TurnRate(Body) * dt);
            }
        }

        private void StartChargeWindup(World w)
        {
            // Prefer a far, soft target: that's who a charge punishes.
            Unit pick = null;
            float best = -1f;
            foreach (var u in w.Units)
            {
                if (!u.IsPlayer || !u.IsActive || !w.InArena(u.Pos, 2f)) continue;
                float s = Vec2.Distance(u.Pos, Body.Pos) + w.Rng.Range(0f, 6f);
                if (u.Role == Role.Healer || u.Role == Role.Siege) s += 4f;
                if (s > best)
                {
                    best = s;
                    pick = u;
                }
            }
            _chargeCd = Phase == 1 ? 9f : 14f;
            if (pick == null) return;
            Vec2 to = pick.Pos - Body.Pos;
            _chargeDir = to.Normalized;
            if (_chargeDir == Vec2.Zero) _chargeDir = Vec2.FromAngle(Body.Facing);
            _chargeLeft = KMath.Clamp(to.Length + 6f, 10f, 30f);
            _state = State.ChargeWindup;
            _stateTimer = 1.7f;
            Body.Action = UnitAction.None;
            var t = w.AddTelegraph(new Telegraph
            {
                Shape = TelegraphShape.Line, Pos = Body.Pos, Dir = _chargeDir, Length = _chargeLeft, Width = 4.6f,
                Duration = 1.7f, Threatens = Faction.Player, OwnerId = Body.Id, Label = "Line Charge",
            });
            _telegraphId = t.Id;
        }

        private void Charge(World w, float dt)
        {
            Body.Intent = default;
            float speed = 18f;
            float move = Math.Min(speed * dt, _chargeLeft);
            int steps = Math.Max(1, (int)Math.Ceiling(move / 0.4f));
            float step = move / steps;
            for (int s = 0; s < steps; s++)
            {
                Vec2 next = Body.Pos + _chargeDir * step;
                // What's in front of the plates?
                Vec2 nose = next + _chargeDir * (Body.Radius * 0.8f);
                Vec2 side = _chargeDir.Right * (Body.Radius * 0.7f);
                int cell = -1;
                foreach (var probe in new[] { nose, nose + side, nose - side })
                {
                    int cx = NavGrid.CellX(probe.X), cz = NavGrid.CellX(probe.Z);
                    if (w.Nav.BlocksMove(cx, cz))
                    {
                        cell = w.Nav.InBounds(cx, cz) ? w.Nav.Index(cx, cz) : -2;
                        break;
                    }
                }
                bool leavingArena = !w.InArena(next, -Body.Radius * 0.5f);
                if (cell != -1 || leavingArena)
                {
                    var wall = cell >= 0 ? w.FindStructure(w.Nav.OwnerOfCell(cell)) : null;
                    if (wall != null && wall.Kind == StructureKind.Rampart)
                    {
                        Combat.DestroyStructure(w, wall);
                        _state = State.Stagger;
                        _stateTimer = 4f;
                        _ventsTime = 4f;
                        Body.Status.StunTime = 4f;
                        w.Emit(SimEventType.BossCrash, Body.Id, wall.Id, nose);
                        w.Alert("Rustmaw crashed into a rampart and is staggered! Burst window, hit the vents!", Body.Pos, 3);
                        var builder = w.FindUnitAny(wall.OwnerUnitId);
                        if (builder?.Fight != null) builder.Fight.StructureAbsorbed += 300f;
                    }
                    else EndCharge();
                    return;
                }
                Body.Pos = next;
                HitInPath(w);
            }
            _chargeLeft -= move;
            if (_chargeLeft <= 0.01f) EndCharge();
        }

        private void HitInPath(World w)
        {
            float dmg = 115f * DamageDealtMultiplier;
            foreach (var u in w.Units)
            {
                if (!u.IsPlayer || !u.IsTargetable || _chargeHit.Contains(u.Id)) continue;
                if (Vec2.Distance(u.Pos, Body.Pos) > Body.Radius + u.Radius + 0.2f) continue;
                _chargeHit.Add(u.Id);
                Combat.Deal(w, Body, u, dmg, DamageType.Kinetic, HitFlags.Melee | HitFlags.Ability);
                float sideSign = Vec2.Dot(u.Pos - Body.Pos, _chargeDir.Right) >= 0f ? 1f : -1f;
                Combat.Knockback(w, u, _chargeDir.Right * sideSign + _chargeDir * 0.5f, 3.5f);
                Combat.Stun(w, u, 0.6f);
            }
        }

        private void EndCharge()
        {
            _state = State.Chase;
            _ventsTime = 3f;
        }

        private void StartPhase2(World w)
        {
            Phase = 2;
            _phaseTime = 0f;
            _state = State.Burrowing;
            _stateTimer = 1.5f;
            _chargeCd = 12f;
            AbilitySystem.RemoveTelegraph(w, _telegraphId);
            Combat.Interrupt(w, Body);
            w.Emit(SimEventType.BossPhase, Body.Id, 0, Body.Pos, 2f);
            w.Alert("Phase 2, Scrap Brood: Rustmaw burrows! Kill the Scrap Menders before they weld its plating back on.", Center, 2);
        }

        private void SpawnMenders(World w)
        {
            for (int i = 0; i < 2; i++)
            {
                Vec2 p = Center + Vec2.FromAngle(i * KMath.Pi + 0.8f) * (ArenaRadius - 4f);
                var m = Spawner.SpawnEnemy(w, UnitKind.ScrapMender, p, Group);
                m.Threat.Add(w.NearestUnit(m.Pos, Faction.Player, 60f)?.Id ?? 0, 1f);
            }
        }

        private void SpawnHounds(World w, int n)
        {
            var near = w.NearestUnit(Center, Faction.Player, ArenaRadius + 6f);
            Vec2 toward = near != null ? (near.Pos - Center).Normalized : Vec2.Forward;
            if (toward == Vec2.Zero) toward = Vec2.Forward;
            for (int i = 0; i < n; i++)
            {
                Vec2 p = Center + toward.Rotate((i - 1) * 0.6f + w.Rng.Range(-0.3f, 0.3f)) * (ArenaRadius - 3f);
                var h = Spawner.SpawnEnemy(w, UnitKind.ScrapHound, p, Group);
                var t = w.NearestUnit(h.Pos, Faction.Player, 60f);
                if (t != null) h.Threat.Add(t.Id, 1f);
            }
        }

        private void Burrowed_(World w, float dt)
        {
            Body.Intent = default;
            float elapsed = 14f - _stateTimer;
            if (_wavesSpawned < 3 && elapsed >= 0.5f + _wavesSpawned * 4.5f)
            {
                _wavesSpawned++;
                SpawnHounds(w, 4);
            }
            if (_stateTimer <= 0f)
            {
                // Surface under the biggest cluster of Vhorn.
                Unit pick = null;
                int bestN = -1;
                foreach (var u in w.Units)
                {
                    if (!u.IsPlayer || !u.IsActive || !w.InArena(u.Pos, 0f)) continue;
                    int n = w.CountUnits(u.Pos, 5f, Faction.Player);
                    if (u.Role == Role.Healer) n++;
                    if (n > bestN)
                    {
                        bestN = n;
                        pick = u;
                    }
                }
                _surfaceAt = pick != null ? pick.Pos : Center;
                _state = State.Surfacing;
                _stateTimer = 2f;
                _houndTimer = 26f;
                var t = w.AddTelegraph(new Telegraph
                {
                    Shape = TelegraphShape.Circle, Pos = _surfaceAt, Radius = 5f, Duration = 2f, Threatens = Faction.Player,
                    OwnerId = Body.Id, Label = "Burrow Eruption",
                });
                _telegraphId = t.Id;
            }
        }

        private void Surface(World w)
        {
            SpawnMenders(w);
            Body.Pos = w.Nav.NearestWalkable(_surfaceAt, 4);
            Body.PrevPos = Body.Pos;
            Body.Untargetable = false;
            _state = State.Chase;
            _scratch.Clear();
            w.QueryUnits(Body.Pos, 5f, Faction.Player, _scratch);
            foreach (var u in _scratch.ToArray())
            {
                Combat.Deal(w, Body, u, 80f * DamageDealtMultiplier, DamageType.Kinetic, HitFlags.Ability);
                Combat.Knockback(w, u, u.Pos - Body.Pos, 3f);
            }
            w.Emit(SimEventType.BossSurface, Body.Id, 0, Body.Pos, 5f);
        }

        private void StartPhase3(World w)
        {
            Phase = 3;
            _phaseTime = 0f;
            _state = State.Chase;
            _blastCd = 4f;
            Body.ArmorOverride = 0f;
            AbilitySystem.RemoveTelegraph(w, _telegraphId);
            w.Emit(SimEventType.BossPhase, Body.Id, 0, Body.Pos, 3f);
            w.Alert("Phase 3, Overheat: its plating peels off (double damage), but its vents blast fire. Stack behind ramparts for each blast, burst between them!", Center, 2);
        }

        private void StartBlast(World w)
        {
            _state = State.BlastWindup;
            _stateTimer = 2.6f;
            Combat.Interrupt(w, Body);
            var t = w.AddTelegraph(new Telegraph
            {
                Shape = TelegraphShape.Circle, Pos = Body.Pos, Radius = 16f, Duration = 2.6f, Threatens = Faction.Player,
                OwnerId = Body.Id, Label = "Fire Wave", CoverBlocks = true,
            });
            _telegraphId = t.Id;
        }

        private void Blast(World w)
        {
            _state = State.Chase;
            _blastCd = 8.5f;
            float dmg = 130f * DamageDealtMultiplier;
            foreach (var u in w.Units.ToArray())
            {
                if (!u.IsPlayer || !u.IsTargetable) continue;
                if (Vec2.Distance(u.Pos, Body.Pos) > 16f + u.Radius) continue;
                if (!w.Nav.SightClear(Body.Pos, u.Pos)) continue; // behind a wall
                Combat.Deal(w, Body, u, dmg, DamageType.Thermal, HitFlags.Ability);
            }
            foreach (var s in w.Structures.ToArray())
            {
                if (s.Removed || s.Faction != Faction.Player || !s.IsTargetable) continue;
                if (Vec2.Distance(s.Pos, Body.Pos) <= 16f) Combat.Deal(w, Body, s, 80f, DamageType.Thermal, HitFlags.Ability);
            }
            w.Emit(SimEventType.BossBlast, Body.Id, 0, Body.Pos, 16f, DamageType.Thermal);
        }

        public void OnDeath(World w)
        {
            Phase = 4;
            Defeated = true;
            _state = State.Dead;
            w.Victory = true;
            w.VictoryAt = w.Time;
            foreach (var m in Group.Members)
            {
                if (m != Body && m.Life == LifeState.Active) Combat.EnemyDeath(w, m, null);
            }
            foreach (var sq in w.Squads)
            {
                if (sq.Fight != null && sq.Fight.EnemyIds.Contains(Body.Id)) sq.Fight.BossKilled = true;
            }
            ReturnRaidCores(w);
            w.Emit(SimEventType.Victory, Body.Id, 0, Body.Pos);
            w.Alert("RUSTMAW IS DEAD! Its relic is yours: the Forge, Core Lathe and War Shrine can now be built (next milestone). Biome 1 cleared.", Body.Pos, 3);
        }
    }
}
