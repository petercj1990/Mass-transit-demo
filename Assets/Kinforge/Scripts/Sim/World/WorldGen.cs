using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// Seeded generation of the Craters biome: a grey-green plain pocked with craters, the crash-site
    /// base in the south, Breach Hold (Rustmaw's crater) in the north, ferrite in crater beds, ark
    /// wreckage, and enemy camps that get tougher the further they are from home. As in the design doc,
    /// the open land changes every seed while the set piece (the raid arena) keeps a hand-built shape.
    /// </summary>
    public static class WorldGen
    {
        public static void Generate(World w)
        {
            var rng = new SimRng((ulong)w.Seed * 31UL + 7UL);
            var noise = new Noise((uint)w.Seed);
            w.GroundNoise = new Noise((uint)w.Seed + 101u);
            int size = Balance.MapSize;

            w.BasePos = new Vec2(size * 0.5f, 34f);
            w.ArenaCenter = new Vec2(size * 0.5f + rng.Range(-25f, 25f), 198f);
            w.ArenaRadius = 22f;

            // --- Craters --------------------------------------------------------------------
            w.Craters.Add(new Crater { Center = w.ArenaCenter, Radius = 28f, Depth = 3.2f, Rim = 2.4f, FlatFloor = true });
            int attempts = 0;
            while (w.Craters.Count < 19 && attempts++ < 600)
            {
                float r = rng.Range(6f, 15f);
                var c = new Vec2(rng.Range(15f, size - 15f), rng.Range(15f, size - 15f));
                if (Vec2.Distance(c, w.BasePos) < 30f + r) continue;
                if (Vec2.Distance(c, w.ArenaCenter) < 34f + r) continue;
                bool overlap = false;
                foreach (var o in w.Craters)
                {
                    if (Vec2.Distance(o.Center, c) < (o.Radius + r) * 1.1f) overlap = true;
                }
                if (overlap) continue;
                w.Craters.Add(new Crater { Center = c, Radius = r, Depth = r * 0.22f, Rim = r * 0.11f });
            }

            // --- Heights --------------------------------------------------------------------
            var h = w.Height;
            for (int z = 0; z < h.Verts; z++)
            for (int x = 0; x < h.Verts; x++)
            {
                var p = new Vec2(x, z);
                float height = (noise.Fbm(x * 0.011f, z * 0.011f, 4) - 0.5f) * 8f;
                height += (noise.Fbm(x * 0.05f + 40f, z * 0.05f, 2) - 0.5f) * 1.2f;
                foreach (var c in w.Craters) height += CraterProfile(c, p);
                float db = Vec2.Distance(p, w.BasePos);
                float baseBlend = KMath.SmoothStep(30f, 18f, db);
                height = KMath.Lerp(height, 0.4f, baseBlend);
                h.Set(x, z, height);
            }

            // --- Base -----------------------------------------------------------------------
            w.Base = new PlayerBase { Pos = w.BasePos };
            PlaceBuilding(w, BuildingKind.KinCradle, 70f, 11.5f, 3.4f, 2.6f);
            PlaceBuilding(w, BuildingKind.WarHall, 107f, 12f, 4f, 2.8f);
            PlaceBuilding(w, BuildingKind.Smelter, 143f, 11.5f, 2.8f, 2.6f);
            PlaceBuilding(w, BuildingKind.Stockhouse, 180f, 11f, 3.6f, 2.4f);
            PlaceBuilding(w, BuildingKind.SalvageBench, 217f, 11.5f, 2.8f, 2.2f);
            PlaceBuilding(w, BuildingKind.SiegeYard, 253f, 12f, 3.4f, 2.6f);
            PlaceBuilding(w, BuildingKind.PowerHub, 290f, 11.5f, 2.2f, 2.2f);

            // --- Breach Hold: a ring of ark hull plates around the arena with one sealed hatch.
            float ringR = w.ArenaRadius + 2.5f;
            Vec2 gateDir = (w.BasePos - w.ArenaCenter).Normalized;
            float gateAngle = gateDir.ToAngle();
            int segments = 22;
            for (int i = 0; i < segments; i++)
            {
                float a = i * KMath.TwoPi / segments;
                if (Math.Abs(KMath.AngleDelta(a, gateAngle)) < 0.22f) continue;
                Vec2 pos = w.ArenaCenter + Vec2.FromAngle(a) * ringR;
                Vec2 tangent = Vec2.FromAngle(a).Right;
                float halfLen = KMath.TwoPi * ringR / segments * 0.55f;
                AddWreck(w, pos, tangent, halfLen, 0.9f, rng.Range(2.6f, 4.2f), PropKind.Wreck, rng.Range(0, 4));
            }
            w.ArenaGate = w.ArenaCenter + gateDir * ringR;
            var gate = StructureSystem.SpawnHatch(w, w.ArenaGate, gateDir.Right, 4.2f);
            var bossGroup = Spawner.CreateGroup(w, "Breach Hold", w.ArenaCenter, 4, false);
            bossGroup.IsBossGroup = true;
            var body = Spawner.SpawnEnemy(w, UnitKind.Rustmaw, w.ArenaCenter, bossGroup);
            body.Facing = gateAngle;
            w.Boss = new RustmawBoss { Body = body, Group = bossGroup, Center = w.ArenaCenter, ArenaRadius = w.ArenaRadius, Gate = gate };

            // --- Ark wreckage sites (in the larger craters where possible) -----------------------
            var big = new List<Crater>();
            foreach (var c in w.Craters)
            {
                if (!c.FlatFloor && c.Radius >= 9f) big.Add(c);
            }
            int sites = 0;
            foreach (var c in big)
            {
                if (sites >= 5) break;
                if (Vec2.Distance(c.Center, w.BasePos) < 45f) continue;
                AddWreckSite(w, rng, c.Center + rng.InsideCircle(c.Radius * 0.3f));
                sites++;
            }
            for (int tries = 0; sites < 5 && tries < 100; tries++)
            {
                var p = new Vec2(rng.Range(20f, size - 20f), rng.Range(20f, size - 20f));
                if (!ClearSpot(w, p, 14f)) continue;
                if (Vec2.Distance(p, w.BasePos) < 45f) continue;
                AddWreckSite(w, rng, p);
                sites++;
            }

            // --- Rocks and crater-stone outcrops ------------------------------------------------
            int rocks = 0;
            for (int tries = 0; rocks < 75 && tries < 1200; tries++)
            {
                var p = new Vec2(rng.Range(5f, size - 5f), rng.Range(5f, size - 5f));
                if (!ClearSpot(w, p, 4f)) continue;
                float r = rng.Range(0.7f, 2.2f);
                AddRock(w, p, r, rng.Range(0, 5));
                rocks++;
            }
            int stones = 0;
            for (int tries = 0; stones < 12 && tries < 500; tries++)
            {
                var p = new Vec2(rng.Range(15f, size - 15f), rng.Range(15f, size - 15f));
                float db = Vec2.Distance(p, w.BasePos);
                if (stones < 3 ? (db < 22f || db > 45f) : db < 22f) continue;
                if (!ClearSpot(w, p, 5f)) continue;
                AddNode(w, ResourceKind.Stone, p, 1.6f, rng.Range(90f, 130f), "Crater stone outcrop");
                stones++;
            }

            // --- Ferrite in crater beds ---------------------------------------------------------
            foreach (var c in w.Craters)
            {
                if (c.FlatFloor) continue;
                int n = c.Radius >= 11f ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    var p = c.Center + rng.InsideCircle(c.Radius * 0.45f);
                    if (!ClearSpot(w, p, 2.5f)) continue;
                    AddNode(w, ResourceKind.Ferrite, p, 1.1f, rng.Range(60f, 90f), "Ferrite chunk");
                }
            }
            // A guaranteed starter chunk close to home.
            {
                Vec2 p = w.BasePos + new Vec2(rng.Range(-25f, 25f), 30f);
                AddNode(w, ResourceKind.Ferrite, w.Nav.NearestWalkable(p, 6), 1.1f, 70f, "Ferrite chunk");
            }

            // --- Grass fibre --------------------------------------------------------------------
            int fibers = 0;
            for (int tries = 0; fibers < 10 && tries < 400; tries++)
            {
                var p = new Vec2(rng.Range(15f, size - 15f), rng.Range(15f, size - 15f));
                float db = Vec2.Distance(p, w.BasePos);
                if (fibers < 3 ? (db < 20f || db > 50f) : db < 20f) continue;
                if (!ClearSpot(w, p, 4f) || Ground(w, p) != GroundType.Grass) continue;
                AddNode(w, ResourceKind.Fiber, p, 1.4f, rng.Range(50f, 70f), "Wiry grass", false);
                fibers++;
            }

            // --- Enemy camps by distance ring ------------------------------------------------------
            PlaceCamps(w, rng);

            // --- Starting kin-band and world systems -----------------------------------------------
            PlayerUnits.CreateSquad(w, w.BasePos + new Vec2(0f, 7f));
            w.Director = new Director(w);
            w.Stock.Add(ResourceKind.Scrap, 60f);
            w.Stock.Add(ResourceKind.Stone, 30f);
            w.Stock.Add(ResourceKind.Fiber, 20f);
            w.Stock.Add(ResourceKind.Ferrite, 10f);
        }

        public static float CraterProfile(Crater c, Vec2 p)
        {
            float d = Vec2.Distance(p, c.Center) / c.Radius;
            if (c.FlatFloor)
            {
                if (d < 1f)
                {
                    float t = KMath.Clamp01((d - 0.78f) / 0.22f);
                    return -c.Depth + (c.Depth + c.Rim) * t * t;
                }
            }
            else if (d < 1f)
            {
                return -c.Depth + (c.Depth + c.Rim) * d * d;
            }
            if (d < 1.6f)
            {
                float t = 1f - (d - 1f) / 0.6f;
                return c.Rim * t * t;
            }
            return 0f;
        }

        /// <summary>Ground classification used for the terrain colours (and handy for tests).</summary>
        public static GroundType Ground(World w, Vec2 p)
        {
            if (Vec2.Distance(p, w.BasePos) < 19f) return GroundType.Base;
            if (w.InArena(p, 2f)) return GroundType.Arena;
            foreach (var s in w.WreckSites)
            {
                if (Vec2.Distance(p, s) < World.WreckSiteRadius * 0.8f) return GroundType.Wreckage;
            }
            foreach (var c in w.Craters)
            {
                float d = Vec2.Distance(p, c.Center) / c.Radius;
                if (d < 0.78f) return GroundType.CraterBed;
                if (d < 1.18f) return GroundType.CraterRim;
            }
            if (w.GroundNoise != null && w.GroundNoise.Fbm(p.X * 0.045f, p.Z * 0.045f, 3) > 0.62f) return GroundType.Stone;
            return GroundType.Grass;
        }

        private static bool ClearSpot(World w, Vec2 p, float r)
        {
            if (p.X < r || p.Z < r || p.X > Balance.MapSize - r || p.Z > Balance.MapSize - r) return false;
            if (Vec2.Distance(p, w.BasePos) < 22f + r) return false;
            if (w.InArena(p, 5f + r)) return false;
            if (Vec2.Distance(p, w.ArenaGate) < 10f + r && w.ArenaGate != Vec2.Zero) return false;
            int cx = NavGrid.CellX(p.X), cz = NavGrid.CellX(p.Z);
            int ir = (int)MathF.Ceiling(r);
            for (int z = cz - ir; z <= cz + ir; z++)
            for (int x = cx - ir; x <= cx + ir; x++)
            {
                if (w.Nav.BlocksMove(x, z)) return false;
            }
            foreach (var n in w.Nodes)
            {
                if (Vec2.Distance(n.Pos, p) < r + n.Radius + 1f) return false;
            }
            return true;
        }

        private static void PlaceBuilding(World w, BuildingKind kind, float angleDeg, float radius, float halfLen, float halfWidth)
        {
            // 0 degrees points north (+Z). Buildings ring the east, south and west of the plaza;
            // the north side stays open toward the map.
            float a = angleDeg * KMath.Deg2Rad;
            Vec2 dirOut = Vec2.FromAngle(a);
            Vec2 pos = w.BasePos + dirOut * radius;
            var s = StructureSystem.SpawnBuilding(w, kind, pos, dirOut.Right, halfLen, halfWidth);
            w.Base.Buildings[kind] = s;
        }

        private static void AddWreck(World w, Vec2 pos, Vec2 dir, float halfLen, float halfWidth, float height, PropKind kind, int variant)
        {
            var prop = new StaticProp { Kind = kind, Pos = pos, Dir = dir.Normalized, HalfLen = halfLen, HalfWidth = halfWidth, Height = height, Variant = variant };
            w.Props.Add(prop);
            var cells = w.Nav.RasterBox(pos, prop.Dir, halfLen + 0.2f, halfWidth + 0.2f);
            w.Nav.Stamp(cells, true, true, 0, +1);
        }

        private static void AddRock(World w, Vec2 pos, float r, int variant)
        {
            w.Props.Add(new StaticProp { Kind = r > 1.6f ? PropKind.Boulder : PropKind.Rock, Pos = pos, Radius = r, Height = r * 1.1f, Variant = variant });
            var cells = w.Nav.RasterCircle(pos, r + 0.3f);
            w.Nav.Stamp(cells, true, true, 0, +1);
        }

        private static void AddWreckSite(World w, SimRng rng, Vec2 center)
        {
            w.WreckSites.Add(center);
            int plates = rng.Range(3, 6);
            for (int i = 0; i < plates; i++)
            {
                Vec2 p = center + rng.InsideCircle(6f);
                if (!ClearSpot(w, p, 3f)) continue;
                AddWreck(w, p, Vec2.FromAngle(rng.Range(0f, KMath.TwoPi)), rng.Range(1.5f, 3.5f), rng.Range(0.6f, 1.1f), rng.Range(1.5f, 3.5f),
                    i == 0 ? PropKind.WreckSpire : PropKind.Wreck, rng.Range(0, 4));
            }
            for (int i = 0; i < 2; i++)
            {
                Vec2 p = center + rng.InsideCircle(5f);
                if (!ClearSpot(w, p, 2.2f)) continue;
                AddNode(w, ResourceKind.Scrap, p, 1.2f, rng.Range(60f, 90f), "Ark salvage");
            }
        }

        private static void AddNode(World w, ResourceKind kind, Vec2 pos, float radius, float amount, string label, bool blocks = true)
        {
            var n = new ResourceNode
            {
                Id = w.NextId(), Kind = kind, Pos = pos, Radius = radius, Amount = amount, MaxAmount = amount, Label = label,
                Variant = (int)(pos.X * 7 + pos.Z * 13) % 4,
            };
            if (blocks)
            {
                n.Cells = w.Nav.RasterCircle(pos, radius * 0.75f);
                w.Nav.Stamp(n.Cells, true, false, 0, +1);
            }
            w.Nodes.Add(n);
        }

        private static void PlaceCamps(World w, SimRng rng)
        {
            var camps = new List<(float minD, float maxD, string name, UnitKind[] kinds, int tier, bool wreck)>
            {
                (38f, 62f, "Cratermaw den", new[] { UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }, 1, false),
                (40f, 68f, "Cratermaw den", new[] { UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }, 1, false),
                (45f, 75f, "Feral drone nest", new[] { UnitKind.SalvageDrone, UnitKind.SalvageDrone, UnitKind.SalvageDrone }, 1, true),
                (68f, 105f, "Rockback wallow", new[] { UnitKind.Rockback, UnitKind.Rockback, UnitKind.Cratermaw }, 2, false),
                (70f, 110f, "Cratermaw den", new[] { UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }, 2, false),
                (72f, 115f, "Drone nest", new[] { UnitKind.SalvageDrone, UnitKind.SalvageDrone, UnitKind.ScrapMender }, 2, true),
                (100f, 150f, "Rockback wallow", new[] { UnitKind.Rockback, UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }, 3, false),
                (105f, 160f, "Drone hive", new[] { UnitKind.SalvageDrone, UnitKind.SalvageDrone, UnitKind.SalvageDrone, UnitKind.ScrapMender }, 3, true),
                (110f, 165f, "Alpha Cratermaw den", new[] { UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }, 3, false),
            };
            var used = new List<Vec2>();
            foreach (var spec in camps)
            {
                Vec2 home = Vec2.Zero;
                bool found = false;
                for (int tries = 0; tries < 300 && !found; tries++)
                {
                    Vec2 p;
                    if (spec.wreck && w.WreckSites.Count > 0 && tries < 100)
                    {
                        p = rng.Pick(w.WreckSites) + rng.InsideCircle(3f);
                    }
                    else
                    {
                        var c = w.Craters[rng.Range(1, w.Craters.Count)];
                        p = c.Center + Vec2.FromAngle(rng.Range(0f, KMath.TwoPi)) * (c.Radius * 0.85f); // dens in crater walls
                    }
                    float db = Vec2.Distance(p, w.BasePos);
                    if (db < spec.minD || db > spec.maxD) continue;
                    if (w.InArena(p, 12f)) continue;
                    if (w.Nav.BlocksMoveAt(p)) continue;
                    bool tooClose = false;
                    foreach (var u in used)
                    {
                        if (Vec2.Distance(u, p) < 24f) tooClose = true;
                    }
                    if (tooClose) continue;
                    home = p;
                    found = true;
                }
                if (!found) continue;
                used.Add(home);
                var g = Spawner.CreateGroup(w, spec.name, home, spec.tier, true);
                g.Spec.AddRange(spec.kinds);
                g.AggroRadius = spec.tier >= 3 ? 14f : 12f;
                Spawner.Populate(w, g);
            }
        }
    }
}
