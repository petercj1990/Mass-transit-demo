namespace Kinforge.Sim
{
    /// <summary>Applies the commander's queued orders at the start of a tick.</summary>
    public static class Orders
    {
        public static void Process(World w)
        {
            if (w.PendingOrders.Count == 0) return;
            var orders = w.PendingOrders.ToArray();
            w.PendingOrders.Clear();
            foreach (var o in orders) Apply(w, o);
        }

        private static void Apply(World w, PlayerOrder o)
        {
            switch (o.Type)
            {
                case OrderType.Recruit:
                case OrderType.UpgradeWarHall:
                case OrderType.CraftWeapons:
                case OrderType.CraftArmor:
                case OrderType.CraftSigil:
                case OrderType.RecallCores:
                    w.Base?.HandleOrder(w, o);
                    return;
                case OrderType.DebugDrill:
                    foreach (int id in o.Squads)
                    {
                        var sq = w.FindSquad(id);
                        if (sq != null) PlayerUnits.DebugDrill(w, sq, o.Arg);
                    }
                    return;
                case OrderType.LockInstinct:
                {
                    var u = w.FindUnitAny(o.TargetId);
                    if (u?.Instinct != null) u.Instinct.Locked = o.Arg != 0;
                    return;
                }
                case OrderType.ResetInstinct:
                {
                    var u = w.FindUnitAny(o.TargetId);
                    u?.Instinct?.ResetCommand(o.Command);
                    return;
                }
            }

            int index = 0;
            int count = o.Squads.Count;
            foreach (int id in o.Squads)
            {
                var sq = w.FindSquad(id);
                if (sq == null || sq.Wiped) continue;
                ApplyToSquad(w, sq, o, index++, count);
            }
        }

        private static void ApplyToSquad(World w, Squad sq, PlayerOrder o, int index, int count)
        {
            if (o.Type != OrderType.SetCommand && o.Type != OrderType.Garrison && sq.Garrisoned) SquadSystem.Ungarrison(w, sq);

            switch (o.Type)
            {
                case OrderType.Move:
                {
                    Vec2 pos = o.Pos;
                    if (count > 1)
                    {
                        // Several squads: spread them across the click point.
                        Vec2 dir = (o.Pos - sq.Centroid).Normalized;
                        if (dir == Vec2.Zero) dir = Vec2.Forward;
                        pos += dir.Right * ((index - (count - 1) * 0.5f) * 9f);
                    }
                    LeaveSpecialStances(sq);
                    sq.Task = SquadTask.Move;
                    sq.TaskPos = w.Nav.NearestWalkable(pos);
                    sq.Forced = sq.InCombat;
                    sq.FocusTargetId = 0;
                    SquadSystem.BeginTravel(w, sq);
                    break;
                }
                case OrderType.Attack:
                    LeaveSpecialStances(sq);
                    sq.Task = SquadTask.Attack;
                    sq.TaskTargetId = o.TargetId;
                    sq.FocusTargetId = o.TargetId;
                    sq.Forced = false;
                    SquadSystem.BeginTravel(w, sq);
                    break;
                case OrderType.Gather:
                    LeaveSpecialStances(sq);
                    sq.Task = SquadTask.Gather;
                    sq.TaskTargetId = o.TargetId;
                    sq.Forced = false;
                    SquadSystem.BeginTravel(w, sq);
                    break;
                case OrderType.ReturnToBase:
                    LeaveSpecialStances(sq);
                    sq.Task = SquadTask.Return;
                    sq.ResumeTask = SquadTask.Idle;
                    sq.Forced = sq.InCombat;
                    SquadSystem.BeginTravel(w, sq);
                    break;
                case OrderType.Garrison:
                    if (sq.Garrisoned) break;
                    LeaveSpecialStances(sq);
                    sq.Task = SquadTask.Garrison;
                    sq.Forced = sq.InCombat;
                    SquadSystem.BeginTravel(w, sq);
                    break;
                case OrderType.Recover:
                    LeaveSpecialStances(sq);
                    sq.Task = SquadTask.Recover;
                    sq.TaskTargetId = o.TargetId;
                    sq.Forced = false;
                    SquadSystem.BeginTravel(w, sq);
                    break;
                case OrderType.Stop:
                    LeaveSpecialStances(sq);
                    sq.Task = SquadTask.Idle;
                    sq.Anchor = sq.Centroid;
                    sq.Forced = false;
                    break;
                case OrderType.SetCommand:
                    SetStance(w, sq, o.Command);
                    break;
            }
        }

        private static void LeaveSpecialStances(Squad sq)
        {
            if (sq.Stance == CommandType.FallBack || sq.Stance == CommandType.Rest)
            {
                sq.Stance = sq.PrevStance == CommandType.FallBack || sq.PrevStance == CommandType.Rest ? CommandType.DefensiveFormation : sq.PrevStance;
            }
        }

        public static void SetStance(World w, Squad sq, CommandType cmd)
        {
            if (cmd == CommandType.Rest && sq.InCombat)
            {
                w.Alert($"{sq.Name} can't rest while in combat.", 1);
                return;
            }
            if (sq.Garrisoned && cmd != CommandType.Rest)
            {
                // Garrisoned squads keep their posture for when they come out.
                if (cmd != CommandType.FallBack) sq.Stance = cmd;
                return;
            }
            if (sq.Stance != CommandType.FallBack && sq.Stance != CommandType.Rest && sq.Stance != cmd) sq.PrevStance = sq.Stance;
            sq.Stance = cmd;
            sq.StanceSince = w.Time;
            switch (cmd)
            {
                case CommandType.FallBack:
                    sq.Task = SquadTask.FallBack;
                    sq.TaskPos = sq.RallyPoint;
                    sq.FocusTargetId = 0;
                    SquadSystem.BeginTravel(w, sq);
                    break;
                case CommandType.HoldPosition:
                case CommandType.Fortify:
                case CommandType.Rest:
                    if (sq.Task == SquadTask.Move || sq.Task == SquadTask.Idle || sq.Task == SquadTask.FallBack)
                    {
                        sq.Task = SquadTask.Idle;
                        sq.Anchor = sq.Centroid;
                        sq.CombatAnchor = sq.Anchor;
                    }
                    break;
            }
        }
    }
}
