namespace Kinforge.Sim
{
    public static class Spawner
    {
        public static Unit SpawnEnemy(World w, UnitKind kind, Vec2 pos, EnemyGroup g)
        {
            var def = UnitDefs.Get(kind);
            var u = new Unit
            {
                Def = def,
                Faction = Faction.Enemy,
                Pos = w.Nav.NearestWalkable(pos, 6),
                Radius = def.Radius,
                Name = def.Name,
                Group = g,
                Threat = new ThreatTable(),
                ThinkPhase = w.Rng.Range(0, Balance.ThinkInterval),
                Facing = w.Rng.Range(-KMath.Pi, KMath.Pi),
                HeightOffset = def.Flying ? 1.4f : 0f,
                SpawnedAt = w.Time,
            };
            u.Abilities = new AbilitySlot[def.Kit.Length];
            for (int i = 0; i < def.Kit.Length; i++)
            {
                u.Abilities[i] = new AbilitySlot { Def = AbilityDefs.Get(def.Kit[i]), Unlocked = true, Cooldown = w.Rng.Range(0f, 3f) };
            }
            u.Hp = u.MaxHp;
            w.Register(u);
            g?.Members.Add(u);
            return u;
        }

        public static EnemyGroup CreateGroup(World w, string name, Vec2 home, int tier, bool isCamp)
        {
            var g = new EnemyGroup { Id = w.NextId(), Name = name, Home = home, Tier = tier, IsCamp = isCamp, SpawnedAt = w.Time };
            w.Groups.Add(g);
            return g;
        }

        /// <summary>Spawns (or respawns) every unit in a group's spec around its home.</summary>
        public static void Populate(World w, EnemyGroup g)
        {
            g.Members.RemoveAll(m => m.Life != LifeState.Active || m.Removed);
            for (int i = 0; i < g.Spec.Count; i++)
            {
                Vec2 p = g.Home + Vec2.FromAngle(i * 2.4f + g.Id) * (1.5f + i * 0.6f);
                SpawnEnemy(w, g.Spec[i], p, g);
            }
            g.State = GroupState.Idle;
            g.ClearedAt = -1f;
            g.SpawnedAt = w.Time;
        }
    }
}
