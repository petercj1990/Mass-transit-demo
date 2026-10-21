using System;

namespace Kinforge.Sim
{
    /// <summary>
    /// Physical projectiles: shots travel, can miss, and are stopped by walls, rocks and bodies.
    /// A shot hits the first hostile body on its path, which is how a Shieldbearer standing in the
    /// line of fire protects the Mender behind it.
    /// </summary>
    public static class ProjectileSystem
    {
        private const float SubStep = 0.5f;
        private const float ShotRadius = 0.15f;

        public static Projectile Fire(World w, Entity shooter, int ownerUnitId, Entity target, Vec2 aimOverride, bool useAim,
            float damage, DamageType type, float speed, float range, float spreadDeg, ProjectileVisual visual, HitFlags flags)
        {
            Vec2 from = shooter.Pos;
            Vec2 aim;
            if (useAim)
            {
                aim = aimOverride;
            }
            else
            {
                aim = target.Pos;
                if (target is Unit tu)
                {
                    float t = Vec2.Distance(from, aim) / Math.Max(1f, speed);
                    aim += tu.Velocity * (t * 0.85f);
                }
            }
            Vec2 dir = (aim - from).Normalized;
            if (dir == Vec2.Zero) dir = Vec2.Forward;
            float spread = spreadDeg;
            if (shooter is Unit su && w.HeightAt(su.Pos) - w.HeightAt(aim) > Balance.HighGroundHeight) spread *= 0.6f;
            dir = dir.Rotate(w.Rng.Gaussian() * spread * KMath.Deg2Rad);

            float startOffset = shooter.Radius + 0.25f;
            var p = new Projectile
            {
                Id = w.NextId(),
                Faction = shooter.Faction,
                SourceId = shooter.Id,
                OwnerUnitId = ownerUnitId,
                Start = from,
                Pos = from + dir * startOffset,
                Dir = dir,
                Speed = speed,
                MaxRange = range * 1.3f + 2f,
                Damage = damage,
                Type = type,
                Flags = flags | HitFlags.Ranged,
                Visual = visual,
                StartHeight = shooter is Unit u2 ? u2.Def.Height * 0.65f : 1.4f,
            };
            p.PrevPos = p.Pos;
            w.Projectiles.Add(p);
            return p;
        }

        public static Projectile Lob(World w, Entity shooter, int ownerUnitId, Vec2 target, float damage, DamageType type, float splash, float flightTime)
        {
            var p = new Projectile
            {
                Id = w.NextId(),
                Faction = shooter.Faction,
                SourceId = shooter.Id,
                OwnerUnitId = ownerUnitId,
                Start = shooter.Pos,
                Pos = shooter.Pos,
                Dir = (target - shooter.Pos).Normalized,
                Arcing = true,
                ArcTarget = target,
                ArcDuration = flightTime,
                Damage = damage,
                Type = type,
                Splash = splash,
                Flags = HitFlags.Ranged | HitFlags.Splash | HitFlags.Ability,
                Visual = ProjectileVisual.Shell,
                StartHeight = 1.2f,
            };
            p.PrevPos = p.Pos;
            w.Projectiles.Add(p);
            return p;
        }

        public static void Update(World w)
        {
            float dt = Balance.Dt;
            for (int i = 0; i < w.Projectiles.Count; i++)
            {
                var p = w.Projectiles[i];
                if (p.Removed) continue;
                if (p.Arcing) UpdateArc(w, p, dt);
                else UpdateStraight(w, p, dt);
            }
        }

        private static void UpdateArc(World w, Projectile p, float dt)
        {
            p.ArcTime += dt;
            p.Pos = Vec2.Lerp(p.Start, p.ArcTarget, p.ArcProgress);
            if (p.ArcTime < p.ArcDuration) return;
            p.Removed = true;
            var src = w.FindEntity(p.SourceId);
            w.Emit(SimEventType.Explosion, p.SourceId, 0, p.ArcTarget, p.Splash, p.Type);
            Faction hostile = p.Faction == Faction.Player ? Faction.Enemy : Faction.Player;
            var hits = w.Scratch();
            w.QueryUnits(p.ArcTarget, p.Splash, hostile, hits);
            var copy = hits.ToArray();
            foreach (var u in copy)
            {
                float falloff = 1f - 0.4f * KMath.Clamp01(Vec2.Distance(u.Pos, p.ArcTarget) / Math.Max(0.1f, p.Splash));
                Combat.Deal(w, src ?? (Entity)w.FindUnitAny(p.OwnerUnitId), u, p.Damage * falloff, p.Type, p.Flags);
            }
        }

