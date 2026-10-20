using System;
using System.Collections.Generic;
using System.Linq;
using Kinforge.Sim;

/// <summary>
/// A scripted "player" that runs the intended MVP progression on a real generated world, to check
/// the game is completable and to measure pacing: gather, hunt hide, garrison to smelt, upgrade the
/// War Hall, recruit, craft gear and the Sigil, then raid Rustmaw with phase-appropriate commands.
/// </summary>
static class Campaign
{
    sealed class Step
    {
        public string Name;
        public Func<World, bool> Done;
        public Cost Cost;
        public Action<World> Buy;
    }

    public static void Run(int seed, int maxMinutes, bool verbose)
    {
        var w = World.Create(seed);
        var log = new List<string>();
        void Log(string s)
        {
            string line = $"[{(int)w.Time / 60:00}:{(int)w.Time % 60:00}] {s}";
            log.Add(line);
            if (verbose) Console.WriteLine(line);
        }

        Squad A() => w.Squads[0];
        Squad B() => w.Squads.Count > 1 ? w.Squads[1] : null;

        var steps = new List<Step>
        {
            new Step { Name = "War Hall 2", Done = x => x.Base.WarHallLevel >= 2, Cost = PlayerBase.WarHallCost[1], Buy = x => x.Issue(new PlayerOrder { Type = OrderType.UpgradeWarHall }) },
            new Step { Name = "Recruit", Done = x => x.Squads.Count >= 2 || x.Base.Recruiting, Cost = PlayerBase.RecruitCost, Buy = x => x.Issue(new PlayerOrder { Type = OrderType.Recruit }) },
            new Step { Name = "Arms A", Done = x => x.Squads[0].WeaponTier >= 2 || (x.Base.Bench == BenchJob.Weapons && x.Base.BenchSquadId == x.Squads[0].Id), Cost = PlayerBase.WeaponsCost, Buy = x => x.Issue(new PlayerOrder { Type = OrderType.CraftWeapons, TargetId = x.Squads[0].Id }) },
            new Step { Name = "Plate A", Done = x => x.Squads[0].ArmorTier >= 2 || (x.Base.Bench == BenchJob.Armor && x.Base.BenchSquadId == x.Squads[0].Id), Cost = PlayerBase.ArmorCost, Buy = x => x.Issue(new PlayerOrder { Type = OrderType.CraftArmor, TargetId = x.Squads[0].Id }) },
            new Step { Name = "Arms B", Done = x => x.Squads.Count > 1 && (x.Squads[1].WeaponTier >= 2 || (x.Base.Bench == BenchJob.Weapons && x.Base.BenchSquadId == x.Squads[1].Id)), Cost = PlayerBase.WeaponsCost, Buy = x => { if (x.Squads.Count > 1) x.Issue(new PlayerOrder { Type = OrderType.CraftWeapons, TargetId = x.Squads[1].Id }); } },
            new Step { Name = "Plate B", Done = x => x.Squads.Count > 1 && (x.Squads[1].ArmorTier >= 2 || (x.Base.Bench == BenchJob.Armor && x.Base.BenchSquadId == x.Squads[1].Id)), Cost = PlayerBase.ArmorCost, Buy = x => { if (x.Squads.Count > 1) x.Issue(new PlayerOrder { Type = OrderType.CraftArmor, TargetId = x.Squads[1].Id }); } },
            new Step { Name = "Sigil", Done = x => x.Base.SigilCrafted || x.Base.Bench == BenchJob.Sigil, Cost = PlayerBase.SigilCost, Buy = x => x.Issue(new PlayerOrder { Type = OrderType.CraftSigil }) },
        };

        var milestones = new HashSet<string>();
        void Mile(string name, bool cond)
        {
            if (cond && milestones.Add(name)) Log("MILESTONE " + name);
        }

        CommandType lastCmd = CommandType.DefensiveFormation;
        bool raiding = false;
        int alertIdx = 0;
        int maxTicks = maxMinutes * 60 * 20;
        for (int t = 0; t < maxTicks && !w.Victory; t++)
        {
            w.Step();
            if (verbose)
            {
                while (alertIdx < w.Alerts.Count)
                {
                    var a = w.Alerts[alertIdx++];
                    if (a.Severity >= 2) Log("  alert: " + a.Text);
                }
            }

            Mile("War Hall 2", w.Base.WarHallLevel >= 2);
            Mile("Second kin-band", w.Squads.Count >= 2);
            Mile("First ferrite gear", w.Squads.Any(s => s.WeaponTier > 1 || s.ArmorTier > 1));
            Mile("All gear", w.Squads.Count > 1 && w.Squads.All(s => s.WeaponTier > 1 && s.ArmorTier > 1));
            Mile("Sigil", w.Base.SigilCrafted);
            Mile("Boss phase 2", w.Boss.Phase >= 2);
            Mile("Boss phase 3", w.Boss.Phase >= 3);

            // Raid commander: read the boss every half second.
            if (raiding && w.Boss.Active && t % 10 == 0)
            {
                var boss = w.Boss;
                CommandType want = CommandType.DefensiveFormation;
                if (boss.Phase == 1) want = boss.VentsExposed || boss.Staggered ? CommandType.Charge : CommandType.SpreadOut;
                else if (boss.Phase == 2) want = boss.MendersAlive > 0 ? CommandType.FocusFire : CommandType.DefensiveFormation;
                else if (boss.Phase == 3) want = boss.BlastPending || boss.NextBlastIn < 2.5f ? CommandType.Stack : CommandType.Charge;
                var ids = w.Squads.Where(s => !s.Wiped && !s.Garrisoned).Select(s => s.Id).ToList();
                if (want != lastCmd)
                {
                    w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = want, Squads = { } }.With(ids));
                    lastCmd = want;
                }
                if (boss.Phase == 2 && t % 40 == 0)
                {
                    var mender = boss.Group.Members.FirstOrDefault(m => m.Kind == UnitKind.ScrapMender && m.IsActive);
                    if (mender != null) w.Issue(new PlayerOrder { Type = OrderType.Attack, TargetId = mender.Id }.With(ids));
                }
            }

