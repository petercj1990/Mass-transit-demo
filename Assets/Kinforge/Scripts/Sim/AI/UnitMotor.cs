using System;

namespace Kinforge.Sim
{
    /// <summary>
    /// Movement: straight-line steering when clear, A* around walls otherwise, then body collision.
    /// Heavier bodies push lighter ones aside, so a Shieldbearer genuinely body-blocks.
    /// </summary>
    public static class UnitMotor
    {
        private const int MaxPathsPerTick = 8;

        public static float BaseSpeed(Unit u)
        {
            float s = u.Def.Speed;
            if (u.Injured) s *= Balance.InjuredSpeedMul;
            if (u.IsPlayer) s *= 1f - Balance.LoadSpeedPenalty * u.LoadFraction;
            if (u.TitanT > 0f) s *= 1f - (1f - Balance.TitanSpeedMul) * u.TitanT;
            float slow = Math.Max(u.Status.SlowPct, u.Status.CryoStacks * Balance.CryoSlowPerStack);
            s *= Math.Max(0.3f, 1f - slow);
            return s;
        }

        public static float TurnRate(Unit u) => u.Kind == UnitKind.Rustmaw ? 2.2f : (u.Kind == UnitKind.Rockback ? 3.5f : 9f);

        public static void Update(World w)
        {
            w.PathsThisTick = 0;
            float dt = Balance.Dt;
            var units = w.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Life != LifeState.Active || u.Garrisoned)
                {
                    u.Velocity = Vec2.Zero;
                    u.Moving = false;
                    continue;
                }

                if (u.ForcedTime > 0f)
                {
                    float t = Math.Min(dt, u.ForcedTime);
                    u.ForcedTime -= dt;
                    Vec2 next = u.Pos + u.ForcedVel * t;
                    if (!w.Nav.BlocksMoveAt(next) || u.Def.Flying) u.Pos = next;
                    else u.ForcedTime = 0f;
                    u.Velocity = u.ForcedVel;
                    u.Moving = true;
                    continue;
                }

                bool movingFire = u.Action == UnitAction.Windup && u.Intent.FireOnMove;
                bool canMove = !u.Status.Immobile && u.HasMoveGoal && (!u.IsBusy || movingFire)
                               && u.Action != UnitAction.Resting && u.Action != UnitAction.Gathering;
                if (!canMove)
                {
                    u.Velocity = Vec2.Zero;
                    u.Moving = false;
                    continue;
                }

                Vec2 goal = u.MoveGoal;
                float dist = Vec2.Distance(u.Pos, goal);
                if (dist <= u.MoveArriveDist)
                {
                    u.Velocity = Vec2.Zero;
                    u.Moving = false;
                    continue;
                }

                float speed = BaseSpeed(u) * u.MoveSpeedMul * Titan.LoomMul(w, u);
                if (movingFire) speed *= 0.6f;
                Vec2 waypoint = Steer(w, u, goal);
                Vec2 dir = (waypoint - u.Pos).Normalized;
                if (dir == Vec2.Zero)
                {
                    u.Velocity = Vec2.Zero;
                    u.Moving = false;
                    continue;
                }
                float step = Math.Min(speed * dt, Vec2.Distance(u.Pos, waypoint) + 0.05f);
                u.Pos += dir * step;
                u.Velocity = dir * speed;
                u.Moving = true;
                if (!u.IsBusy) u.Facing = KMath.RotateTowards(u.Facing, dir.ToAngle(), TurnRate(u) * dt);
            }

