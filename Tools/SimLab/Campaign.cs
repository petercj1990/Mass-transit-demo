using System;
using System.Collections.Generic;
using System.Linq;
using Kinforge.Sim;

/// <summary>
/// A scripted "player" that runs the intended MVP progression on a real generated world, to check
/// the game is completable and to measure pacing: gather, hunt hide, go home to close wounds and smelt,
/// drill the kin-band in the War Hall, craft gear and the Sigil, then raid Rustmaw with phase-appropriate commands.
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

    static readonly float TraceFrom = float.TryParse(Environment.GetEnvironmentVariable("KF_TRACE_FROM"), out float f) ? f : 0f;

    public static void Run(int seed, int maxMinutes, bool verbose)
    {
        var w = World.Create(seed);
        w.TitanEnabled = Program.TitanOn;
        var log = new List<string>();
        void Log(string s)
        {
            string line = $"[{(int)w.Time / 60:00}:{(int)w.Time % 60:00}] {s}";
            log.Add(line);
            if (verbose) Console.WriteLine(line);
        }

        Squad Band() => w.Squads[0];

        var steps = new List<Step>
        {
            new Step { Name = "War Hall 2", Done = x => x.Base.WarHallLevel >= 2, Cost = PlayerBase.WarHallCost[1], Buy = x => x.Issue(new PlayerOrder { Type = OrderType.UpgradeWarHall }) },
            new Step { Name = "Arms", Done = x => Band().WeaponTier >= 2 || x.Base.Bench == BenchJob.Weapons, Cost = PlayerBase.WeaponsCost, Buy = x => x.Issue(new PlayerOrder { Type = OrderType.CraftWeapons, TargetId = Band().Id }) },
            new Step { Name = "Plate", Done = x => Band().ArmorTier >= 2 || x.Base.Bench == BenchJob.Armor, Cost = PlayerBase.ArmorCost, Buy = x => x.Issue(new PlayerOrder { Type = OrderType.CraftArmor, TargetId = Band().Id }) },
            new Step { Name = "War Hall 3", Done = x => x.Base.WarHallLevel >= 3, Cost = PlayerBase.WarHallCost[2], Buy = x => x.Issue(new PlayerOrder { Type = OrderType.UpgradeWarHall }) },
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
        int titanRises = 0, titanClashes = 0, titanShelters = 0, titanViolations = 0;
        for (int t = 0; t < maxTicks && !w.Victory; t++)
        {
            w.Step();
            foreach (var ev in w.Events)
            {
                if (ev.Type == SimEventType.AbilityUnlocked && ev.Ability == AbilityId.TitanForm) Log($"MILESTONE Titan Form unlocked ({w.FindUnitAny(ev.A)?.Name})");
                else if (ev.Type == SimEventType.TitanRise) titanRises++;
                else if (ev.Type == SimEventType.TitanClash) titanClashes++;
                else if (ev.Type == SimEventType.TitanShelter) titanShelters++;
            }
            if (t % 20 == 0 && Titan.CheckInvariants(w) is string bad)
            {
                if (titanViolations++ == 0) Log("TITAN INVARIANT " + bad);
            }
            if (verbose)
            {
                while (alertIdx < w.Alerts.Count)
                {
                    var a = w.Alerts[alertIdx++];
                    if (a.Severity >= 2) Log("  alert: " + a.Text);
                }
            }

            Mile("War Hall 2", w.Base.WarHallLevel >= 2);
            Mile("War Hall 3", w.Base.WarHallLevel >= 3);
            Mile("First ferrite gear", Band().WeaponTier > 1 || Band().ArmorTier > 1);
            Mile("All gear", Band().WeaponTier > 1 && Band().ArmorTier > 1);
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

            if (TraceFrom > 0 && w.Time >= TraceFrom && w.Time < TraceFrom + 60 && t % 40 == 0)
            {
                var s0 = Band();
                Console.WriteLine($"T {w.Time:0} combat {s0.InCombat} engaged {s0.Engaged.Count} task {s0.Task} anchor {s0.Anchor} canchor {s0.CombatAnchor} | " +
                    string.Join(" ", s0.Members.Select(m => $"{m.Role.ToString()[0]}{m.Pos}->{m.Intent.Kind}{(m.Intent.Kind == IntentKind.Move ? m.Intent.Point.ToString() : "")}")) + " | " +
                    string.Join(" ", s0.Engaged.Select(e => $"{e.Kind}#{e.Id} {e.Pos} {e.Intent.Kind} {e.Intent.Point} grp {e.Group?.State} home {e.Group?.Home} leash {e.Group?.LeashRadius}")));
            }
            if (t % 100 != 0) continue; // the "player" thinks every 5 seconds

            // Buy whatever the plan needs next.
            foreach (var s in steps)
            {
                if (s.Done(w)) continue;
                bool benchStep = s.Name.StartsWith("Arms") || s.Name.StartsWith("Plate") || s.Name == "Sigil";
                if (benchStep && w.Base.Bench != BenchJob.None) break;
                if (w.Stock.Has(s.Cost))
                {
                    s.Buy(w);
                    Log($"buy {s.Name}");
                }
                break;
            }

            var remaining = steps.Where(s => !s.Done(w)).ToList();
            bool trained = Band().Members.Average(m => m.Doctrine.Tier(CommandType.DefensiveFormation)) >= 2.4;
            bool ready = remaining.Count == 0 && w.Base.Bench == BenchJob.None && trained;

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

            // Not ready while dead kin wait at the Cradle for materials we lack: go and gather them first.
            if (shortfall.Sum() >= 1f) ready = false;

            bool canSmelt = alloyShort > 0f && w.Stock[ResourceKind.Ferrite] >= 2f;
            bool benchWaiting = w.Base.Bench != BenchJob.None;

            foreach (var sq in w.Squads)
            {
                if (sq.Wiped)
                {
                    // One kin-band: its cores come home through the recall (there is nobody to fetch them).
                    if (w.Caches.Count > 0) w.Issue(new PlayerOrder { Type = OrderType.RecallCores });
                    continue;
                }
                if (sq.InCombat) continue;
                if (raiding) continue;

                // Push your luck, but not too far: deep wounds only close at home.
                float worstWound = sq.Members.Where(m => m.Life != LifeState.Dead).Select(m => m.Wound).DefaultIfEmpty(0f).Max();
                bool wounded = worstWound > 0.35f || sq.Members.Any(m => m.Life == LifeState.Downed);
                if (sq.Garrisoned && worstWound > 0.02f) continue;
                if (wounded && !sq.Garrisoned)
                {
                    if (sq.Task != SquadTask.Garrison)
                    {
                        w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { sq.Id } });
                        Log($"{sq.Name} heads home to close wounds (worst {worstWound:P0}), carrying {sq.CarryLoad:0}");
                    }
                    continue;
                }

                // Garrison duty when smelting or the bench is the bottleneck.
                // (Only alloy missing, and ferrite on hand to smelt it.)
                float otherShort = shortfall.Sum() - shortfall[(int)ResourceKind.Ferrite] - shortfall[(int)ResourceKind.Alloy];
                bool garrisonDuty = benchWaiting || (canSmelt && otherShort < 1f);
                if (garrisonDuty)
                {
                    if (!sq.Garrisoned && sq.Task != SquadTask.Garrison)
                    {
                        w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { sq.Id } });
                        Log($"{sq.Name} garrisons (smelt {alloyShort:0} alloy, bench {w.Base.Bench})");
                    }
                    continue;
                }
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

                if (ready) continue; // handled below
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
                if (kind < 0 || (remaining.Count == 0 && !trained && best < 1f)) kind = (int)ResourceKind.Hide; // nothing to buy or regrow: go train
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
                bool allHealthy = w.Squads.All(s => s.Members.All(m => m.Life == LifeState.Active && !m.Injured && m.Wound < 0.02f && m.ManaFraction > 0.9f));
                if (!allHealthy)
                {
                    foreach (var sq in w.Squads)
                        if (!sq.Garrisoned) w.Issue(new PlayerOrder { Type = OrderType.Garrison, Squads = { sq.Id } });
                    continue;
                }
                raiding = true;
                Log("RAID: the kin-band heads for Breach Hold");
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
        foreach (var l in log.Where(l => l.Contains("MILESTONE") || l.Contains("TITAN") || l.Contains("RAID") || l.Contains("raid failed") || l.Contains("buy"))) Console.WriteLine("  " + l);
        int deaths = w.Squads.Sum(s => s.Members.Count(m => m.Life == LifeState.Dead));
        Console.WriteLine($"  fights {w.Reports.Count}, wipes {w.Reports.Count(r => r.Result == "Squad wiped")}, dead now {deaths}, trips home to heal {log.Count(l => l.Contains("close wounds"))}");
        Console.WriteLine($"  Titan Form: {titanRises} rises, {titanClashes} charge clashes, {titanShelters} sheltered hits, {titanViolations} invariant violations");
        if (!w.Victory)
            foreach (var s in w.Squads)
                Console.WriteLine($"  end state: raiding {raiding}, boss phase {w.Boss.Phase} active {w.Boss.Active} gate open {w.Boss.GateOpen}, {s.Name} task {s.Task} stance {s.Stance} " +
                                  $"{Vec2.Distance(s.Anchor, w.ArenaCenter):0} m from the arena (radius {w.ArenaRadius:0}), in combat {s.InCombat}, " +
                                  string.Join(" ", s.Members.Select(m => $"{m.Role}:{m.Life}@{Vec2.Distance(m.Pos, w.ArenaCenter):0}")) +
                                  "\n    enemies near: " + string.Join(" ", w.Units.Where(u => u.Faction == Faction.Enemy && !u.Removed && Vec2.Distance(u.Pos, s.Centroid) < 40f)
                                      .Select(u => $"{u.Kind}#{u.Id}:{u.Life} hp {u.Hp:0}/{u.MaxHp:0} {Vec2.Distance(u.Pos, s.Centroid):0}m arena {Vec2.Distance(u.Pos, w.ArenaCenter):0} intent {u.Intent.Kind}")) +
                                  "\n    tank intent " + s.Member(Role.Tank).Intent.Kind + " target " + s.Member(Role.Tank).Intent.TargetId + ", healer intent " + s.Member(Role.Healer).Intent.Kind);
        foreach (var s in w.Squads)
            Console.WriteLine($"  {s.Name}: DF tier {string.Join("", s.Members.Select(m => m.Doctrine.Tier(CommandType.DefensiveFormation)))}, max tiers {string.Join("", s.Members.Select(m => m.Doctrine.MaxTier))}");
    }

    static PlayerOrder With(this PlayerOrder o, IEnumerable<int> ids)
    {
        o.Squads.AddRange(ids);
        return o;
    }
}
