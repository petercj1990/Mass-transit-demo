using System.Collections.Generic;

namespace Kinforge.Sim
{
    public static class PlayerUnits
    {
        private static readonly string[] VhornNames =
        {
            "Ghar", "Kesh", "Vorna", "Tusk", "Brakka", "Orsk", "Ulra", "Drogh", "Marr", "Skarn", "Hesk", "Vhal", "Rokka",
            "Zhor", "Narga", "Thrask", "Ivra", "Gorm", "Yska", "Bront", "Khel", "Asha", "Durn", "Vekka", "Morg", "Ruun",
            "Tova", "Grisk", "Ondra", "Halk", "Sorra", "Brem", "Ketta", "Wulgar", "Isk", "Rhona", "Dask", "Ugra",
        };

        public static readonly string[] SquadNames = { "Ember Kin", "Iron Tusk", "Ash Horn", "Rust Oath", "Cinder Band", "Stone Vow" };

        public static Squad CreateSquad(World w, Vec2 at, int doctrineTier = 0)
        {
            int index = w.Squads.Count;
            var sq = new Squad
            {
                Id = w.NextId(),
                Name = SquadNames[index % SquadNames.Length],
                ColorIndex = index,
                Anchor = at,
                CombatAnchor = at,
                MarchPoint = at,
                RallyPoint = w.BasePos,
                Forward = (at - w.BasePos).LengthSq > 1f ? (at - w.BasePos).Normalized : Vec2.Forward,
            };
            sq.MarchHeading = sq.Forward;
            for (int i = 0; i < UnitDefs.SquadRoles.Length; i++)
            {
                Vec2 offset = FormationOffset(UnitDefs.SquadRoles[i], sq.Forward, 1f);
                var u = CreateUnit(w, UnitDefs.SquadRoles[i], sq, w.Nav.NearestWalkable(at + offset));
                if (doctrineTier > 0) u.Doctrine.SeedTier(doctrineTier);
                RefreshAbilitySlots(u);
            }
            w.Squads.Add(sq);
            return sq;
        }

        public static Unit CreateUnit(World w, UnitKind kind, Squad sq, Vec2 pos)
        {
            var def = UnitDefs.Get(kind);
            var u = new Unit
            {
                Def = def,
                Faction = Faction.Player,
                Pos = pos,
                Radius = def.Radius,
                Name = PickName(w),
                Squad = sq,
                Facing = sq != null ? sq.Forward.ToAngle() : 0f,
                Doctrine = new Doctrine(),
                Instinct = new Instinct(def.Role),
                Carry = new Stockpile(),
                ThinkPhase = w.Rng.Range(0, Balance.ThinkInterval),
                WeaponTier = sq?.WeaponTier ?? 1,
                ArmorTier = sq?.ArmorTier ?? 1,
                SpawnedAt = w.Time,
            };
            u.Abilities = new AbilitySlot[def.Kit.Length];
            for (int i = 0; i < def.Kit.Length; i++)
            {
                u.Abilities[i] = new AbilitySlot { Def = AbilityDefs.Get(def.Kit[i]), Unlocked = i < 2 };
            }
            u.Hp = u.MaxHp;
            w.Register(u);
            sq?.Members.Add(u);
            return u;
        }

        private static string PickName(World w)
        {
            var used = new HashSet<string>();
            foreach (var u in w.Units)
            {
                if (u.IsPlayer && u.Name != null) used.Add(u.Name);
            }
            for (int tries = 0; tries < 40; tries++)
            {
                string n = VhornNames[w.Rng.Range(0, VhornNames.Length)];
                if (!used.Contains(n)) return n;
            }
            return VhornNames[w.Rng.Range(0, VhornNames.Length)] + " " + (char)('A' + w.Rng.Range(0, 26));
        }

        /// <summary>Opens ability slot 3 once a unit reaches Doctrine tier 2 in any command.</summary>
        public static bool RefreshAbilitySlots(Unit u)
        {
            if (u.Abilities == null || u.Abilities.Length < 3 || u.Doctrine == null) return false;
            if (u.Abilities[2].Unlocked) return false;
            if (u.Doctrine.MaxTier < Balance.ThirdAbilityTier) return false;
            u.Abilities[2].Unlocked = true;
            return true;
        }

        /// <summary>Marching / idle formation: tank leads, healer and Siegewright behind.</summary>
        public static Vec2 FormationOffset(UnitKind kind, Vec2 forward, float spacing)
        {
            Vec2 f = forward.LengthSq > 0.001f ? forward.Normalized : Vec2.Forward;
            Vec2 r = f.Right;
            switch (kind)
            {
                case UnitKind.Shieldbearer: return f * (2f * spacing);
                case UnitKind.Bruiser: return (f * 0.9f + r * 1.7f) * spacing;
                case UnitKind.Warwright: return (f * 0.9f - r * 1.7f) * spacing;
                case UnitKind.Siegewright: return (-f * 1.5f + r * 1.3f) * spacing;
                case UnitKind.Mender: return (-f * 1.7f - r * 1.2f) * spacing;
            }
            return Vec2.Zero;
        }

        public static int RoleIndex(Role r)
        {
            switch (r)
            {
                case Role.Tank: return 0;
                case Role.Healer: return 1;
                case Role.Melee: return 2;
                case Role.Siege: return 3;
                case Role.Warwright: return 4;
            }
            return 0;
        }
    }
}
