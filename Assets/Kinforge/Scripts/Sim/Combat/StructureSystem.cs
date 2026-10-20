using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>Spawning and per-tick behaviour of engineer works, fields and base buildings.</summary>
    public static class StructureSystem
    {
        public static Structure SpawnRampart(World w, Unit builder, Vec2 center, Vec2 dir, float length = 5f)
        {
            var def = AbilityDefs.Get(AbilityId.RaiseRampart);
            dir = dir.Normalized;
            if (dir == Vec2.Zero) dir = Vec2.Forward;
            var s = new Structure
            {
                Kind = StructureKind.Rampart,
                Faction = builder.Faction,
                OwnerUnitId = builder.Id,
                OwnerSquad = builder.Squad,
                Pos = center,
                Dir = dir,
                HalfLen = length * 0.5f,
                HalfWidth = 0.55f,
                BaseMaxHp = def.Power,
                Hp = def.Power,
                Life = def.Duration,
                MaxLife = def.Duration,
                BlocksMove = true,
                BlocksSight = true,
                Radius = length * 0.5f,
                BuiltAt = w.Time,
            };
            s.Cells = w.Nav.RasterBox(center, dir, s.HalfLen, s.HalfWidth + 0.15f);
            EnforceCap(w, builder.Squad, StructureKind.Rampart, Balance.MaxRampartsPerSquad);
            Place(w, s);
            return s;
        }

        public static Structure SpawnTurret(World w, Unit builder, Vec2 pos)
        {
            var def = AbilityDefs.Get(AbilityId.BoltTurret);
            pos = w.Nav.NearestWalkable(pos, 3);
            var s = new Structure
            {
                Kind = StructureKind.BoltTurret,
                Faction = builder.Faction,
                OwnerUnitId = builder.Id,
                OwnerSquad = builder.Squad,
                Pos = pos,
                Dir = builder.Squad != null ? builder.Squad.Forward : Vec2.Forward,
                Radius = 0.6f,
                BaseMaxHp = 160f,
                Hp = 160f,
                Life = def.Duration,
                MaxLife = def.Duration,
                Range = def.Radius,
                Power = def.Power,
                Type = DamageType.Kinetic,
                BlocksMove = true,
                BuiltAt = w.Time,
                AttackCooldown = 0.8f,
            };
            s.Cells = w.Nav.RasterCircle(pos, 0.6f);
            EnforceCap(w, builder.Squad, StructureKind.BoltTurret, Balance.MaxTurretsPerSquad);
            Place(w, s);
            return s;
        }

        public static Structure SpawnMortar(World w, Unit builder, Vec2 pos)
        {
            var def = AbilityDefs.Get(AbilityId.MortarPit);
            pos = w.Nav.NearestWalkable(pos, 3);
            var s = new Structure
            {
                Kind = StructureKind.MortarPit,
                Faction = builder.Faction,
                OwnerUnitId = builder.Id,
                OwnerSquad = builder.Squad,
                Pos = pos,
                Radius = 0.9f,
                BaseMaxHp = 200f,
                Hp = 200f,
                Life = def.Duration,
                MaxLife = def.Duration,
                Range = def.Radius,
                Power = def.Power,
                AreaRadius = 3f,
                Type = DamageType.Thermal,
                BlocksMove = true,
                BuiltAt = w.Time,
                AttackCooldown = 1.5f,
            };
            s.Cells = w.Nav.RasterCircle(pos, 0.9f);
            EnforceCap(w, builder.Squad, StructureKind.MortarPit, Balance.MaxMortarsPerSquad);
            Place(w, s);
            return s;
        }

        public static Structure SpawnField(World w, Unit builder, StructureKind kind, Vec2 pos)
        {
            var def = AbilityDefs.Get(kind == StructureKind.SpikeField ? AbilityId.SpikeField : AbilityId.RegenField);
            var s = new Structure
            {
                Kind = kind,
                Faction = builder.Faction,
                OwnerUnitId = builder.Id,
                OwnerSquad = builder.Squad,
                Pos = pos,
                Radius = def.Radius,
                AreaRadius = def.Radius,
                BaseMaxHp = 1f,
                Hp = 1f,
                Life = def.Duration,
                MaxLife = def.Duration,
                Power = def.Power,
                Type = def.Type,
                BuiltAt = w.Time,
            };
            if (kind == StructureKind.SpikeField) EnforceCap(w, builder.Squad, StructureKind.SpikeField, Balance.MaxSpikeFieldsPerSquad);
            Place(w, s);
            return s;
        }

        public static Structure SpawnBuilding(World w, BuildingKind kind, Vec2 pos, Vec2 dir, float halfLen, float halfWidth)
        {
            var s = new Structure
            {
                Kind = StructureKind.Building,
                Building = kind,
                Faction = Faction.Player,
                Pos = pos,
                Dir = dir.Normalized,
                HalfLen = halfLen,
                HalfWidth = halfWidth,
                Radius = Math.Max(halfLen, halfWidth),
                BaseMaxHp = 5000f,
                Hp = 5000f,
                Indestructible = true,
                BlocksMove = true,
                BlocksSight = true,
                BuiltAt = w.Time,
            };
            s.Cells = w.Nav.RasterBox(pos, s.Dir, halfLen + 0.3f, halfWidth + 0.3f);
            Place(w, s, false);
            return s;
        }

        public static Structure SpawnHatch(World w, Vec2 pos, Vec2 dir, float halfLen)
        {
            var s = new Structure
            {
                Kind = StructureKind.Hatch,
                Faction = Faction.Neutral,
                Pos = pos,
                Dir = dir.Normalized,
                HalfLen = halfLen,
                HalfWidth = 0.8f,
                Radius = halfLen,
                BaseMaxHp = 99999f,
                Hp = 99999f,
                Indestructible = true,
                BlocksMove = true,
                BlocksSight = true,
                BuiltAt = w.Time,
            };
            s.Cells = w.Nav.RasterBox(pos, s.Dir, halfLen, 1.1f);
            Place(w, s, false);
            return s;
        }

        private static void Place(World w, Structure s, bool emit = true)
        {
            w.Register(s);
            if (s.Cells != null && (s.BlocksMove || s.BlocksSight)) w.Nav.Stamp(s.Cells, s.BlocksMove, s.BlocksSight, s.Id, +1);
            if (emit) w.Emit(SimEventType.StructureBuilt, s.Id, s.OwnerUnitId, s.Pos);
        }

        private static void EnforceCap(World w, Squad squad, StructureKind kind, int cap)
        {
            if (squad == null) return;
            Structure oldest = null;
            int n = 0;
            foreach (var s in w.Structures)
            {
                if (s.Removed || s.Kind != kind || s.OwnerSquad != squad) continue;
                n++;
                if (oldest == null || s.BuiltAt < oldest.BuiltAt) oldest = s;
            }
            if (n >= cap && oldest != null) Combat.DestroyStructure(w, oldest);
        }

        public static void Update(World w)
        {
            float dt = Balance.Dt;
            for (int i = 0; i < w.Structures.Count; i++)
            {
                var s = w.Structures[i];
                if (s.Removed) continue;
                if (s.Life >= 0f)
                {
                    s.Life -= dt;
                    if (s.Life <= 0f)
                    {
                        Combat.DestroyStructure(w, s);
                        continue;
                    }
                }
                switch (s.Kind)
                {
                    case StructureKind.BoltTurret:
                        UpdateTurret(w, s, dt);
                        break;
                    case StructureKind.MortarPit:
                        UpdateMortar(w, s, dt);
                        break;
                    case StructureKind.SpikeField:
                        UpdateSpikes(w, s, dt);
                        break;
                    case StructureKind.RegenField:
                        UpdateRegen(w, s, dt);
                        break;
                }
            }
        }

        private static Faction Hostile(Structure s) => s.Faction == Faction.Player ? Faction.Enemy : Faction.Player;

        private static void UpdateTurret(World w, Structure s, float dt)
        {
            s.AttackCooldown -= dt;
            if (s.AttackCooldown > 0f) return;
            Unit target = null;
            var focus = s.OwnerSquad != null ? w.FindUnit(s.OwnerSquad.FocusTargetId != 0 ? s.OwnerSquad.FocusTargetId : s.OwnerSquad.AiFocusId) : null;
            if (focus != null && focus.IsTargetable && Vec2.Distance(focus.Pos, s.Pos) <= s.Range && w.Nav.SightClear(s.Pos, focus.Pos))
            {
                target = focus;
            }
            else
            {
                float best = float.MaxValue;
                foreach (var u in w.Units)
                {
                    if (u.Faction != Hostile(s) || !u.IsTargetable) continue;
                    float d = Vec2.Distance(u.Pos, s.Pos);
                    if (d > s.Range) continue;
                    if (!w.Nav.SightClear(s.Pos, u.Pos)) continue;
                    if (d < best)
                    {
                        best = d;
                        target = u;
                    }
                }
            }
            if (target == null)
            {
                s.AttackCooldown = 0.25f;
                return;
            }
            s.TargetId = target.Id;
            s.Dir = (target.Pos - s.Pos).Normalized;
            ProjectileSystem.Fire(w, s, s.OwnerUnitId, target, Vec2.Zero, false, s.Power, DamageType.Kinetic, 38f, s.Range, 2f,
                ProjectileVisual.TurretBolt, HitFlags.FromStructure);
            s.AttackCooldown = 0.8f;
        }

        private static void UpdateMortar(World w, Structure s, float dt)
        {
            s.AttackCooldown -= dt;
            if (s.AttackCooldown > 0f) return;
            Unit best = null;
            int bestCount = 0;
            foreach (var u in w.Units)
            {
                if (u.Faction != Hostile(s) || !u.IsTargetable) continue;
                float d = Vec2.Distance(u.Pos, s.Pos);
                if (d < 5f || d > s.Range) continue;
                int c = w.CountUnits(u.Pos, s.AreaRadius, u.Faction);
                if (c > bestCount)
                {
                    bestCount = c;
                    best = u;
                }
            }
            if (best == null)
            {
                s.AttackCooldown = 0.5f;
                return;
            }
            Vec2 aim = best.Pos + best.Velocity * 1.2f;
            float flight = 1.1f + Vec2.Distance(aim, s.Pos) / 30f;
            s.Dir = (aim - s.Pos).Normalized;
            ProjectileSystem.Lob(w, s, s.OwnerUnitId, aim, s.Power, s.Type, s.AreaRadius, flight);
            s.AttackCooldown = 3f;
        }

        private static void UpdateSpikes(World w, Structure s, float dt)
        {
            var hits = w.FieldScratch;
            w.QueryUnits(s.Pos, s.AreaRadius, Hostile(s), hits, false);
            var owner = w.FindUnitAny(s.OwnerUnitId);
            foreach (var u in hits)
            {
                if (u.Def.Flying) continue;
                u.Status.SlowPct = Math.Max(u.Status.SlowPct, 0.4f);
                u.Status.SlowTime = Math.Max(u.Status.SlowTime, 0.3f);
                Combat.Deal(w, s, u, s.Power * dt, DamageType.Kinetic, HitFlags.DoT | HitFlags.FromStructure);
                if (owner?.Fight != null) owner.Fight.CcSeconds += dt;
            }
        }

        private static void UpdateRegen(World w, Structure s, float dt)
        {
            var hits = w.FieldScratch;
            w.QueryUnits(s.Pos, s.AreaRadius, s.Faction, hits, false);
            var owner = w.FindUnitAny(s.OwnerUnitId);
            foreach (var u in hits)
            {
                Combat.Heal(w, owner, u, s.Power * dt, false);
            }
        }
    }
}