            if (t % 100 != 0) continue; // the "player" thinks every 5 seconds

            // Buy whatever the plan needs next.
            foreach (var s in steps)
            {
                if (s.Done(w)) continue;
                bool benchStep = s.Name.StartsWith("Arms") || s.Name.StartsWith("Plate") || s.Name == "Sigil";
                if (benchStep && w.Base.Bench != BenchJob.None) break;
                if (s.Name == "Recruit" && w.Base.WarHallLevel < 2) break;
                if (w.Stock.Has(s.Cost))
                {
                    s.Buy(w);
                    Log($"buy {s.Name}");
                }
                break;
            }

            var remaining = steps.Where(s => !s.Done(w)).ToList();
            bool trained = w.Squads.Count >= 2 && w.Squads.All(s => s.Members.Average(m => m.Doctrine.Tier(CommandType.DefensiveFormation)) >= 1.8);
            bool ready = remaining.Count == 0 && w.Base.Bench == BenchJob.None && w.Squads.Count >= 2 && trained;

            // What do we still need, in raw materials (alloy counts as 2 ferrite)?
            var need = new float[6];
            foreach (var s in remaining)
                foreach (var it in s.Cost.Items) need[(int)it.Kind] += it.Amount;
            // Dead kin waiting at the Cradle need their rebirth materials too.
            foreach (int id in w.Base.CradleQueue)
            {
                var dead = w.FindUnitAny(id);
                if (dead != null) foreach (var it in w.Base.RegrowCostFor(dead).Items) need[(int)it.Kind] += it.Amount;
            }
            float alloyShort = Math.Max(0f, need[(int)ResourceKind.Alloy] - w.Stock[ResourceKind.Alloy]);
            need[(int)ResourceKind.Ferrite] += alloyShort * 2f;
            var shortfall = new float[6];
            for (int i = 0; i < 6; i++) shortfall[i] = Math.Max(0f, need[i] - w.Stock.Amounts[i]);

