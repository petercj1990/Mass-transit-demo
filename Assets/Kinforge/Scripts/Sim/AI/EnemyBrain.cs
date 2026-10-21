using System;

namespace Kinforge.Sim
{
    /// <summary>
    /// Crater fauna and salvage AI. Camps aggro together, leash home and heal if pulled too far,
    /// and attack whoever holds the most threat, with species quirks: Cratermaws pounce on the
    /// back line, Rockbacks slam in a telegraphed cone, drones kite, Scrap Menders weld allies,
    /// scrap hounds dive healers and engineers.
    /// </summary>
    public static class EnemyBrain
    {
        public static void OnAttacked(World w, Unit e, Entity src)
        {
            var g = e.Group;
            if (g == null || g.IsBossGroup) return;
            if (g.State == GroupState.Idle || g.State == GroupState.Hunting) Engage(w, g, src);
        }

        public static void Engage(World w, EnemyGroup g, Entity trigger)
        {
            if (g.State == GroupState.Engaged) return;
            g.State = GroupState.Engaged;
            g.EngagedAt = w.Time;
            var owner = Combat.OwnerUnit(w, trigger);
            foreach (var m in g.Members)
            {
                if (m.Life != LifeState.Active || m.Threat == null) continue;
                if (trigger != null && trigger.Faction == Faction.Player) m.Threat.Add(trigger.Id, 1f);
                if (owner != null && owner != trigger && owner.IsPlayer) m.Threat.Add(owner.Id, 0.5f);
                var near = w.NearestUnit(m.Pos, Faction.Player, 16f);
                if (near != null) m.Threat.Add(near.Id, 0.5f);
            }
        }

        private static void Reset(World w, EnemyGroup g)
        {
            g.State = GroupState.Returning;
            foreach (var m in g.Members)
            {
                if (m.Life != LifeState.Active) continue;
                m.Threat?.Clear();
                m.CurrentTargetId = 0;
                m.Intent = default;
                Combat.Interrupt(w, m);
            }
        }

        public static void UpdateGroups(World w)
        {
            if (w.Tick % 5 != 0) return;
            foreach (var g in w.Groups)
            {
                if (g.IsBossGroup) continue;
                int alive = 0;
                foreach (var m in g.Members)
                {
                    if (m.Life == LifeState.Active) alive++;
                }
                if (alive == 0) continue;

                switch (g.State)
                {
                    case GroupState.Idle:
                    {
                        var trigger = AnyPlayerNear(w, g, g.AggroRadius);
                        if (trigger != null) Engage(w, g, trigger);
                        break;
                    }
                    case GroupState.Hunting:
                    {
                        var target = w.FindSquad(g.HuntSquadId);
                        if (target == null || target.Wiped || target.Garrisoned || target.ActiveCount == 0 || w.InBase(target.Centroid))
                        {
                            target = PickHuntTarget(w);
                            g.HuntSquadId = target?.Id ?? 0;
                        }
                        if (target != null) g.Home = target.Centroid;
                        else if (w.Time - g.SpawnedAt > 150f) Despawn(w, g);
                        var trigger = AnyPlayerNear(w, g, 15f);
                        if (trigger != null) Engage(w, g, trigger);
                        break;
                    }
                    case GroupState.Engaged:
                    {
                        if (g.IsWarParty)
                        {
                            if (AnyPlayerNear(w, g, 35f) == null)
                            {
                                g.State = GroupState.Hunting;
                                foreach (var m in g.Members) m.Threat?.Clear();
                            }
                            break;
                        }
                        bool leashed = false;
                        bool anyThreat = false;
                        foreach (var m in g.Members)
                        {
                            if (m.Life != LifeState.Active) continue;
                            if (Vec2.Distance(m.Pos, g.Home) > g.LeashRadius) leashed = true;
                            if (m.Threat != null && m.Threat.Top(id => ValidTarget(w, id)) != 0) anyThreat = true;
                        }
                        if (leashed || (!anyThreat && AnyPlayerNear(w, g, 14f) == null && w.Time - g.EngagedAt > 3f)) Reset(w, g);
                        break;
                    }
                    case GroupState.Returning:
                    {
                        bool home = true;
                        foreach (var m in g.Members)
                        {
                            if (m.Life != LifeState.Active) continue;
                            Combat.Heal(w, null, m, m.MaxHp * 0.08f * 0.25f, false);
                            m.Status = default;
                            if (Vec2.Distance(m.Pos, g.Home) > 4f) home = false;
                        }
                        if (home) g.State = GroupState.Idle;
                        break;
                    }
                }
            }
        }

