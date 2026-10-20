using System;

namespace Kinforge.Sim
{
    /// <summary>
    /// Fog of war on a 2 m grid: 0 = never seen, 1 = explored (terrain stays mapped), 2 = in sight now.
    /// Enemies only show while their cell is in sight, as the design doc specifies.
    /// </summary>
    public sealed class Visibility
    {
        public const float CellSize = 2f;
        public readonly int Cells;
        public readonly byte[] State;
        public int Version { get; private set; }

        public Visibility(int mapSize)
        {
            Cells = (int)(mapSize / CellSize);
            State = new byte[Cells * Cells];
        }

        public void BeginFrame()
        {
            for (int i = 0; i < State.Length; i++)
            {
                if (State[i] == 2) State[i] = 1;
            }
        }

        public void Reveal(Vec2 center, float radius)
        {
            int cx = (int)(center.X / CellSize);
            int cz = (int)(center.Z / CellSize);
            int r = (int)MathF.Ceiling(radius / CellSize);
            float rsq = (radius / CellSize) * (radius / CellSize);
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (z < 0 || z >= Cells) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= Cells) continue;
                    float dx = x + 0.5f - center.X / CellSize;
                    float dz = z + 0.5f - center.Z / CellSize;
                    if (dx * dx + dz * dz <= rsq) State[z * Cells + x] = 2;
                }
            }
        }

        public void EndFrame() => Version++;

        public byte At(Vec2 p)
        {
            int x = (int)(p.X / CellSize);
            int z = (int)(p.Z / CellSize);
            if (x < 0 || z < 0 || x >= Cells || z >= Cells) return 0;
            return State[z * Cells + x];
        }

        public bool IsVisible(Vec2 p) => At(p) == 2;
        public bool IsExplored(Vec2 p) => At(p) >= 1;

        public void RevealAll()
        {
            for (int i = 0; i < State.Length; i++) State[i] = 2;
            Version++;
        }
    }
}
