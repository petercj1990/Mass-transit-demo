using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// Squad-level state: which enemies a squad is fighting, when fights start and end, and the
    /// non-combat task it is carrying out (travel, gather, haul home, garrison, recover cores).
    /// </summary>
    public static class SquadSystem
    {
        public static void Update(World w)
        {
            float dt = Balance.Dt;
            foreach (var sq in w.Squads)
            {
                if (sq.Garrisoned)
                {
                    sq.InCombat = false;
                    sq.Engaged.Clear();
                    continue;
                }
                if (sq.ActiveCount == 0)
                {
                    sq.Engaged.Clear();
                    if (sq.InCombat && !sq.Wiped && w.Time - sq.LastEngagedTime > Balance.CombatEndDelay) EndFight(w, sq, false);
                    continue;
                }

                if ((w.Tick + sq.Id) % 2 == 0) UpdateEngagement(w, sq);

                if (sq.Engaged.Count > 0)
                {
                    sq.LastEngagedTime = w.Time;
                    if (!sq.InCombat) BeginFight(w, sq);
                }
                else if (sq.InCombat && w.Time - sq.LastEngagedTime > Balance.CombatEndDelay)
                {
                    EndFight(w, sq, false);
                }

                if (sq.InCombat)
                {
                    sq.Fight?.Tick(w, sq, dt);
                    UpdateCombatFrame(w, sq, dt);
                }

                DepositAtBase(w, sq);
                UpdateTask(w, sq);
                UpdateMarch(w, sq, dt);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Engagement
        // ---------------------------------------------------------------------------------------

        private static void UpdateEngagement(World w, Squad sq)
        {
            sq.Engaged.Clear();
            float r2 = Balance.EngageRadius * Balance.EngageRadius;
            foreach (var e in w.Units)
            {
                if (e.Faction != Faction.Enemy || e.Life != LifeState.Active || e.Untargetable) continue;
                bool engaged = false;
                bool targetsUs = false;
                if (e.Threat != null)
                {
                    for (int i = 0; i < e.Threat.Count; i++)
                    {
                        var t = w.FindUnit(e.Threat.IdAt(i));
                        if (t != null && t.Squad == sq)
                        {
                            targetsUs = true;
                            break;
                        }
                    }
                }
                foreach (var m in sq.Members)
                {
                    if (!m.IsActive) continue;
                    float d = Vec2.DistanceSq(m.Pos, e.Pos);
                    if (d <= r2 && (e.Group == null || e.Group.State != GroupState.Idle || d <= 100f))
                    {
                        engaged = true;
                        break;
                    }
                    if (targetsUs && d <= 40f * 40f)
                    {
                        engaged = true;
                        break;
                    }
                }
                if (engaged) sq.Engaged.Add(e);
            }
        }

        private static void BeginFight(World w, Squad sq)
        {
            sq.InCombat = true;
            sq.CombatStartTime = w.Time;
            Vec2 c = sq.Centroid;
            sq.CombatAnchor = sq.Task == SquadTask.Idle ? sq.Anchor : c;
            if (sq.Stance == CommandType.HoldPosition || sq.Stance == CommandType.Fortify) sq.CombatAnchor = sq.Anchor;
            Vec2 ec = EnemyCenter(sq);
            Vec2 f = (ec - c).Normalized;
            if (f != Vec2.Zero) sq.Forward = f;
            sq.AiFocusId = 0;
            sq.Fight = new FightTracker();
            sq.Fight.Begin(w, sq);
            if (sq.Stance == CommandType.Rest)
            {
                sq.Stance = sq.PrevStance == CommandType.Rest ? CommandType.DefensiveFormation : sq.PrevStance;
                w.Alert($"{sq.Name}'s rest was interrupted!", c, 2);
            }
            if (w.Time - sq.LastUnderAttackAlert > 20f)
            {
                sq.LastUnderAttackAlert = w.Time;
                w.Alert($"{sq.Name} is in combat.", c, 1);
            }
        }

        public static void EndFight(World w, Squad sq, bool wiped)
        {
            if (sq.Fight == null)
            {
                sq.InCombat = false;
                return;
            }
            sq.InCombat = false;
            sq.Engaged.Clear();
            if (!wiped)
            {
                // "Rises when combat ends; either way it becomes Injured."
                foreach (var m in sq.Members)
                {
                    if (m.Life == LifeState.Downed) Combat.Revive(w, m, Balance.RiseHpFraction, null);
                }
            }
            var report = sq.Fight.End(w, sq, wiped);
            sq.Fight = null;
            sq.LastReport = report;
            w.Reports.Add(report);
            sq.FocusTargetId = sq.Task == SquadTask.Attack ? sq.FocusTargetId : 0;
            sq.AiFocusId = 0;
            if (!wiped && (sq.Task == SquadTask.Idle || sq.Task == SquadTask.Attack)) sq.Anchor = sq.Centroid;
        }

        public static Vec2 EnemyCenter(Squad sq)
        {
            Vec2 sum = Vec2.Zero;
            int n = 0;
            foreach (var e in sq.Engaged)
            {
                if (e.Life != LifeState.Active) continue;
                sum += e.Pos;
                n++;
            }
            return n > 0 ? sum / n : sq.Centroid + sq.Forward * 5f;
        }

        /// <summary>Keeps the squad's notion of "forward" pointed at the threat.</summary>
        private static void UpdateCombatFrame(World w, Squad sq, float dt)
        {
            Vec2 c = sq.Stance == CommandType.HoldPosition || sq.Stance == CommandType.Fortify || sq.Stance == CommandType.DefensiveFormation
                ? sq.CombatAnchor : sq.Centroid;
            Vec2 desired = (EnemyCenter(sq) - c).Normalized;
            if (desired == Vec2.Zero) return;
            // Defensive Formation only rotates to cover flanks from Doctrine tier 4.
            float rate = 1.5f;
            if (sq.Stance == CommandType.DefensiveFormation)
            {
                int tier = SquadTier(sq);
                rate = tier >= 4 ? 1.2f : 0.25f;
            }
            float a = KMath.RotateTowards(sq.Forward.ToAngle(), desired.ToAngle(), rate * dt);
            sq.Forward = Vec2.FromAngle(a);

            // Defensive postures creep toward enemies that refuse to come to them, so fights resolve.
            if ((sq.Stance == CommandType.DefensiveFormation) && sq.Task == SquadTask.Idle)
            {
                float nearest = float.MaxValue;
                foreach (var e in sq.Engaged)
                {
                    float d = Vec2.Distance(e.Pos, sq.CombatAnchor);
                    if (d < nearest) nearest = d;
                }
                if (nearest > 12f && w.Time - sq.CombatStartTime > 5f) sq.CombatAnchor += sq.Forward * (1.2f * dt);
            }
        }

        /// <summary>Average Doctrine tier of active members in the squad's current command.</summary>
        public static int SquadTier(Squad sq)
        {
            int sum = 0, n = 0;
            foreach (var m in sq.Members)
            {
                if (!m.IsActive || m.Doctrine == null) continue;
                sum += m.Doctrine.Tier(sq.Stance);
                n++;
            }
            return n > 0 ? sum / n : 0;
        }

        // ---------------------------------------------------------------------------------------
        // Tasks
        // ---------------------------------------------------------------------------------------

        public static bool IsTraveling(World w, Squad sq)
        {
            switch (sq.Task)
            {
                case SquadTask.Gather:
                    // At the node the squad stops marching and gets to work.
                    return !sq.Arrived && w.FindNode(sq.TaskTargetId) != null;
                case SquadTask.Move:
                case SquadTask.Attack:
                case SquadTask.Return:
                case SquadTask.Garrison:
                case SquadTask.Recover:
                case SquadTask.FallBack:
                    return true;
            }
            return false;
        }

        /// <summary>Marching rather than fighting: the squad's units walk in formation instead of picking fights.</summary>
        public static bool IsMarching(World w, Squad sq) =>
            IsTraveling(w, sq) && (sq.Task == SquadTask.FallBack || !sq.InCombat || sq.Forced);

        public static Vec2 TravelGoal(World w, Squad sq)
        {
            switch (sq.Task)
            {
                case SquadTask.Move:
                case SquadTask.FallBack:
                    return sq.TaskPos;
                case SquadTask.Attack:
                {
                    var t = w.FindUnit(sq.TaskTargetId);
                    return t != null ? t.Pos : sq.Anchor;
                }
                case SquadTask.Gather:
                {
                    var n = w.FindNode(sq.TaskTargetId);
                    return n != null ? n.Pos : sq.Anchor;
                }
                case SquadTask.Return:
                case SquadTask.Garrison:
                    return w.BasePos;
                case SquadTask.Recover:
                {
                    var c = w.FindCache(sq.TaskTargetId);
                    return c != null ? c.Pos : w.BasePos;
                }
            }
            return sq.Anchor;
        }

        public static void BeginTravel(World w, Squad sq)
        {
            sq.MarchPoint = sq.Centroid;
            sq.MarchPath.Clear();
            sq.MarchNavVersion = -1;
            sq.Arrived = false;
            if (!sq.InCombat) sq.Forced = false;
        }

        private static void UpdateTask(World w, Squad sq)
        {
            Vec2 c = sq.Centroid;
            switch (sq.Task)
            {
                case SquadTask.Move:
                    if (Vec2.Distance(sq.MarchPoint, sq.TaskPos) < 0.6f && Vec2.Distance(c, sq.TaskPos) < 4f)
                    {
                        sq.Task = SquadTask.Idle;
                        sq.Anchor = sq.TaskPos;
                        sq.CombatAnchor = sq.TaskPos;
                        sq.Forced = false;
                    }
                    break;
                case SquadTask.Attack:
                {
                    var t = w.FindUnit(sq.TaskTargetId);
                    if (t == null || t.Life != LifeState.Active)
                    {
                        sq.Task = SquadTask.Idle;
                        sq.Anchor = c;
                        sq.FocusTargetId = 0;
                    }
                    break;
                }
                case SquadTask.Gather:
                {
                    var n = w.FindNode(sq.TaskTargetId);
                    if (n == null)
                    {
                        if (sq.CarryLoad > 1f)
                        {
                            sq.Task = SquadTask.Return;
                            sq.ResumeTask = SquadTask.Idle;
                        }
                        else
                        {
                            sq.Task = SquadTask.Idle;
                            sq.Anchor = c;
                        }
                    }
                    else if (sq.CarryLoad >= sq.CarryCapacity * Balance.GatherReturnFraction)
                    {
                        sq.Task = SquadTask.Return;
                        sq.ResumeTask = SquadTask.Gather;
                        sq.ResumeTargetId = n.Id;
                        BeginTravel(w, sq);
                    }
                    break;
                }
                case SquadTask.Return:
                    if (AllActiveInBase(w, sq) && sq.CarryLoad < 0.5f)
                    {
                        if (sq.ResumeTask == SquadTask.Gather && w.FindNode(sq.ResumeTargetId) != null)
                        {
                            sq.Task = SquadTask.Gather;
                            sq.TaskTargetId = sq.ResumeTargetId;
                            BeginTravel(w, sq);
                        }
                        else
                        {
                            sq.Task = SquadTask.Idle;
                            sq.Anchor = w.BasePos + (sq.Centroid - w.BasePos).ClampLength(8f);
                        }
                        sq.ResumeTask = SquadTask.Idle;
                    }
                    break;
                case SquadTask.Garrison:
                    if (AllActiveInBase(w, sq)) Garrison(w, sq);
                    break;
                case SquadTask.Recover:
                {
                    var cache = w.FindCache(sq.TaskTargetId);
                    if (cache == null)
                    {
                        sq.Task = SquadTask.Return;
                        sq.ResumeTask = SquadTask.Idle;
                        break;
                    }
                    foreach (var m in sq.Members)
                    {
                        if (!m.IsActive || Vec2.Distance(m.Pos, cache.Pos) > 3f) continue;
                        Combat.GiveCores(w, m, cache.UnitIds);
                        cache.Removed = true;
                        w.Alert($"{sq.Name} recovered {cache.UnitIds.Count} kin-core(s). Bring them home to the Kin Cradle.", cache.Pos, 3);
                        sq.Task = SquadTask.Return;
                        sq.ResumeTask = SquadTask.Idle;
                        BeginTravel(w, sq);
                        break;
                    }
                    break;
                }
                case SquadTask.FallBack:
                    if (Vec2.Distance(c, sq.TaskPos) < 6f)
                    {
                        sq.Task = SquadTask.Idle;
                        sq.Anchor = sq.TaskPos;
                        sq.Stance = sq.PrevStance == CommandType.FallBack || sq.PrevStance == CommandType.Rest ? CommandType.DefensiveFormation : sq.PrevStance;
                    }
                    break;
            }
        }

        private static bool AllActiveInBase(World w, Squad sq)
        {
            foreach (var m in sq.Members)
            {
                if (m.IsActive && !w.InBase(m.Pos)) return false;
            }
            return true;
        }

        /// <summary>Anyone inside the base drops their load, hands kin-cores to the Cradle and banks Doctrine XP.</summary>
        private static void DepositAtBase(World w, Squad sq)
        {
            foreach (var m in sq.Members)
            {
                if (!m.IsActive || !w.InBase(m.Pos)) continue;
                if (m.Carry != null && m.Carry.Total > 0.01f)
                {
                    float amount = m.Carry.Total;
                    m.Carry.MoveAllTo(w.Stock);
                    w.Emit(SimEventType.Deposit, m.Id, 0, m.Pos, amount);
                }
                if (m.CarriedCores.Count > 0)
                {
                    foreach (int id in m.CarriedCores) w.Base?.ReceiveCore(w, id);
                    m.CarriedCores.Clear();
                }
                m.Doctrine?.Bank();
            }
        }

        public static void Garrison(World w, Squad sq)
        {
            sq.Garrisoned = true;
            sq.Task = SquadTask.Idle;
            sq.InCombat = false;
            if (sq.Stance == CommandType.Rest || sq.Stance == CommandType.FallBack) sq.Stance = sq.PrevStance == CommandType.Rest || sq.PrevStance == CommandType.FallBack ? CommandType.DefensiveFormation : sq.PrevStance;
            foreach (var m in sq.Members)
            {
                if (m.Life != LifeState.Active) continue;
                Titan.EndInstant(w, m, Titan.FadeLeftFight);
                m.Garrisoned = true;
                m.Action = UnitAction.None;
                m.Intent = default;
                m.Velocity = Vec2.Zero;
                m.Doctrine?.Bank();
            }
            w.Alert($"{sq.Name} garrisoned: resting and working the Smelter and Salvage Bench.", w.BasePos, 0);
        }

        public static void Ungarrison(World w, Squad sq)
        {
            if (!sq.Garrisoned) return;
            sq.Garrisoned = false;
            int i = 0;
            foreach (var m in sq.Members)
            {
                if (!m.Garrisoned) continue;
                m.Garrisoned = false;
                m.Pos = w.Nav.NearestWalkable(w.BasePos + Vec2.FromAngle(i * 1.25f) * 3f);
                m.PrevPos = m.Pos;
                i++;
            }
            sq.Anchor = w.BasePos;
            sq.MarchPoint = w.BasePos;
        }

        // ---------------------------------------------------------------------------------------
        // Formation march
        // ---------------------------------------------------------------------------------------

        private static void UpdateMarch(World w, Squad sq, float dt)
        {
            // Orders given out of combat behave like attack-moves: the squad stops to fight what it meets.
            // Orders given mid-fight (Forced) and Fall Back keep marching.
            bool traveling = IsTraveling(w, sq) && (sq.Task == SquadTask.FallBack || !sq.InCombat || sq.Forced);
            if (!traveling)
            {
                if (!sq.InCombat) sq.MarchPoint = Vec2.MoveTowards(sq.MarchPoint, sq.Anchor, sq.MarchSpeed * dt);
                return;
            }

            Vec2 goal = TravelGoal(w, sq);
            if (sq.MarchPath.Count == 0 || Vec2.DistanceSq(goal, sq.MarchGoal) > 9f || sq.MarchNavVersion != w.Nav.Version)
            {
                if (!w.Nav.FindPath(sq.MarchPoint, goal, sq.MarchPath, 1.2f) && sq.MarchPath.Count == 0) sq.MarchPath.Add(goal);
                sq.MarchIndex = 0;
                sq.MarchGoal = goal;
                sq.MarchNavVersion = w.Nav.Version;
            }

            // Don't let the march point run away from stragglers.
            float maxLag = 0f;
            foreach (var m in sq.Members)
            {
                if (!m.IsActive) continue;
                float lag = Vec2.Distance(m.Pos, sq.MarchPoint);
                if (lag > maxLag) maxLag = lag;
            }
            if (maxLag > 7f) return;

            if (sq.Task == SquadTask.Gather && w.FindNode(sq.TaskTargetId) is ResourceNode node
                && Vec2.Distance(sq.MarchPoint, node.Pos) < node.Radius + 5f)
            {
                sq.Arrived = true;
                sq.Anchor = sq.MarchPoint;
                return;
            }
            if (sq.MarchIndex >= sq.MarchPath.Count)
            {
                if (sq.Task == SquadTask.Gather)
                {
                    sq.Arrived = true;
                    sq.Anchor = sq.MarchPoint;
                }
                return;
            }
            Vec2 wp = sq.MarchPath[sq.MarchIndex];
            float speed = sq.MarchSpeed * (sq.Task == SquadTask.FallBack ? 1.05f : 0.95f);
            Vec2 before = sq.MarchPoint;
            sq.MarchPoint = Vec2.MoveTowards(sq.MarchPoint, wp, speed * dt);
            Vec2 moved = sq.MarchPoint - before;
            if (moved.LengthSq > 1e-5f)
            {
                float a = KMath.RotateTowards(sq.MarchHeading.ToAngle(), moved.ToAngle(), 3f * dt);
                sq.MarchHeading = Vec2.FromAngle(a);
                if (!sq.InCombat) sq.Forward = sq.MarchHeading;
            }
            if (Vec2.DistanceSq(sq.MarchPoint, wp) < 0.04f) sq.MarchIndex++;
        }
    }
}
