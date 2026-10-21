using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>What each ability does when its cast completes.</summary>
    public static class AbilitySystem
    {
        public static void Resolve(World w, Unit u, AbilityId id, int targetId, Vec2 point, Vec2 dir)
        {
            var hits = w.AbilityScratch;
            var def = AbilityDefs.Get(id);
            Faction hostile = u.Faction == Faction.Player ? Faction.Enemy : Faction.Player;
            var target = w.FindUnit(targetId);
            float power = def.Power * (u.IsPlayer ? u.DamageMultiplier : 1f);

            switch (id)
            {
                case AbilityId.TauntRoar:
                {
                    float reach = Titan.TauntRadius(u, def);
                    w.QueryUnits(u.Pos, reach, hostile, hits);
                    foreach (var e in hits) Combat.Taunt(w, e, u, def.Duration);
                    w.Emit(SimEventType.AbilityImpact, u.Id, 0, u.Pos, reach, def.Type, id);
                    break;
                }
                case AbilityId.ShieldBash:
                    if (target != null && target.IsTargetable)
                    {
                        bool wasCasting = target.Action == UnitAction.Casting || target.Action == UnitAction.Channel;
                        Combat.Deal(w, u, target, power, def.Type, HitFlags.Melee | HitFlags.Ability);
                        if (target.IsActive) Combat.Stun(w, target, def.Duration);
                        if (wasCasting && u.Fight != null) u.Fight.Interrupts++;
                        w.Emit(SimEventType.AbilityImpact, u.Id, target.Id, target.Pos, 0f, def.Type, id);
                    }
                    break;
                case AbilityId.ShockwaveSlam:
                {
                    Vec2 face = dir.LengthSq > 0.001f ? dir.Normalized : Vec2.FromAngle(u.Facing);
                    ConeHits(w, u.Pos, face, def.Radius + u.Radius, def.ArcDeg, hostile, hits);
                    foreach (var e in hits.ToArray())
                    {
                        Combat.Deal(w, u, e, power, def.Type, HitFlags.Melee | HitFlags.Ability);
                        Combat.Knockback(w, e, e.Pos - u.Pos, 3f);
                    }
                    w.Emit(SimEventType.AbilityImpact, u.Id, 0, u.Pos + face * 2.5f, def.Radius, def.Type, id);
                    break;
                }
                case AbilityId.TitanForm:
                    Titan.Begin(w, u);
                    break;
                case AbilityId.RegenField:
                    StructureSystem.SpawnField(w, u, StructureKind.RegenField, point);
                    w.Emit(SimEventType.AbilityImpact, u.Id, 0, point, def.Radius, def.Type, id);
                    break;
                case AbilityId.Cleave:
                {
                    Vec2 face = Vec2.FromAngle(u.Facing);
                    ConeHits(w, u.Pos, face, u.Radius + def.Range + 0.9f, def.ArcDeg, hostile, hits);
                    foreach (var e in hits.ToArray()) Combat.Deal(w, u, e, power, def.Type, HitFlags.Melee | HitFlags.Ability);
                    w.Emit(SimEventType.AbilityImpact, u.Id, hits.Count, u.Pos + face * 1.5f, def.Range, def.Type, id);
                    break;
                }
                case AbilityId.GutHook:
                    if (target != null && target.IsTargetable)
                    {
                        Combat.Deal(w, u, target, power, def.Type, HitFlags.Ability | HitFlags.Ranged);
                        if (target.IsActive)
                        {
                            target.Status.BleedDps = Math.Max(target.Status.BleedDps, 6f * u.DamageMultiplier);
                            target.Status.BleedTime = def.Duration;
                            target.Status.BleedSource = u.Id;
                            if (target.Def.Mass < 4f)
                            {
                                Vec2 to = u.Pos - target.Pos;
                                float pull = Math.Max(0f, to.Length - (u.Radius + target.Radius + 0.5f));
                                Combat.Knockback(w, target, to, pull, 0.35f);
                            }
                            if (u.Fight != null) u.Fight.CcSeconds += 0.5f;
                        }
                        w.Emit(SimEventType.AbilityImpact, u.Id, target.Id, target.Pos, 0f, def.Type, id);
                    }
                    break;
                case AbilityId.CorrodeEdge:
                    if (target != null && target.IsTargetable)
                    {
                        Combat.Deal(w, u, target, power, def.Type, HitFlags.Melee | HitFlags.Ability);
                        if (target.IsActive)
                        {
                            target.Status.ShredStacks = Math.Min(Balance.MaxShredStacks, target.Status.ShredStacks + 2);
                            target.Status.ShredTime = 8f;
                        }
                        w.Emit(SimEventType.AbilityImpact, u.Id, target.Id, target.Pos, 0f, def.Type, id);
                    }
                    break;
                case AbilityId.BoltTurret:
                    StructureSystem.SpawnTurret(w, u, point);
                    break;
                case AbilityId.MortarPit:
                    StructureSystem.SpawnMortar(w, u, point);
                    break;
                case AbilityId.RaiseRampart:
                {
                    Vec2 along = dir.LengthSq > 0.001f ? dir.Normalized : Vec2.FromAngle(u.Facing).Right;
                    StructureSystem.SpawnRampart(w, u, point, along);
                    w.Emit(SimEventType.AbilityImpact, u.Id, 0, point, 2.5f, def.Type, id);
                    break;
                }
                case AbilityId.SpikeField:
                    StructureSystem.SpawnField(w, u, StructureKind.SpikeField, point);
                    w.Emit(SimEventType.AbilityImpact, u.Id, 0, point, def.Radius, def.Type, id);
                    break;
                case AbilityId.SnareCage:
                    if (target != null && target.IsTargetable)
                    {
                        Combat.Root(w, target, def.Duration);
                        if (u.Fight != null) u.Fight.CcSeconds += def.Duration;
                        w.Emit(SimEventType.AbilityImpact, u.Id, target.Id, target.Pos, def.Duration, def.Type, id);
                    }
                    break;

                // --- Enemy abilities ---
                case AbilityId.TailSlam:
                case AbilityId.RustmawSwipe:
                {
                    RemoveTelegraph(w, u.ActionTelegraphId);
                    float radius = id == AbilityId.TailSlam ? def.Radius : u.Radius + def.Range + 0.6f;
                    float dmg = def.Power * (u.Kind == UnitKind.Rustmaw && w.Boss != null ? w.Boss.DamageDealtMultiplier : 1f);
                    ConeHits(w, u.Pos, dir, radius, def.ArcDeg, hostile, hits);
                    foreach (var e in hits.ToArray())
                    {
                        Combat.Deal(w, u, e, dmg, def.Type, HitFlags.Melee | HitFlags.Ability);
                        if (id == AbilityId.TailSlam) Combat.Knockback(w, e, e.Pos - u.Pos, 2.5f);
                    }
                    // Swipes also smash structures in the arc.
                    foreach (var s in w.Structures.ToArray())
                    {
                        if (s.Faction != hostile || !s.IsTargetable) continue;
                        if (InCone(u.Pos, dir, radius + s.Radius, def.ArcDeg, s.Pos)) Combat.Deal(w, u, s, dmg, def.Type, HitFlags.Melee);
                    }
                    w.Emit(SimEventType.AbilityImpact, u.Id, 0, u.Pos + dir.Normalized * (radius * 0.6f), radius, def.Type, id);
                    break;
                }
            }
        }

        public static void RemoveTelegraph(World w, int id)
        {
            if (id == 0) return;
            foreach (var t in w.Telegraphs)
            {
                if (t.Id == id) t.Remaining = Math.Min(t.Remaining, 0f);
            }
        }

        public static bool InCone(Vec2 origin, Vec2 dir, float radius, float arcDeg, Vec2 p)
        {
            Vec2 d = p - origin;
            float dist = d.Length;
            if (dist > radius) return false;
            if (dist < 0.8f) return true;
            float ang = MathF.Abs(KMath.AngleDelta(dir.ToAngle(), d.ToAngle()));
            return ang <= arcDeg * 0.5f * KMath.Deg2Rad;
        }

        public static void ConeHits(World w, Vec2 origin, Vec2 dir, float radius, float arcDeg, Faction faction, List<Unit> results)
        {
            results.Clear();
            if (dir.LengthSq < 0.0001f) dir = Vec2.Forward;
            foreach (var e in w.Units)
            {
                if (e.Faction != faction || !e.IsTargetable) continue;
                Vec2 d = e.Pos - origin;
                float dist = d.Length;
                if (dist > radius + e.Radius) continue;
                if (dist < 0.8f + e.Radius)
                {
                    results.Add(e);
                    continue;
                }
                float ang = MathF.Abs(KMath.AngleDelta(dir.ToAngle(), d.ToAngle()));
                if (ang <= arcDeg * 0.5f * KMath.Deg2Rad + e.Radius / dist) results.Add(e);
            }
        }
    }
}
