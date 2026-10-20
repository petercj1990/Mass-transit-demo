namespace Kinforge.Sim
{
    /// <summary>
    /// Five-tier mastery track per combat command. XP earned since the last base visit is
    /// "unbanked" and is lost if the unit is killed (the design doc's proposed death cost).
    /// </summary>
    public sealed class Doctrine
    {
        public readonly float[] Banked = new float[CommandInfo.Count];
        public readonly float[] Unbanked = new float[CommandInfo.Count];

        public float Total(CommandType c) => Banked[(int)c] + Unbanked[(int)c];

        public int Tier(CommandType c) => TierForXp(Total(c));

        public static int TierForXp(float xp)
        {
            var t = Balance.DoctrineTierXp;
            for (int i = t.Length - 1; i >= 1; i--)
            {
                if (xp >= t[i]) return i;
            }
            return 0;
        }

        /// <summary>Progress from the current tier to the next, 0..1 (1 at tier 5).</summary>
        public float Progress(CommandType c)
        {
            int tier = Tier(c);
            var t = Balance.DoctrineTierXp;
            if (tier >= t.Length - 1) return 1f;
            return KMath.InverseLerp(t[tier], t[tier + 1], Total(c));
        }

        public int MaxTier
        {
            get
            {
                int m = 0;
                for (int i = 0; i < CommandInfo.Count; i++)
                {
                    int t = Tier((CommandType)i);
                    if (t > m) m = t;
                }
                return m;
            }
        }

        public int TierSum
        {
            get
            {
                int s = 0;
                for (int i = 0; i < CommandInfo.Count; i++) s += Tier((CommandType)i);
                return s;
            }
        }

        public float UnbankedTotal
        {
            get
            {
                float s = 0f;
                foreach (float u in Unbanked) s += u;
                return s;
            }
        }

        /// <summary>Adds XP and returns the new tier if it went up, otherwise -1.</summary>
        public int AddXp(CommandType c, float amount)
        {
            int before = Tier(c);
            Unbanked[(int)c] += amount;
            int after = Tier(c);
            return after > before ? after : -1;
        }

        public void Bank()
        {
            for (int i = 0; i < Banked.Length; i++)
            {
                Banked[i] += Unbanked[i];
                Unbanked[i] = 0f;
            }
        }

        public float LoseUnbanked()
        {
            float lost = UnbankedTotal;
            for (int i = 0; i < Unbanked.Length; i++) Unbanked[i] = 0f;
            return lost;
        }

        /// <summary>Drill-ground style seeding: sets every command to at least the given tier.</summary>
        public void SeedTier(int tier)
        {
            float xp = Balance.DoctrineTierXp[KMath.Clamp(tier, 0, Balance.DoctrineTierXp.Length - 1)];
            for (int i = 0; i < Banked.Length; i++)
            {
                if (Banked[i] < xp) Banked[i] = xp;
            }
        }
    }
}
