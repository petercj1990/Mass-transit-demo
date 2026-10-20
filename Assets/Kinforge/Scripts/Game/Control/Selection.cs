using System.Collections.Generic;

namespace Kinforge.Game
{
    /// <summary>What the commander has selected: squads to command, and one unit or enemy to inspect.</summary>
    public sealed class Selection
    {
        public readonly List<int> Squads = new List<int>();
        public int InspectUnitId;

        public bool IsSelected(int squadId) => Squads.Contains(squadId);

        public void Clear()
        {
            Squads.Clear();
            InspectUnitId = 0;
        }

        public void SelectOnly(int squadId)
        {
            Squads.Clear();
            Squads.Add(squadId);
        }

        public void Toggle(int squadId)
        {
            if (!Squads.Remove(squadId)) Squads.Add(squadId);
        }

        public void Add(int squadId)
        {
            if (!Squads.Contains(squadId)) Squads.Add(squadId);
        }
    }
}