        private static Squad PickHuntTarget(World w)
        {
            Squad best = null;
            float bestD = float.MaxValue;
            foreach (var s in w.Squads)
            {
                if (s.Wiped || s.Garrisoned || s.ActiveCount == 0) continue;
                Vec2 c = s.Centroid;
                if (w.InBase(c)) continue;
                float d = Vec2.Distance(c, w.BasePos);
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        private static void Despawn(World w, EnemyGroup g)
        {
            foreach (var m in g.Members)
            {
                if (m.Life == LifeState.Active)
                {
                    m.Life = LifeState.Dead;
                    m.Removed = true;
                }
            }
        }

        private static Unit AnyPlayerNear(World w, EnemyGroup g, float r)
        {
            foreach (var m in g.Members)
            {
                if (m.Life != LifeState.Active) continue;
                var p = w.NearestUnit(m.Pos, Faction.Player, r);
                if (p != null) return p;
            }
            return null;
        }

        public static bool ValidTarget(World w, int id)
        {
            var e = w.FindEntity(id);
            if (e == null || !e.IsTargetable) return false;
            if (e is Structure s) return s.Faction == Faction.Player && !s.IsField && s.Kind != StructureKind.Building;
            return e.Faction == Faction.Player;
        }

        private static Entity SelectTarget(World w, Unit e)
        {
            if (e.Status.TauntTime > 0f && ValidTarget(w, e.Status.TauntBy)) return w.FindEntity(e.Status.TauntBy);
            if (e.Threat == null) return null;
            int top = e.Threat.Top(id => ValidTarget(w, id));
            var cur = ValidTarget(w, e.CurrentTargetId) ? w.FindEntity(e.CurrentTargetId) : null;
            Entity pick;
            if (cur != null && top != 0 && top != cur.Id)
            {
                bool melee = Vec2.Distance(cur.Pos, e.Pos) <= ActionSystem.AttackReach(e, cur) + 0.5f;
                float need = e.Threat.Get(cur.Id) * (melee ? 1.1f : 1.3f);
                pick = e.Threat.Get(top) > need ? w.FindEntity(top) : cur;
            }
            else
            {
                pick = cur ?? (top != 0 ? w.FindEntity(top) : null);
            }
            if (pick == null)
            {
                var near = w.NearestUnit(e.Pos, Faction.Player, 20f);
                if (near != null)
                {
                    e.Threat.Add(near.Id, 1f);
                    pick = near;
                }
            }
            e.CurrentTargetId = pick?.Id ?? 0;
            return pick;
        }

        public static void Think(World w, Unit e)
        {
            if (e.Kind == UnitKind.Rustmaw) return; // RustmawBoss drives the body
            var g = e.Group;
            if (g == null) return;

            switch (g.State)
            {
                case GroupState.Idle:
                {
                    // Prowl around the den.
                    if (w.Rng.Chance(0.08f) || e.Intent.Kind == IntentKind.Idle)
                    {
                        Vec2 p = g.Home + w.Rng.InsideCircle(g.IsCamp ? 5f : 2f);
                        e.Intent = new Intent { Kind = IntentKind.Move, Point = w.Nav.NearestWalkable(p, 3) };
                    }
                    return;
                }
                case GroupState.Returning:
                    e.Intent = new Intent { Kind = IntentKind.Move, Point = g.Home };
                    return;
                case GroupState.Hunting:
                {
                    int i = g.Members.IndexOf(e);
                    e.Intent = new Intent { Kind = IntentKind.Move, Point = w.Nav.NearestWalkable(g.Home + Vec2.FromAngle(i * 1.3f) * 2.5f, 3) };
                    return;
                }
            }

            var target = SelectTarget(w, e);
            if (target == null)
            {
                e.Intent = new Intent { Kind = IntentKind.Move, Point = g.Home };
                return;
            }

            switch (e.Kind)
            {
                case UnitKind.Cratermaw:
                    if (TryPounce(w, e)) return;
                    break;
                case UnitKind.Rockback:
                    if (e.AbilityReady(AbilityId.TailSlam))
                    {
                        Vec2 dir = (target.Pos - e.Pos).Normalized;
                        int n = 0;
                        foreach (var u in w.Units)
                        {
                            if (u.Faction == Faction.Player && u.IsTargetable && AbilitySystem.InCone(e.Pos, dir, 4.5f + u.Radius + e.Radius, 110f, u.Pos)) n++;
                        }
                        if (n >= 1 && Vec2.Distance(e.Pos, target.Pos) < 4.5f + e.Radius)
                        {
                            e.Intent = new Intent { Kind = IntentKind.Ability, Ability = AbilityId.TailSlam, TargetId = target.Id, Point = target.Pos };
                            return;
                        }
                    }
                    break;
                case UnitKind.SalvageDrone:
                {
                    // Drones keep their distance, but won't flee far from their nest: cornered, they fight.
                    var threat = w.NearestUnit(e.Pos, Faction.Player, 4.5f, u => u.Def.Attack.IsMelee);
                    if (threat != null && Vec2.Distance(e.Pos, g.Home) < 18f && e.HpFraction > 0.3f)
                    {
                        Vec2 away = (e.Pos - threat.Pos).Normalized;
                        e.Intent = new Intent { Kind = IntentKind.Move, Point = w.Nav.NearestWalkable(e.Pos + away * 5f, 3) };
                        return;
                    }
                    // Half the drones snipe the softest Vhorn in range instead of following threat.
                    if (e.Id % 2 == 0 && e.Status.TauntTime <= 0f)
                    {
                        var soft = w.NearestUnit(e.Pos, Faction.Player, 13f,
                            u => (u.Role == Role.Healer || u.Role == Role.Siege) && w.Nav.SightClear(e.Pos, u.Pos));
                        if (soft != null)
                        {
                            target = soft;
                            e.CurrentTargetId = soft.Id;
                        }
                    }
                    var it = new Intent { Kind = IntentKind.Attack, TargetId = target.Id, HasStand = true, StandTolerance = 2.5f };
                    Vec2 off = (e.Pos - target.Pos).Normalized;
                    if (off == Vec2.Zero) off = Vec2.FromAngle(e.Id);
                    it.Stand = w.Nav.NearestWalkable(target.Pos + off.Rotate(0.05f) * 10f, 3);
                    e.Intent = it;
                    return;
                }
                case UnitKind.ScrapMender:
                {
                    var threat = w.NearestUnit(e.Pos, Faction.Player, 3f, u => u.Def.Attack.IsMelee);
                    if (threat != null && e.Action != UnitAction.Channel && (g.IsBossGroup || Vec2.Distance(e.Pos, g.Home) < 18f))
                    {
                        Vec2 away = (e.Pos - threat.Pos).Normalized;
                        e.Intent = new Intent { Kind = IntentKind.Move, Point = w.Nav.NearestWalkable(e.Pos + away * 5f, 3) };
                        return;
                    }
                    Unit patient = null;
                    float worst = 0.97f;
                    foreach (var a in w.Units)
                    {
                        if (a.Faction != Faction.Enemy || !a.IsActive || a == e) continue;
                        if (Vec2.Distance(a.Pos, e.Pos) > 18f) continue;
                        float f = a.HpFraction;
                        if (a.Kind == UnitKind.Rustmaw) f -= 0.1f; // always welding the boss when it's hurt
                        if (f < worst)
                        {
                            worst = f;
                            patient = a;
                        }
                    }
                    if (patient != null && e.AbilityReady(AbilityId.WeldBeam))
                    {
                        e.Intent = new Intent { Kind = IntentKind.Ability, Ability = AbilityId.WeldBeam, TargetId = patient.Id, Point = patient.Pos };
                        return;
                    }
                    break;
                }
                case UnitKind.ScrapHound:
                {
                    if (e.Status.TauntTime <= 0f)
                    {
                        var soft = w.NearestUnit(e.Pos, Faction.Player, 14f,
                            u => u.Role == Role.Healer || u.Role == Role.Siege || u.Role == Role.Warwright);
                        // A hound already worrying a grown Titan stays on it.
                        if (soft != null && (target is Unit tu && tu.Role == Role.Tank && tu.TitanT <= 0f)) target = soft;
                    }
                    break;
                }
            }

            // Walled in? Break through the nearest player wall.
            if (e.Def.Attack.IsMelee && e.PathFailed && !w.Nav.LineWalkable(e.Pos, target.Pos, e.Radius))
            {
                Structure wall = null;
                float best = 5f;
                foreach (var s in w.Structures)
                {
                    if (s.Removed || s.Kind != StructureKind.Rampart || s.Faction != Faction.Player) continue;
                    float d = Vec2.DistanceToSegment(s.EndA, s.EndB, e.Pos);
                    if (d < best)
                    {
                        best = d;
                        wall = s;
                    }
                }
                if (wall != null) target = wall;
            }

            e.Intent = new Intent { Kind = IntentKind.Attack, TargetId = target.Id };
        }

        private static bool TryPounce(World w, Unit e)
        {
            if (!e.AbilityReady(AbilityId.Pounce) || e.Status.TauntTime > 0f) return false;
            Unit best = null;
            float bestS = 0f;
            foreach (var u in w.Units)
            {
                if (u.Faction != Faction.Player || !u.IsTargetable) continue;
                float d = Vec2.Distance(u.Pos, e.Pos);
                if (d < 4f || d > 10f) continue;
                if (!w.Nav.LineWalkable(e.Pos, u.Pos, e.Radius)) continue;
                if (Titan.ShieldOnLine(w, e.Pos, u.Pos, u, e.Radius) != null) continue; // no leaping past a Titan
                float s = 10f - d;
                if (u.Role == Role.Healer) s += 8f;
                else if (u.Role == Role.Siege) s += 6f;
                else if (u.Role == Role.Tank) s -= 6f;
                if (s > bestS)
                {
                    bestS = s;
                    best = u;
                }
            }
            if (best == null) return false;
            e.Threat?.Add(best.Id, 25f);
            e.CurrentTargetId = best.Id;
            e.Intent = new Intent { Kind = IntentKind.Ability, Ability = AbilityId.Pounce, TargetId = best.Id, Point = best.Pos };
            return true;
        }
    }
}
