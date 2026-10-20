namespace Kinforge.Sim
{
    /// <summary>
    /// Small hand-built worlds for tests, the balance lab and the in-game proving ground:
    /// flat ground, no camps, no director, so a single fight can be studied in isolation.
    /// </summary>
    public static class Scenarios
    {
        public static World Blank(int seed)
        {
            var w = new World(seed);
            w.GroundNoise = new Noise((uint)seed);
            w.BasePos = new Vec2(12f, 12f);
            w.Base = new PlayerBase { Pos = w.BasePos };
            w.ArenaCenter = new Vec2(-500f, -500f);
            w.ArenaRadius = 1f;
            w.RevealMap = true;
            return w;
        }

        public static Squad AddSquad(World w, Vec2 at, int doctrineTier, CommandType stance, int gearTier = 1)
        {
            var sq = PlayerUnits.CreateSquad(w, at, doctrineTier);
            sq.Stance = stance;
            sq.PrevStance = stance;
            sq.Anchor = at;
            sq.CombatAnchor = at;
            sq.MarchPoint = at;
            if (gearTier > 1)
            {
                sq.WeaponTier = gearTier;
                sq.ArmorTier = gearTier;
                foreach (var m in sq.Members)
                {
                    m.WeaponTier = gearTier;
                    m.ArmorTier = gearTier;
                    m.Hp = m.MaxHp;
                }
            }
            return sq;
        }

        public static EnemyGroup AddPack(World w, Vec2 home, params UnitKind[] kinds)
        {
            var g = Spawner.CreateGroup(w, "Test pack", home, 1, true);
            g.Spec.AddRange(kinds);
            g.LeashRadius = 60f;
            Spawner.Populate(w, g);
            return g;
        }

        /// <summary>A flat arena with Rustmaw in the middle and the hatch already open.</summary>
        public static World BossArena(int seed)
        {
            var w = Blank(seed);
            w.ArenaCenter = new Vec2(60f, 60f);
            w.ArenaRadius = 22f;
            var g = Spawner.CreateGroup(w, "Breach Hold", w.ArenaCenter, 4, false);
            g.IsBossGroup = true;
            var body = Spawner.SpawnEnemy(w, UnitKind.Rustmaw, w.ArenaCenter, g);
            w.Boss = new RustmawBoss { Body = body, Group = g, Center = w.ArenaCenter, ArenaRadius = w.ArenaRadius, GateOpen = true };
            return w;
        }
    }
}
