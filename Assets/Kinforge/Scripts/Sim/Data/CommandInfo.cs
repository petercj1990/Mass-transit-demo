namespace Kinforge.Sim
{
    /// <summary>Player-facing text for each combat command, taken from the design doc's command table.</summary>
    public sealed class CommandInfo
    {
        public CommandType Type;
        public string Name;
        public string Hotkey;
        public string Does;
        public string Warwright;
        public string Siegewright;
        public string StrongAgainst;
        public string Risk;

        public static readonly CommandType[] Playable =
        {
            CommandType.Charge, CommandType.DefensiveFormation, CommandType.HoldPosition, CommandType.Kite,
            CommandType.FocusFire, CommandType.SpreadOut, CommandType.Stack, CommandType.FallBack,
            CommandType.Fortify, CommandType.Rest,
        };

        public const int Count = 10;

        private static readonly CommandInfo[] All = new CommandInfo[Count];

        public static CommandInfo Get(CommandType t) => All[(int)t];

        static CommandInfo()
        {
            Add(CommandType.Charge, "Charge", "Z", "Close distance fast, open with burst abilities",
                "Seals escape routes behind the target", "Drops a bolt turret to cover the push",
                "Ranged enemies, fleeing targets", "Over-extending, pulling extra enemies");
            Add(CommandType.DefensiveFormation, "Defensive Formation", "X", "Tank in front, healer sheltered, DPS behind",
                "Raises ramparts on the flanks", "Sets turrets inside the ramparts",
                "Melee waves, guarding a point", "Low damage; slow to react");
            Add(CommandType.HoldPosition, "Hold Position", "C", "Fight from current spot, never chase",
                "Lays spike fields and a wall in front", "Places a mortar pit behind the line",
                "Chokepoints, guarding gatherers", "Out-ranged enemies pick them apart");
            Add(CommandType.Kite, "Kite", "V", "Keep distance and fire while retreating; tank drags enemies in circles",
                "Drops spikes along the retreat path", "Leaves turrets along the route",
                "Slow melee brutes", "Fails in tight terrain");
            Add(CommandType.FocusFire, "Focus Fire", "B", "Both DPS on one target, chosen by you (right-click) or the AI",
                "Cages the target so it can't escape", "Turns every siege work onto the target",
                "Enemy healers, casters, elites", "Ignores other incoming threats");
            Add(CommandType.SpreadOut, "Spread Out", "N", "Keep apart to limit area damage",
                "Raises separate cover for each unit", "Spreads turrets to cover each unit",
                "Charges, bombs, breath attacks", "Harder to heal, line of sight breaks");
            Add(CommandType.Stack, "Stack", "M", "Cluster tight for group heals and shields",
                "Raises a ring wall around the squad", "Places one turret in the centre",
                "Burst damage phases", "Area attacks punish it");
            Add(CommandType.FallBack, "Fall Back", "G", "Disengage all the way to base, tank covering the retreat",
                "Collapses the path behind the squad", "Leaves a turret behind to slow pursuers",
                "Losing fights", "Units can be downed while retreating");
            Add(CommandType.Rest, "Rest", "R", "Out of combat only: sit, recover, clear Injured status",
                "Raises a low wall around the camp", "Keeps turrets standing guard",
                "After fights", "Vulnerable; interrupted by any attack");
            Add(CommandType.Fortify, "Fortify", "T", "Dig in at a spot before enemies arrive",
                "Builds a field fort: walls with a gap", "Mounts turrets inside the fort",
                "Waves, defending gather sites", "Takes time; enemies may go around");
        }

        private static void Add(CommandType t, string name, string key, string does, string ww, string sw, string strong, string risk)
        {
            All[(int)t] = new CommandInfo
            {
                Type = t, Name = name, Hotkey = key, Does = does, Warwright = ww, Siegewright = sw, StrongAgainst = strong, Risk = risk,
            };
        }
    }
}
