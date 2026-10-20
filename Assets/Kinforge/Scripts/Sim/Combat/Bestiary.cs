using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// Shared knowledge of enemy resistances. Entries start unknown and fill in as squads land
    /// hits of each damage type. Units at Doctrine tier 3+ use known weaknesses to pick targets.
    /// </summary>
    public sealed class Bestiary
    {
        private readonly Dictionary<EnemyFamily, bool[]> _known = new Dictionary<EnemyFamily, bool[]>();
        private readonly Dictionary<EnemyFamily, int> _kills = new Dictionary<EnemyFamily, int>();
        public int Version { get; private set; }

        public bool Knows(EnemyFamily f, DamageType t) => _known.TryGetValue(f, out var k) && k[(int)t];

        public bool Discover(EnemyFamily f, DamageType t)
        {
            if (f == EnemyFamily.None) return false;
            if (!_known.TryGetValue(f, out var k))
            {
                k = new bool[6];
                _known[f] = k;
            }
            if (k[(int)t]) return false;
            k[(int)t] = true;
            Version++;
            return true;
        }

        public void AddKill(EnemyFamily f)
        {
            _kills.TryGetValue(f, out int n);
            _kills[f] = n + 1;
            Version++;
        }

        public int Kills(EnemyFamily f) => _kills.TryGetValue(f, out int n) ? n : 0;

        public bool Seen(EnemyFamily f) => _known.ContainsKey(f) || _kills.ContainsKey(f);

        /// <summary>Known multiplier for a family, or 1 if undiscovered (units can't exploit what they don't know).</summary>
        public float KnownMultiplier(EnemyFamily f, DamageType t, UnitDef sample)
        {
            if (!Knows(f, t) || sample == null) return 1f;
            return sample.ResistTo(t);
        }
    }
}