        private static void UpdateStraight(World w, Projectile p, float dt)
        {
            float move = p.Speed * dt;
            int steps = Math.Max(1, (int)MathF.Ceiling(move / SubStep));
            float stepLen = move / steps;
            Faction hostile = p.Faction == Faction.Player ? Faction.Enemy : Faction.Player;

            for (int s = 0; s < steps; s++)
            {
                Vec2 a = p.Pos;
                Vec2 b = a + p.Dir * stepLen;

                // Walls, rocks and wreckage stop shots for both sides.
                int cell = w.Nav.FirstBlockedCell(a, b, true);
                if (cell >= 0)
                {
                    p.Removed = true;
                    int owner = w.Nav.OwnerOfCell(cell);
                    var wall = w.FindStructure(owner);
                    Vec2 at = w.Nav.CellCenter(cell);
                    if (wall != null && wall.Faction != p.Faction)
                    {
                        Combat.Deal(w, w.FindEntity(p.SourceId), wall, p.Damage, p.Type, p.Flags);
                    }
                    else if (wall != null && wall.Faction == Faction.Player)
                    {
                        wall.DamageAbsorbed += p.Damage;
                        var builder = w.FindUnitAny(wall.OwnerUnitId);
                        if (builder?.Fight != null && p.Faction != Faction.Player) builder.Fight.StructureAbsorbed += p.Damage;
                    }
                    w.Emit(SimEventType.Miss, p.SourceId, owner, at);
                    return;
                }

                // First hostile body along the segment.
                Entity hit = null;
                float bestT = float.MaxValue;
                foreach (var u in w.Units)
                {
                    if (u.Faction != hostile || !u.IsTargetable) continue;
                    float t = SegmentCircle(a, b, u.Pos, u.Radius + ShotRadius + Balance.TitanShieldReach * u.TitanT);
                    if (t >= 0f && t < bestT)
                    {
                        bestT = t;
                        hit = u;
                    }
                }
                foreach (var st in w.Structures)
                {
                    if (st.Faction != hostile || !st.IsTargetable || st.IsWall) continue;
                    float t = SegmentCircle(a, b, st.Pos, st.Radius + ShotRadius);
                    if (t >= 0f && t < bestT)
                    {
                        bestT = t;
                        hit = st;
                    }
                }
                if (hit != null)
                {
                    p.Removed = true;
                    p.Pos = Vec2.Lerp(a, b, bestT);
                    var src = w.FindEntity(p.SourceId) ?? w.FindUnitAny(p.OwnerUnitId);
                    Combat.Deal(w, src, hit, p.Damage, p.Type, p.Flags);
                    return;
                }

                p.Pos = b;
                p.Traveled += stepLen;
                if (p.Traveled >= p.MaxRange)
                {
                    p.Removed = true;
                    w.Emit(SimEventType.Miss, p.SourceId, 0, p.Pos);
                    return;
                }
            }
        }

        /// <summary>Returns the segment parameter t in [0,1] of first contact with the circle, or -1.</summary>
        private static float SegmentCircle(Vec2 a, Vec2 b, Vec2 c, float r)
        {
            Vec2 d = b - a;
            Vec2 f = a - c;
            float A = Vec2.Dot(d, d);
            if (A < 1e-6f) return f.LengthSq <= r * r ? 0f : -1f;
            float B = 2f * Vec2.Dot(f, d);
            float C = Vec2.Dot(f, f) - r * r;
            if (C <= 0f) return 0f;
            float disc = B * B - 4f * A * C;
            if (disc < 0f) return -1f;
            float sq = MathF.Sqrt(disc);
            float t1 = (-B - sq) / (2f * A);
            if (t1 >= 0f && t1 <= 1f) return t1;
            return -1f;
        }
    }
}
