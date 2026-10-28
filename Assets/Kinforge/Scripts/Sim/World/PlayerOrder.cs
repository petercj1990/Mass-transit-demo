using System.Collections.Generic;

namespace Kinforge.Sim
{
    public enum OrderType : byte
    {
        Move,
        Attack,
        Gather,
        ReturnToBase,
        Garrison,
        Recover,
        SetCommand,
        Stop,
        // Base actions (no squads needed)
        Recruit,
        UpgradeWarHall,
        CraftWeapons,
        CraftArmor,
        CraftSigil,
        RecallCores,
        // Unit-level learning controls
        LockInstinct,
        ResetInstinct,
        // Playtest only: drill the squads to Doctrine tier Arg (F7)
        DebugDrill,
        // Direct control: steer the squad's banner this tick (Pos = direction, length 0..1)
        Steer,
    }

    public sealed class PlayerOrder
    {
        public OrderType Type;
        public readonly List<int> Squads = new List<int>();
        public Vec2 Pos;
        public int TargetId;
        public CommandType Command;
        public int Arg;

        public static PlayerOrder For(OrderType type, IEnumerable<int> squads)
        {
            var o = new PlayerOrder { Type = type };
            if (squads != null) o.Squads.AddRange(squads);
            return o;
        }
    }
}
