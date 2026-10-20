using System;
using System.Collections.Generic;
using System.Linq;
using Kinforge.Sim;

// Headless balance lab. Usage:
//   dotnet run -- smoke [seed] [minutes]
//   dotnet run -- fights [runs]
//   dotnet run -- boss [runs] [tier] [gear]
//   dotnet run -- learn [fights]
//   dotnet run -- trace
static class Program
{
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "fights";
        int Arg(int i, int d) => args.Length > i && int.TryParse(args[i], out int v) ? v : d;
        switch (mode)
        {
            case "smoke": Smoke(Arg(1, 1), Arg(2, 10)); break;
            case "fights": Fights(Arg(1, 30)); break;
            case "boss": Boss(Arg(1, 20), Arg(2, 3), Arg(3, 2)); break;
            case "learn": Learn(Arg(1, 12)); break;
            case "bosstrace": BossTrace(Arg(1, 500), Arg(2, 2), Arg(3, 2), Arg(4, 1) != 0); break;
            case "p3": P3(Arg(1, 500), Arg(2, 2)); break;
            case "gather": GatherTest(); break;
            case "bake": BakeTiming(); break;
            case "campaign": Campaign.Run(Arg(1, 1337), Arg(2, 90), Arg(3, 0) != 0); break;
            case "trace": Trace(Arg(1, 1), Arg(2, 1), Arg(3, 0), Arg(4, 0)); break;
            default: Console.WriteLine("unknown mode"); return 1;
        }
        return 0;
    }

    // ------------------------------------------------------------------------------------------

    static void Smoke(int seed, int minutes)
    {
        var w = World.Create(seed);
        Console.WriteLine($"Seed {seed}: {w.Units.Count} units, {w.Structures.Count} structures, {w.Nodes.Count} nodes, {w.Props.Count} props, {w.Groups.Count} groups");
        foreach (var g in w.Groups) Console.WriteLine($"  {g.Name} tier {g.Tier} at {g.Home} ({g.Members.Count}) dist {Vec2.Distance(g.Home, w.BasePos):0}");
        var sq = w.Squads[0];
        // A simple bot commander: gather ferrite, then go hunting the nearest camp, rest after fights.
        int ticks = minutes * 60 * 20;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int alertIdx = 0;
        for (int t = 0; t < ticks; t++)
        {
            if (t % 200 == 0) Bot(w);
            w.Step();
            while (alertIdx < w.Alerts.Count) { Console.WriteLine($"[{w.Time,6:0.0}] {w.Alerts[alertIdx].Text}"); alertIdx++; }
        }
        sw.Stop();
        Console.WriteLine($"Simulated {minutes} min in {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds * 1000.0 / ticks:0.0} us/tick)");
        Console.WriteLine($"Stock: " + string.Join(", ", Enumerable.Range(0, 6).Select(i => $"{(ResourceKind)i}={w.Stock.Amounts[i]:0}")));
        foreach (var s in w.Squads)
        {
            Console.WriteLine($"{s.Name}: task {s.Task} stance {s.Stance} wiped {s.Wiped} garrison {s.Garrisoned}");
            foreach (var m in s.Members)
                Console.WriteLine($"  {m.Name,-8} {m.Def.Name,-12} {m.Life,-7} inj {m.Injured,-5} hp {m.Hp,4:0}/{m.MaxHp,4:0} pos {m.Pos} tiers {string.Join("", Enumerable.Range(0, 10).Select(i => m.Doctrine.Tier((CommandType)i)))} xp {m.Doctrine.UnbankedTotal:0}+{m.Doctrine.Banked.Sum():0}");
        }
        foreach (var r in w.Reports.TakeLast(6))
        {
            Console.WriteLine(r.Headline);
            foreach (var l in r.Lines) Console.WriteLine($"    {l.Who}: {l.Text}");
        }
    }

    static int _botTarget;
    static void Bot(World w)
    {
        foreach (var sq in w.Squads)
        {
            if (sq.Wiped || sq.InCombat) continue;
            bool hurt = sq.Members.Any(m => m.IsActive && (m.Injured || m.HpFraction < 0.6f));
            if (hurt && sq.Stance != CommandType.Rest && sq.Task == SquadTask.Idle)
            {
                w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = CommandType.Rest, Squads = { sq.Id } });
                continue;
            }
            if (sq.Stance == CommandType.Rest && hurt) continue;
            if (sq.Task != SquadTask.Idle) continue;
            if (sq.CoresCarried > 0) { w.Issue(new PlayerOrder { Type = OrderType.ReturnToBase, Squads = { sq.Id } }); continue; }
            // Alternate: gather, then attack nearest live camp.
            if ((_botTarget++ % 2) == 0)
            {
                var node = w.Nodes.Where(n => !n.Removed && n.Kind == ResourceKind.Ferrite).OrderBy(n => Vec2.Distance(n.Pos, w.BasePos)).FirstOrDefault();
                if (node != null) { w.Issue(new PlayerOrder { Type = OrderType.Gather, TargetId = node.Id, Squads = { sq.Id } }); continue; }
            }
            var target = w.Units.Where(u => u.Faction == Faction.Enemy && u.IsActive && u.Kind != UnitKind.Rustmaw && u.Group != null && u.Group.IsCamp)
                .OrderBy(u => Vec2.Distance(u.Pos, w.BasePos)).FirstOrDefault();
            if (target != null)
            {
                w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = CommandType.DefensiveFormation, Squads = { sq.Id } });
                w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = target.Pos + (w.BasePos - target.Pos).Normalized * 10f, Squads = { sq.Id } });
            }
        }
    }

    // ------------------------------------------------------------------------------------------

    sealed class Result
    {
        public int Wins, Wipes, Downs, Deaths, Runs;
        public double Time, HpLeft, Taken, Healed;
        public void Add(Result r) { Runs += r.Runs; Wins += r.Wins; Wipes += r.Wipes; Downs += r.Downs; Deaths += r.Deaths; Time += r.Time; HpLeft += r.HpLeft; Taken += r.Taken; Healed += r.Healed; }
        public override string ToString() =>
            $"win {100.0 * Wins / Runs,4:0}%  wipe {100.0 * Wipes / Runs,3:0}%  downs {1.0 * Downs / Runs,4:0.0}  deaths {1.0 * Deaths / Runs,4:0.0}  time {Time / Runs,4:0}s  taken {Taken / Runs,4:P0}  healed {Healed / Runs,4:P0}";
    }

    static Result RunFight(int seed, CommandType stance, int tier, UnitKind[] enemies, int gear = 1, bool verbose = false)
    {
        var w = Scenarios.Blank(seed);
        var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), tier, stance, gear);
        Scenarios.AddPack(w, new Vec2(40f, 62f), enemies);
        // Walk the squad into the camp.
        w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = new Vec2(40f, 54f), Squads = { sq.Id } });
        var res = new Result { Runs = 1 };
        int maxTicks = 20 * 240;
        int t = 0;
        float squadHp = sq.Members.Sum(m => m.MaxHp);
        for (; t < maxTicks; t++)
        {
            w.Step();
            foreach (var ev in w.Events)
            {
                if (ev.Type == SimEventType.Hit && w.FindUnitAny(ev.B) is Unit hu && hu.IsPlayer) res.Taken += ev.Value / squadHp;
                if (ev.Type == SimEventType.Heal && w.FindUnitAny(ev.B) is Unit hh && hh.IsPlayer) res.Healed += ev.Value / squadHp;
            }
            if (verbose && t % 20 == 0) Dump(w);
            if (w.Reports.Count > 0) break;
        }
        var r = w.Reports.FirstOrDefault();
        if (r != null)
        {
            if (r.Result == "Victory") res.Wins++;
            if (r.Result == "Squad wiped") res.Wipes++;
            res.Time = r.Duration;
        }
        else res.Time = t / 20.0;
        foreach (var m in sq.Members)
        {
            if (m.Life == LifeState.Dead) res.Deaths++;
            if (m.Injured || m.Life != LifeState.Active) res.Downs++;
            res.HpLeft += m.Life == LifeState.Active ? m.HpFraction / 5.0 : 0;
        }
        if (verbose && r != null)
        {
            Console.WriteLine(r.Headline);
            foreach (var l in r.Lines) Console.WriteLine($"    {l.Who}: {l.Text}");
        }
        return res;
    }

    static void Dump(World w)
    {
        Console.Write($"t={w.Time,5:0.0} ");
        foreach (var u in w.Units.Where(u => u.Life != LifeState.Dead))
            Console.Write($"{u.Def.Name.Substring(0, 3)}{(u.Life == LifeState.Downed ? "_" : "")}({u.Pos.X:0},{u.Pos.Z:0} {u.Hp:0} {u.Intent.Kind.ToString()[0]}{(u.Action != UnitAction.None ? u.Action.ToString()[0] : ' ')}) ");
        Console.WriteLine();
    }

    static void Fights(int runs)
    {
        var setups = Setups;
        var stances = new[] { CommandType.DefensiveFormation, CommandType.Charge, CommandType.Kite, CommandType.FocusFire, CommandType.HoldPosition, CommandType.SpreadOut, CommandType.Stack, CommandType.Fortify };
        foreach (int tier in new[] { 0, 3 })
        {
            Console.WriteLine($"=== Doctrine tier {tier} ===");
            foreach (var s in setups)
            {
                Console.WriteLine(s.name);
                foreach (var st in stances)
                {
                    var agg = new Result();
                    for (int i = 0; i < runs; i++) agg.Add(RunFight(1000 + i, st, tier, s.kinds));
                    Console.WriteLine($"   {st,-19} {agg}");
                }
            }
        }
    }

    static readonly (string name, UnitKind[] kinds)[] Setups =
    {
        ("3 Cratermaws", new[] { UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }),
        ("4 Cratermaws", new[] { UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }),
        ("3 Drones", new[] { UnitKind.SalvageDrone, UnitKind.SalvageDrone, UnitKind.SalvageDrone }),
        ("2 Drones+Mender", new[] { UnitKind.SalvageDrone, UnitKind.SalvageDrone, UnitKind.ScrapMender }),
        ("2 Rockbacks+Maw", new[] { UnitKind.Rockback, UnitKind.Rockback, UnitKind.Cratermaw }),
        ("5 Cratermaws", new[] { UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw }),
    };

    static void Trace(int seed, int stance, int setup, int tier)
    {
        RunFight(seed, (CommandType)stance, tier, Setups[setup].kinds, 1, true);
    }

    static void Learn(int fights)
    {
        var w = Scenarios.Blank(7);
        var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 0, CommandType.DefensiveFormation);
        for (int f = 0; f < fights; f++)
        {
            var g = Scenarios.AddPack(w, sq.Centroid + new Vec2(0f, 22f), UnitKind.Cratermaw, UnitKind.Cratermaw, UnitKind.Cratermaw);
            w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = sq.Centroid + new Vec2(0f, 14f), Squads = { sq.Id } });
            int before = w.Reports.Count;
            for (int t = 0; t < 20 * 200 && w.Reports.Count == before; t++) w.Step();
            var r = w.Reports.LastOrDefault();
            Console.WriteLine(r?.Headline ?? "no report");
            if (r != null) foreach (var l in r.Lines.Where(l => l.Kind != 0)) Console.WriteLine($"    {l.Who}: {l.Text}");
            // Heal up between fights (rest) and bank XP like a base visit.
            foreach (var m in sq.Members)
            {
                if (m.Life == LifeState.Downed) Combat.Revive(w, m, 1f, null);
                if (m.Life == LifeState.Dead) Combat.Regrow(w, m, sq.Centroid);
                m.Injured = false; m.Hp = m.MaxHp; m.Doctrine.Bank();
            }
        }
        foreach (var m in sq.Members)
        {
            Console.WriteLine($"{m.Name,-8} {m.Def.Name,-12} DF tier {m.Doctrine.Tier(CommandType.DefensiveFormation)} xp {m.Doctrine.Total(CommandType.DefensiveFormation):0}");
            foreach (var p in m.Instinct.Params)
                Console.WriteLine($"      {p.Param,-14} {m.Instinct.Learned(CommandType.DefensiveFormation, EnemyFamily.Cratermaw, p.Param, m.Doctrine.Tier(CommandType.DefensiveFormation)):0.00} (default {p.Default:0.00})");
        }
    }

    // ------------------------------------------------------------------------------------------

    static void BossTrace(int seed, int tier, int gear, bool smart)
    {
        var w = Scenarios.BossArena(seed);
        var a = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(-6f, -30f), tier, CommandType.DefensiveFormation, gear);
        var b = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(6f, -30f), tier, CommandType.DefensiveFormation, gear);
        w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + new Vec2(0f, -12f), Squads = { a.Id, b.Id } });
        CommandType last = CommandType.DefensiveFormation;
        int alertIdx = 0;
        var counts = new Dictionary<SimEventType, int>();
        float dmgToBoss = 0f;
        for (int t = 0; t < 20 * 600 && !w.Victory; t++)
        {
            if (smart && t % 10 == 0)
            {
                var boss = w.Boss;
                CommandType want = CommandType.DefensiveFormation;
                if (boss.Phase == 1) want = boss.VentsExposed || boss.Staggered ? CommandType.Charge : CommandType.SpreadOut;
                else if (boss.Phase == 2) want = boss.MendersAlive > 0 ? CommandType.FocusFire : CommandType.DefensiveFormation;
                else if (boss.Phase == 3) want = boss.BlastPending ? CommandType.Stack : CommandType.Charge;
                if (want != last) { w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = want, Squads = { a.Id, b.Id } }); last = want; }
            }
            w.Step();
            foreach (var ev in w.Events)
            {
                counts.TryGetValue(ev.Type, out int n); counts[ev.Type] = n + 1;
                if (ev.Type == SimEventType.Hit && ev.B == w.Boss.Body.Id) dmgToBoss += ev.Value;
            }
            while (alertIdx < w.Alerts.Count) { Console.WriteLine($"[{w.Time,6:0.0}] ALERT {w.Alerts[alertIdx].Text}"); alertIdx++; }
            if (t % 60 == 0)
            {
                var all = a.Members.Concat(b.Members).ToList();
                Console.WriteLine($"t={w.Time,5:0} {w.Boss.PhaseName,-22} {w.Boss.StateName,-12} boss {w.Boss.Body.Hp,5:0} ({w.Boss.Body.HpFraction:P0}) vents {w.Boss.VentsExposed} adds {w.Boss.Group.Members.Count(m => m.IsActive) - 1} squadHP {all.Where(m => m.IsActive).Sum(m => m.Hp):0}/{all.Sum(m => m.MaxHp):0} down {all.Count(m => m.Life == LifeState.Downed)} dead {all.Count(m => m.Life == LifeState.Dead)} dps {dmgToBoss / Math.Max(1, w.Time):0}");
            }
        }
        Console.WriteLine(string.Join(", ", counts.Where(kv => kv.Key != SimEventType.Hit && kv.Key != SimEventType.Swing && kv.Key != SimEventType.Miss).Select(kv => $"{kv.Key}={kv.Value}")));
    }

    static void BakeTiming()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = World.Create(1337);
        Console.WriteLine($"World.Create: {sw.ElapsedMilliseconds} ms");
        sw.Restart();
        int n = w.Height.Verts;
        var v = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            var p = new Vec2(x, z);
            v[z * n + x] = (int)WorldGen.Ground(w, p) + w.GroundNoise.Fbm(p.X * 0.08f, p.Z * 0.08f, 3) + w.Height.Slope(p);
        }
        int size = 1024;
        float m2px = (float)w.Height.Size / size;
        double acc = 0;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float fx = (x + 0.5f) * m2px, fz = (y + 0.5f) * m2px;
            int ix = Math.Min((int)fx, n - 2), iz = Math.Min((int)fz, n - 2);
            float tx = fx - ix, tz = fz - iz;
            int i0 = iz * n + ix;
            float a = v[i0] + (v[i0 + 1] - v[i0]) * tx, b = v[i0 + n] + (v[i0 + n + 1] - v[i0 + n]) * tx;
            acc += a + (b - a) * tz;
        }
        Console.WriteLine($"Albedo bake math (1024^2): {sw.ElapsedMilliseconds} ms ({acc:0})");
    }

    static void GatherTest()
    {
        var w = Scenarios.Blank(3);
        var sq = Scenarios.AddSquad(w, w.BasePos + new Vec2(0f, 6f), 0, CommandType.DefensiveFormation);
        var node = new ResourceNode { Id = w.NextId(), Kind = ResourceKind.Ferrite, Pos = new Vec2(12f, 50f), Radius = 1.1f, Amount = 300f, MaxAmount = 300f, Label = "Test ferrite" };
        node.Cells = w.Nav.RasterCircle(node.Pos, 0.8f);
        w.Nav.Stamp(node.Cells, true, false, 0, +1);
        w.Nodes.Add(node);
        w.Issue(new PlayerOrder { Type = OrderType.Gather, TargetId = node.Id, Squads = { sq.Id } });
        for (int t = 0; t < 20 * 150; t++)
        {
            w.Step();
            if (t % 40 == 0)
                Console.WriteLine($"t={w.Time,5:0} task {sq.Task,-7} march {sq.MarchPoint} load {sq.CarryLoad:0}/{sq.CarryCapacity:0} node {node.Amount:0} stock ferrite {w.Stock[ResourceKind.Ferrite]:0} | " +
                    string.Join(" ", sq.Members.Select(m => $"{m.Def.Name.Substring(0, 3)}({m.Pos.X:0},{m.Pos.Z:0} {m.Intent.Kind.ToString().Substring(0, 2)}/{m.Action.ToString().Substring(0, 2)} {m.CarryLoad:0})")));
        }
    }

    static void P3(int seed, int tier)
    {
        var w = Scenarios.BossArena(seed);
        var a = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(-6f, -12f), tier, CommandType.DefensiveFormation, 2);
        var b = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(6f, -12f), tier, CommandType.DefensiveFormation, 2);
        for (int t = 0; t < 60; t++) w.Step();
        w.Boss.DebugSkipToPhase(w, 3);
        CommandType last = CommandType.DefensiveFormation;
        int blasts = 0;
        for (int t = 0; t < 20 * 90 && !w.Victory; t++)
        {
            if (t % 10 == 0)
            {
                var want = w.Boss.BlastPending || w.Boss.NextBlastIn < 2.5f ? CommandType.Stack : CommandType.Charge;
                if (want != last) { w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = want, Squads = { a.Id, b.Id } }); last = want; }
            }
            bool pending = w.Boss.BlastPending;
            w.Step();
            if (pending && !w.Boss.BlastPending)
            {
                blasts++;
                Console.WriteLine($"--- blast {blasts} at t={w.Time:0.0} boss at {w.Boss.Body.Pos}");
                foreach (var s in w.Structures.Where(s => s.Kind == StructureKind.Rampart)) Console.WriteLine($"    wall {s.Pos} dir {s.Dir} hp {s.Hp:0} owner {s.OwnerSquad?.Name}");
                foreach (var m in a.Members.Concat(b.Members))
                {
                    bool covered = !w.Nav.SightClear(w.Boss.Body.Pos, m.Pos);
                    Console.WriteLine($"    {m.Squad.Name,-9} {m.Def.Name,-12} {m.Life,-7} d={Vec2.Distance(m.Pos, w.Boss.Body.Pos),5:0.0} covered={covered,-5} intent={m.Intent.Kind} action={m.Action} hp={m.Hp:0}");
                }
            }
        }
    }

    static void Boss(int runs, int tier, int gear)
    {
        foreach (bool smart in new[] { false, true })
        {
            var agg = new Result();
            var phases = new int[5];
            for (int i = 0; i < runs; i++)
            {
                var w = Scenarios.BossArena(500 + i);
                var a = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(-6f, -30f), tier, CommandType.DefensiveFormation, gear);
                var b = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(6f, -30f), tier, CommandType.DefensiveFormation, gear);
                var ids = new[] { a.Id, b.Id };
                w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + new Vec2(0f, -12f), Squads = { a.Id, b.Id } });
                int t = 0;
                CommandType last = CommandType.DefensiveFormation;
                for (; t < 20 * 600 && !w.Victory; t++)
                {
                    if (smart && t % 10 == 0)
                    {
                        // Scripted commander that reads the boss: the doc's "best response" column.
                        var boss = w.Boss;
                        CommandType want = CommandType.DefensiveFormation;
                        if (boss.Phase == 1) want = boss.VentsExposed || boss.Staggered ? CommandType.Charge : CommandType.SpreadOut;
                        else if (boss.Phase == 2) want = boss.MendersAlive > 0 ? CommandType.FocusFire : CommandType.DefensiveFormation;
                        else if (boss.Phase == 3) want = boss.BlastPending || boss.NextBlastIn < 2.5f ? CommandType.Stack : CommandType.Charge;
                        if (want != last)
                        {
                            w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = want, Squads = { a.Id, b.Id } });
                            last = want;
                        }
                        // Right-click the Scrap Menders, as the doc's best response says.
                        if (boss.Phase == 2 && t % 40 == 0)
                        {
                            var mender = boss.Group.Members.FirstOrDefault(m => m.Kind == UnitKind.ScrapMender && m.IsActive);
                            if (mender != null && (a.FocusTargetId != mender.Id))
                                w.Issue(new PlayerOrder { Type = OrderType.Attack, TargetId = mender.Id, Squads = { a.Id, b.Id } });
                        }
                    }
                    w.Step();
                    if (a.Wiped && b.Wiped) break;
                }
                phases[w.Boss.Phase]++;
                agg.Runs++;
                if (w.Victory) agg.Wins++;
                if (a.Wiped && b.Wiped) agg.Wipes++;
                agg.Time += t / 20.0;
                foreach (var sq in new[] { a, b })
                foreach (var m in sq.Members)
                {
                    if (m.Life == LifeState.Dead) agg.Deaths++;
                    if (m.Injured || m.Life != LifeState.Active) agg.Downs++;
                    agg.HpLeft += m.Life == LifeState.Active ? m.HpFraction / 10.0 : 0;
                }
                Console.WriteLine($"  run {i}: {(w.Victory ? "WIN " : "lose")} phase {w.Boss.Phase} boss hp {w.Boss.Body.HpFraction:P0} t={t / 20.0:0}s deaths {a.Members.Count(m => m.Life == LifeState.Dead) + b.Members.Count(m => m.Life == LifeState.Dead)}");
            }
            Console.WriteLine($"{(smart ? "Smart commander" : "Passive (Defensive Formation only)")}, tier {tier}, gear {gear}: {agg}   ended in phase: {string.Join(" ", phases.Select((n, p) => $"P{p}={n}"))}");
        }
    }
}
