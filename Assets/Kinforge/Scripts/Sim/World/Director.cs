using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>World pressure: telegraphed meteor strikes, camp respawns and hunting war parties.</summary>
    public sealed class Director
    {
        private float _nextMeteor;
        private Telegraph _meteor;
        private float _nextWarParty;
        private int _warParties;
        private readonly List<Unit> _hits = new List<Unit>();

        public float NextMeteorAt => _nextMeteor;
        public float NextWarPartyAt => _nextWarParty;

        public Director(World w)
        {
            _nextMeteor = w.Time + 60f;
            _nextWarParty = Balance.WarPartyFirstAt;
        }

        public void Update(World w)
        {
            UpdateMeteor(w);
            if (w.Tick % 40 == 0) RespawnCamps(w);
            if (w.Time >= _nextWarParty && !w.Victory) SpawnWarParty(w);
        }

        private void UpdateMeteor(World w)
        {
            if (_meteor != null)
            {
                if (_meteor.Remaining > 0f) return;
                Impact(w, _meteor.Pos);
                _meteor = null;
                return;
            }
            if (w.Time < _nextMeteor) return;
            _nextMeteor = w.Time + w.Rng.Range(Balance.MeteorMinInterval, Balance.MeteorMaxInterval);

            Vec2 p = Vec2.Zero;
            bool found = false;
            if (w.Rng.Chance(0.5f))
            {
                var candidates = new List<Squad>();
                foreach (var s in w.Squads)
                {
                    if (s.ActiveCount > 0 && !s.Garrisoned && !w.InBase(s.Centroid)) candidates.Add(s);
                }
                if (candidates.Count > 0)
                {
                    var s = w.Rng.Pick(candidates);
                    p = s.Centroid + w.Rng.OnCircle(w.Rng.Range(4f, 12f));
                    found = true;
                }
            }
            for (int tries = 0; !found && tries < 20; tries++)
            {
                var c = w.Rng.Pick(w.Craters);
                p = c.Center + w.Rng.InsideCircle(c.Radius * 0.6f);
                found = Vec2.Distance(p, w.BasePos) > 35f && !w.InArena(p, 8f);
            }
            if (!found || w.InBase(p) || w.InArena(p, 6f)) return;
            _meteor = w.AddTelegraph(new Telegraph
            {
                Shape = TelegraphShape.Circle, Pos = p, Radius = Balance.MeteorRadius, Duration = Balance.MeteorWarning,
                Threatens = Faction.Player, Label = "Meteor Strike",
            });
            if (w.IsVisibleToPlayer(p)) w.Alert("Meteor incoming! Get clear of the marked ground.", p, 2);
        }

        private void Impact(World w, Vec2 p)
        {
            w.Emit(SimEventType.Meteor, 0, 0, p, Balance.MeteorRadius);
            foreach (var u in w.Units.ToArray())
            {
                if (!u.IsTargetable) continue;
                float d = Vec2.Distance(u.Pos, p);
                if (d > Balance.MeteorRadius + u.Radius) continue;
                Combat.Deal(w, null, u, Balance.MeteorDamage, DamageType.Kinetic, HitFlags.Environment);
                Combat.Knockback(w, u, u.Pos - p, 3f);
            }
            // Fresh ferrite from the impact.
            var node = new ResourceNode
            {
                Id = w.NextId(), Kind = ResourceKind.Ferrite, Pos = w.Nav.NearestWalkable(p, 4), Radius = 0.9f,
                Amount = 30f, MaxAmount = 30f, Label = "Meteor ferrite", Variant = 1,
            };
            node.Cells = w.Nav.RasterCircle(node.Pos, 0.7f);
            w.Nav.Stamp(node.Cells, true, false, 0, +1);
            w.Nodes.Add(node);
        }

        private void RespawnCamps(World w)
        {
            foreach (var g in w.Groups)
            {
                if (!g.IsCamp || g.IsBossGroup || g.ClearedAt < 0f) continue;
                if (w.Time - g.ClearedAt < Balance.CampRespawnSeconds) continue;
                if (w.NearestUnit(g.Home, Faction.Player, 45f) != null) continue;
                Spawner.Populate(w, g);
            }
        }

        private void SpawnWarParty(World w)
        {
            _nextWarParty = w.Time + Balance.WarPartyInterval + w.Rng.Range(-60f, 60f);
            Squad target = null;
            float best = -1f;
            foreach (var s in w.Squads)
            {
                if (s.Wiped || s.Garrisoned || s.ActiveCount == 0) continue;
                Vec2 c = s.Centroid;
                if (w.InBase(c)) continue;
                float d = Vec2.Distance(c, w.BasePos);
                if (d > best)
                {
                    best = d;
                    target = s;
                }
            }
            if (target == null) return; // nobody in the field; try again next time

            _warParties++;
            Vec2 c2 = target.Centroid;
            Vec2 from = (c2 - w.BasePos).Normalized;
            if (from == Vec2.Zero) from = Vec2.Forward;
            Vec2 spawn = c2 + from.Rotate(w.Rng.Range(-0.8f, 0.8f)) * 40f;
            spawn = new Vec2(KMath.Clamp(spawn.X, 6f, Balance.MapSize - 6f), KMath.Clamp(spawn.Z, 6f, Balance.MapSize - 6f));
            var g = Spawner.CreateGroup(w, "War party", spawn, 1 + _warParties / 2, false);
            g.IsWarParty = true;
            g.State = GroupState.Hunting;
            g.HuntSquadId = target.Id;
            int n = 3 + _warParties / 2;
            for (int i = 0; i < n; i++)
            {
                var kind = (i == n - 1 && _warParties >= 3) ? UnitKind.Rockback : UnitKind.Cratermaw;
                Spawner.SpawnEnemy(w, kind, spawn + Vec2.FromAngle(i * 1.7f) * 2f, g);
            }
            w.Alert($"A Cratermaw war party is hunting {target.Name}!", spawn, 2);
        }
    }
}