            bool canSmelt = alloyShort > 0f && w.Stock[ResourceKind.Ferrite] >= 2f;
            bool benchWaiting = w.Base.Bench != BenchJob.None;

            foreach (var sq in w.Squads)
            {
                if (sq.Wiped)
                {
                    var cache = w.Caches.FirstOrDefault();
                    var other = w.Squads.FirstOrDefault(o => o != sq && !o.Wiped && o.ActiveCount > 0);
                    if (cache != null && other != null && other.Task != SquadTask.Recover)
                    {
                        w.Issue(new PlayerOrder { Type = OrderType.Recover, TargetId = cache.Id, Squads = { other.Id } });
                        Log($"{other.Name} goes to recover {sq.Name}'s cores");
                    }
                    if (other == null && cache != null) w.Issue(new PlayerOrder { Type = OrderType.RecallCores });
                    continue;
                }
                if (sq.InCombat) continue;
                if (raiding) continue;

                // Garrison duty: the second squad (or the only one, when smelting is the bottleneck).
                // Rotate garrison duty: whichever squad has the lower Doctrine stays home, so both get trained.
                Squad homebody = w.Squads.Count > 1
                    ? w.Squads.Where(o => !o.Wiped).OrderBy(o => o.Members.Sum(m => m.Doctrine.TierSum)).First()
                    : null;
                if (homebody != null && (int)(w.Time / 240f) % 2 == 1) homebody = w.Squads.Where(o => !o.Wiped && o != homebody).FirstOrDefault() ?? homebody;
                bool isGarrisonSquad = w.Squads.Count > 1 ? sq == homebody : (canSmelt && shortfall.Sum() - shortfall[(int)ResourceKind.Ferrite] < 1f);
                if ((canSmelt || benchWaiting) && isGarrisonSquad)
                {
                    if (!sq.Garrisoned && sq.Task != SquadTask.Garrison)
                    {
                        w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { sq.Id } });
                        Log($"{sq.Name} garrisons (smelt {alloyShort:0} alloy, bench {w.Base.Bench})");
                    }
                    continue;
                }
                if (sq.Garrisoned && (canSmelt || benchWaiting) && w.Squads.Count == 1) continue;
                if (sq.CoresCarried > 0 && sq.Task != SquadTask.Return)
                {
                    w.Issue(new PlayerOrder { Type = OrderType.ReturnToBase, Squads = { sq.Id } });
                    continue;
                }

                bool hurt = sq.Members.Any(m => m.IsActive && (m.Injured || m.HpFraction < 0.55f));
                if (hurt && !sq.Garrisoned)
                {
                    if (sq.Stance != CommandType.Rest && sq.Task == SquadTask.Idle)
                        w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = CommandType.Rest, Squads = { sq.Id } });
                    if (sq.Task == SquadTask.Idle) continue;
                }
                if (sq.Stance == CommandType.Rest && hurt) continue;

                if (ready)
                {
                    continue; // handled below
                }
                if (sq.Garrisoned && !isGarrisonSquad && !(canSmelt || benchWaiting))
                {
                    // fall through: leave the garrison and get to work
                }
                else if (sq.Garrisoned && !isGarrisonSquad)
                {
                    // the other squad has taken over the workshops
                }

                if (sq.Task != SquadTask.Idle && !sq.Garrisoned) continue;

                // Pick the biggest raw shortfall; hide comes from hunting.
                int kind = -1;
                float best = 0f;
                foreach (int i in new[] { (int)ResourceKind.Hide, (int)ResourceKind.Ferrite, (int)ResourceKind.Stone, (int)ResourceKind.Fiber, (int)ResourceKind.Scrap })
                {
                    if (shortfall[i] > best)
                    {
                        best = shortfall[i];
                        kind = i;
                    }
                }
                if (kind < 0 || (remaining.Count == 0 && !trained)) kind = (int)ResourceKind.Hide; // nothing to buy: go train
                if (kind == (int)ResourceKind.Hide)
                {
                    int maxTier = sq.Members.Average(m => m.Doctrine.Tier(CommandType.DefensiveFormation)) >= 1.5 && sq.ArmorTier > 1 ? 2 : 1;
                    var target = w.Units.Where(u => u.Faction == Faction.Enemy && u.IsActive && u.Group != null && u.Group.IsCamp && u.Group.Tier <= maxTier && u.Kind != UnitKind.SalvageDrone)
                        .OrderBy(u => Vec2.Distance(u.Pos, w.BasePos)).FirstOrDefault();
                    if (target != null)
                    {
                        if (sq.Stance != CommandType.DefensiveFormation)
                            w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = CommandType.DefensiveFormation, Squads = { sq.Id } });
                        w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = target.Pos + (w.BasePos - target.Pos).Normalized * 9f, Squads = { sq.Id } });
                        if (verbose) Log($"{sq.Name} hunts {target.Group.Name} (tier {target.Group.Tier}) for hide");
                        continue;
                    }
                    kind = (int)ResourceKind.Ferrite;
                }
                var rk = (ResourceKind)kind;
                var node = w.Nodes.Where(n => !n.Removed && n.Kind == rk).OrderBy(n => Vec2.Distance(n.Pos, w.BasePos)).FirstOrDefault();
                if (node != null)
                {
                    w.Issue(new PlayerOrder { Type = OrderType.Gather, TargetId = node.Id, Squads = { sq.Id } });
                    if (verbose) Log($"{sq.Name} gathers {Stockpile.Name(rk)} ({best:0} short)");
                }
            }

            if (ready && !raiding)
            {
                var ids = w.Squads.Select(s => s.Id).ToList();
                bool allHealthy = w.Squads.All(s => s.Members.All(m => m.Life == LifeState.Active && !m.Injured));
                if (!allHealthy)
                {
                    foreach (var sq in w.Squads)
                        if (!sq.Garrisoned) w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { sq.Id } });
                    continue;
                }
                raiding = true;
                Log("RAID: both squads head for Breach Hold");
                w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = CommandType.DefensiveFormation }.With(ids));
                w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + (w.BasePos - w.ArenaCenter).Normalized * 10f }.With(ids));
            }
            if (raiding && !w.Boss.Active && w.Boss.Phase == 0)
            {
                var ids = w.Squads.Where(s => !s.Wiped).Select(s => s.Id).ToList();
                if (ids.Count == 0 || w.Squads.Any(s => s.Members.Count(m => m.Life == LifeState.Active) < 3))
                {
                    raiding = false;
                    Log("raid failed: regroup at base");
                    foreach (var sq in w.Squads) if (!sq.Wiped) w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { sq.Id } });
                }
                else if (w.Squads.All(s => s.Task == SquadTask.Idle))
                    w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + (w.BasePos - w.ArenaCenter).Normalized * 10f }.With(ids));
            }
        }

        Console.WriteLine($"Seed {seed}: {(w.Victory ? "VICTORY" : "no victory")} at {(int)w.Time / 60}:{(int)w.Time % 60:00}. Stock: " +
                          string.Join(", ", Enumerable.Range(0, 6).Select(i => $"{(ResourceKind)i}={w.Stock.Amounts[i]:0}")));
        foreach (var l in log.Where(l => l.Contains("MILESTONE") || l.Contains("RAID") || l.Contains("raid failed") || l.Contains("buy"))) Console.WriteLine("  " + l);
        int deaths = w.Squads.Sum(s => s.Members.Count(m => m.Life == LifeState.Dead));
        Console.WriteLine($"  fights {w.Reports.Count}, wipes {w.Reports.Count(r => r.Result == "Squad wiped")}, dead now {deaths}");
        foreach (var s in w.Squads)
            Console.WriteLine($"  {s.Name}: DF tier {string.Join("", s.Members.Select(m => m.Doctrine.Tier(CommandType.DefensiveFormation)))}, max tiers {string.Join("", s.Members.Select(m => m.Doctrine.MaxTier))}");
    }

    static PlayerOrder With(this PlayerOrder o, IEnumerable<int> ids)
    {
        o.Squads.AddRange(ids);
        return o;
    }
}