            Separate(w);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Life == LifeState.Dead || u.Garrisoned) continue;
                bool walled = !u.Def.Flying && PushOutOfWalls(w, u);
                if (u.TitanTime > 0f) WatchTitan(w, u, walled);
                float max = Balance.MapSize - 1f;
                u.Pos = new Vec2(KMath.Clamp(u.Pos.X, 1f, max), KMath.Clamp(u.Pos.Z, 1f, max));
            }
        }

        /// <summary>
        /// A grown tank wedged against a wall without making progress shrinks back, so it can never
        /// be stuck in a gap its normal body fits through.
        /// </summary>
        private static void WatchTitan(World w, Unit u, bool walled)
        {
            bool slowGoing = u.HasMoveGoal && Vec2.Distance(u.Pos, u.PrevPos) < 0.3f * BaseSpeed(u) * Balance.Dt
                             && Vec2.Distance(u.Pos, u.MoveGoal) > 1.5f;
            u.TitanStuck = walled && slowGoing ? u.TitanStuck + Balance.Dt : 0f;
            if (u.TitanStuck > Balance.TitanStuckSeconds)
            {
                u.TitanStuck = 0f;
                Titan.Cap(u, 0.001f, Titan.FadeCramped);
            }
        }

        private static Vec2 Steer(World w, Unit u, Vec2 goal)
        {
            // Big bodies use clearance-aware paths; everyone else keeps the plain grid.
            bool wide = u.TitanTime > 0f;
            bool pathStale = u.Path.Count == 0
                             || Vec2.DistanceSq(u.PathGoal, goal) > 4f
                             || u.PathNavVersion != w.Nav.Version
                             || w.Time - u.PathTime > 3f;

            if (pathStale && w.Nav.LineWalkable(u.Pos, goal, u.Radius, wide))
            {
                u.Path.Clear();
                u.PathFailed = false;
                return goal;
            }

            if (pathStale && w.Time - u.PathTime > 0.4f && w.PathsThisTick < MaxPathsPerTick)
            {
                w.PathsThisTick++;
                bool ok = wide ? w.Nav.FindPathWide(u.Pos, goal, u.Path, u.Radius) : w.Nav.FindPath(u.Pos, goal, u.Path, u.Radius);
                u.PathFailed = !ok;
                u.PathIndex = 0;
                u.PathGoal = goal;
                u.PathNavVersion = w.Nav.Version;
                u.PathTime = w.Time;
            }

            if (u.Path.Count == 0) return goal;

            while (u.PathIndex < u.Path.Count - 1)
            {
                if (Vec2.DistanceSq(u.Pos, u.Path[u.PathIndex]) < 0.6f * 0.6f
                    || w.Nav.LineWalkable(u.Pos, u.Path[u.PathIndex + 1], u.Radius, wide))
                {
                    u.PathIndex++;
                }
                else break;
            }
            if (u.PathIndex >= u.Path.Count) return goal;
            Vec2 wp = u.Path[u.PathIndex];
            if (u.PathIndex == u.Path.Count - 1 && !u.PathFailed) return goal;
            return wp;
        }

        private static void Separate(World w)
        {
            var units = w.Units;
            int n = units.Count;
            for (int i = 0; i < n; i++)
            {
                var a = units[i];
                if (a.Life != LifeState.Active || a.Garrisoned || a.Untargetable) continue;
                for (int j = i + 1; j < n; j++)
                {
                    var b = units[j];
                    if (b.Life != LifeState.Active || b.Garrisoned || b.Untargetable) continue;
                    if (a.Def.Flying != b.Def.Flying) continue;
                    float minD = a.Radius + b.Radius;
                    float dx = a.Pos.X - b.Pos.X;
                    float dz = a.Pos.Z - b.Pos.Z;
                    float dsq = dx * dx + dz * dz;
                    if (dsq >= minD * minD) continue;
                    float d = MathF.Sqrt(dsq);
                    Vec2 nrm = d > 1e-4f ? new Vec2(dx / d, dz / d) : Vec2.FromAngle((a.Id * 37 + b.Id) % 628 / 100f);
                    float overlap = minD - d;
                    float strength = a.Faction == b.Faction ? 0.45f : 0.9f;
                    float ma = a.Mass;
                    float mb = b.Mass;
                    if (a.ForcedTime > 0f) ma *= 4f;
                    if (b.ForcedTime > 0f) mb *= 4f;
                    float total = ma + mb;
                    a.Pos += nrm * (overlap * strength * (mb / total));
                    b.Pos -= nrm * (overlap * strength * (ma / total));
                }
            }
        }

        /// <summary>Pushes a body out of blocked cells; true if it touched one.</summary>
        private static bool PushOutOfWalls(World w, Unit u)
        {
            bool touched = false;
            float r = u.Radius;
            int x0 = NavGrid.CellX(u.Pos.X - r), x1 = NavGrid.CellX(u.Pos.X + r);
            int z0 = NavGrid.CellX(u.Pos.Z - r), z1 = NavGrid.CellX(u.Pos.Z + r);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!w.Nav.BlocksMove(x, z)) continue;
                float cx = KMath.Clamp(u.Pos.X, x, x + 1f);
                float cz = KMath.Clamp(u.Pos.Z, z, z + 1f);
                float dx = u.Pos.X - cx;
                float dz = u.Pos.Z - cz;
                float dsq = dx * dx + dz * dz;
                if (dsq >= r * r) continue;
                if (dsq < 1e-6f)
                {
                    u.Pos = w.Nav.NearestWalkable(u.Pos, 6);
                    return true;
                }
                float d = MathF.Sqrt(dsq);
                u.Pos += new Vec2(dx / d, dz / d) * (r - d);
                touched = true;
            }
            return touched;
        }
    }
}
