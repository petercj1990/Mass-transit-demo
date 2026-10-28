using System;
using System.Collections.Generic;
using System.Linq;
using Kinforge.Sim;

// Headless balance lab. Usage:
//   dotnet run -- smoke [seed] [minutes]
//   dotnet run -- fights [runs]
//   dotnet run -- boss [runs] [tier] [gear]
//   dotnet run -- learn [fights]
//   dotnet run -- titan [runs] [tierA] [tierB] [gear]   Titan Form on vs off in the Rustmaw raid
//   dotnet run -- raid [worlds] [tierA] [tierB] [gear] [dfOnly]   same, in real generated Breach Holds (terrain, walls, hatch);
//                                                     dfOnly=1 trains only Defensive Formation, like the campaign bot's squads
//   dotnet run -- trace
static class Program
{
    /// <summary>KF_TITAN=0 turns Titan Form off in every world the lab builds (A/B runs).</summary>
    public static readonly bool TitanOn = Environment.GetEnvironmentVariable("KF_TITAN") != "0";

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
            case "frames": Frames(args.Length > 1 ? args[1] : "frames.txt", Arg(2, 1), Arg(3, 0), Arg(4, 0), Arg(5, 60)); break;
            case "bossframes": BossFrames(args.Length > 1 ? args[1] : "boss.txt", Arg(2, 500), Arg(3, 3)); break;
            case "titan": TitanAb(Arg(1, 40), Arg(2, 3), Arg(3, 2), Arg(4, 2)); break;
            case "raid": RealRaids(Arg(1, 40), Arg(2, 3), Arg(3, 2), Arg(4, 2), Arg(5, 0) != 0); break;
            case "trace": Trace(Arg(1, 1), Arg(2, 1), Arg(3, 0), Arg(4, 0)); break;
            default: Console.WriteLine("unknown mode"); return 1;
        }
        return 0;
    }

    // ------------------------------------------------------------------------------------------

    static void Smoke(int seed, int minutes)
    {
        var w = World.Create(seed);
        w.TitanEnabled = Program.TitanOn;
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
        w.TitanEnabled = Program.TitanOn;
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
        w.TitanEnabled = Program.TitanOn;
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
        w.TitanEnabled = Program.TitanOn;
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

    static void DumpFrame(System.IO.TextWriter o, World w)
    {
        o.WriteLine($"F {w.Time:0.00}");
        foreach (var u in w.Units)
        {
            if (u.Life == LifeState.Dead && u.Faction == Faction.Enemy) continue;
            if (u.Garrisoned) continue;
            string tag = u.IsPlayer ? u.Role.ToString().Substring(0, 1) + (u.Squad?.ColorIndex ?? 0) : u.Kind.ToString().Substring(0, 2);
            o.WriteLine($"U {(u.IsPlayer ? 'P' : 'E')} {tag} {u.Pos.X:0.00} {u.Pos.Z:0.00} {u.Radius:0.00} {u.HpFraction:0.00} {(int)u.Life} {u.Facing:0.00} {(u.Untargetable ? 1 : 0)}");
        }
        foreach (var s in w.Structures)
        {
            if (s.Removed || s.Kind == StructureKind.Building) continue;
            o.WriteLine($"S {s.Kind} {s.Pos.X:0.00} {s.Pos.Z:0.00} {s.Dir.X:0.00} {s.Dir.Z:0.00} {s.HalfLen:0.00} {s.AreaRadius:0.00} {s.Faction}");
        }
        foreach (var t in w.Telegraphs)
        {
            if (t.Removed || t.Remaining <= 0f) continue;
            o.WriteLine($"T {t.Shape} {t.Pos.X:0.00} {t.Pos.Z:0.00} {t.Dir.X:0.00} {t.Dir.Z:0.00} {t.Radius:0.00} {t.Length:0.00} {t.Width:0.00} {t.ArcDeg:0}");
        }
        foreach (var p in w.Projectiles)
        {
            if (p.Removed) continue;
            o.WriteLine($"B {(p.Faction == Faction.Player ? 'P' : 'E')} {p.Pos.X:0.00} {p.Pos.Z:0.00}");
        }
    }

    static void Frames(string path, int seed, int stance, int setup, int seconds)
    {
        var w = Scenarios.Blank(seed);
        w.TitanEnabled = Program.TitanOn;
        var sq = Scenarios.AddSquad(w, new Vec2(40f, 40f), 2, (CommandType)stance);
        Scenarios.AddPack(w, new Vec2(40f, 62f), Setups[setup].kinds);
        w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = new Vec2(40f, 54f), Squads = { sq.Id } });
        using var o = new System.IO.StreamWriter(path);
        for (int t = 0; t < seconds * 20; t++)
        {
            w.Step();
            if (t % 20 == 0) DumpFrame(o, w);
            if (w.Reports.Count > 0 && t % 20 == 0) break;
        }
        Console.WriteLine(w.Reports.FirstOrDefault()?.Headline ?? "no result");
    }

    static void BossFrames(string path, int seed, int tier)
    {
        var w = Scenarios.BossArena(seed);
        w.TitanEnabled = Program.TitanOn;
        var a = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(-6f, -26f), tier, CommandType.DefensiveFormation, 2);
        var b = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(6f, -26f), tier, CommandType.DefensiveFormation, 2);
        w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + new Vec2(0f, -12f), Squads = { a.Id, b.Id } });
        using var o = new System.IO.StreamWriter(path);
        CommandType last = CommandType.DefensiveFormation;
        for (int t = 0; t < 20 * 400 && !w.Victory; t++)
        {
            if (t % 10 == 0)
            {
                var boss = w.Boss;
                CommandType want = CommandType.DefensiveFormation;
                if (boss.Phase == 1) want = boss.VentsExposed || boss.Staggered ? CommandType.Charge : CommandType.SpreadOut;
                else if (boss.Phase == 2) want = boss.MendersAlive > 0 ? CommandType.FocusFire : CommandType.DefensiveFormation;
                else if (boss.Phase == 3) want = boss.BlastPending || boss.NextBlastIn < 2.5f ? CommandType.Stack : CommandType.Charge;
                if (want != last) { w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = want, Squads = { a.Id, b.Id } }); last = want; }
            }
            w.Step();
            if (t % 10 == 0) DumpFrame(o, w);
            foreach (var ev in w.Events)
            {
                if (ev.Type == SimEventType.TitanRise || ev.Type == SimEventType.TitanClash || ev.Type == SimEventType.TitanShelter || ev.Type == SimEventType.BossCrash)
                    Console.WriteLine($"  t={w.Time:0.0} frame {t / 10} {ev.Type} {(ev.Type == SimEventType.TitanClash && ev.Value > 0.5f ? "(anvil)" : "")}");
            }
        }
        Console.WriteLine(w.Victory ? "victory" : "no victory");
    }

    static void BakeTiming()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = World.Create(1337);
        w.TitanEnabled = Program.TitanOn;
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
        w.TitanEnabled = Program.TitanOn;
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
        w.TitanEnabled = Program.TitanOn;
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
                w.TitanEnabled = Program.TitanOn;
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

    static PlayerOrder With(this PlayerOrder o, IEnumerable<int> ids)
    {
        o.Squads.AddRange(ids);
        return o;
    }

    sealed class TitanStats
    {
        public int Runs, Wins, Deaths, Grows, Clashes, Anvils, Sheltered, Cramped, NoRoom, Crashes, Violations;
        public double Time, BossHp, HealerDry, Exhausted, FirstDown, TankWound;
        public int[] EndPhase = new int[5];
        public int[] FirstDownRole = new int[5];
        public readonly Dictionary<string, double> DamageBy = new Dictionary<string, double>();
        public string FirstViolation;
    }

    /// <summary>War Hall level for lab raids (KF_HALL, default 1).</summary>
    static int Hall => int.TryParse(Environment.GetEnvironmentVariable("KF_HALL"), out int h) ? h : 1;

    /// <summary>Per-tick resource and survival diagnostics for a raid.</summary>
    static void Track(TitanStats st, World w, Squad[] squads)
    {
        foreach (var ev in w.Events)
        {
            if (ev.Type != SimEventType.Hit) continue;
            var t = w.FindUnitAny(ev.B);
            if (t == null || !t.IsPlayer) continue;
            var src = w.FindUnitAny(ev.A);
            string key = $"{t.Role} <- {(src != null ? src.Kind.ToString() : "env")}";
            st.DamageBy[key] = (st.DamageBy.TryGetValue(key, out double d) ? d : 0) + ev.Value;
        }
        foreach (var q in squads)
        foreach (var m in q.Members)
        {
            if (m.Life == LifeState.Downed && st.FirstDown == 0)
            {
                st.FirstDown = w.Time;
                st.FirstDownRole[(int)m.Role]++;
            }
            if (!m.IsActive) continue;
            if (m.Role == Role.Healer && m.ManaFraction < 0.15f) st.HealerDry += Balance.Dt;
            if (m.Exhausted) st.Exhausted += Balance.Dt;
        }
    }

    /// <summary>One Rustmaw raid: squad A at tierA, squad B at tierB, passive or scripted-smart commander.</summary>
    static TitanStats BossRun(int seed, int tierA, int tierB, int gear, bool smart, bool titan)
    {
        var st = new TitanStats { Runs = 1 };
        var w = Scenarios.BossArena(seed);
        w.TitanEnabled = titan;
        var a = Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(-6f, -30f), tierA, CommandType.DefensiveFormation, gear);
        // tierB < 0: the kin-band raids alone.
        var b = tierB >= 0 ? Scenarios.AddSquad(w, w.ArenaCenter + new Vec2(6f, -30f), tierB, CommandType.DefensiveFormation, gear) : null;
        var squads = b != null ? new[] { a, b } : new[] { a };
        var ids = squads.Select(q => q.Id).ToList();
        foreach (var q in squads) PlayerBase.ApplyHallLevel(q, Hall);
        w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + new Vec2(0f, -12f) }.With(ids));
        int t = 0;
        CommandType last = CommandType.DefensiveFormation;
        for (; t < 20 * 600 && !w.Victory; t++)
        {
            if (smart && t % 10 == 0)
            {
                var boss = w.Boss;
                CommandType want = CommandType.DefensiveFormation;
                if (boss.Phase == 1) want = boss.VentsExposed || boss.Staggered ? CommandType.Charge : CommandType.SpreadOut;
                else if (boss.Phase == 2) want = boss.MendersAlive > 0 ? CommandType.FocusFire : CommandType.DefensiveFormation;
                else if (boss.Phase == 3) want = boss.BlastPending || boss.NextBlastIn < 2.5f ? CommandType.Stack : CommandType.Charge;
                if (want != last)
                {
                    w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = want }.With(ids));
                    last = want;
                }
                if (boss.Phase == 2 && t % 40 == 0)
                {
                    var mender = boss.Group.Members.FirstOrDefault(m => m.Kind == UnitKind.ScrapMender && m.IsActive);
                    if (mender != null && a.FocusTargetId != mender.Id)
                        w.Issue(new PlayerOrder { Type = OrderType.Attack, TargetId = mender.Id }.With(ids));
                }
            }
            w.Step();
            foreach (var ev in w.Events)
            {
                switch (ev.Type)
                {
                    case SimEventType.TitanRise: st.Grows++; break;
                    case SimEventType.TitanClash: if (ev.Value > 0.5f) st.Anvils++; else st.Clashes++; break;
                    case SimEventType.TitanShelter: st.Sheltered++; break;
                    case SimEventType.TitanFade:
                        if (ev.Value == Titan.FadeCramped) st.Cramped++;
                        else if (ev.Value == Titan.FadeNoRoom) st.NoRoom++;
                        break;
                    case SimEventType.BossCrash: st.Crashes++; break;
                }
            }
            string bad = Titan.CheckInvariants(w);
            if (bad != null)
            {
                st.Violations++;
                st.FirstViolation ??= $"seed {seed} t={w.Time:0.0}: {bad}";
            }
            Track(st, w, squads);
            if (squads.All(q => q.Wiped)) break;
        }
        if (w.Victory) st.Wins++;
        st.BossHp = w.Boss.Body.HpFraction;
        st.EndPhase[Math.Min(4, w.Boss.Phase)]++;
        st.Time = t / 20.0;
        st.Deaths = squads.Sum(q => q.Members.Count(m => m.Life == LifeState.Dead));
        st.TankWound = a.Member(Role.Tank).Wound;
        return st;
    }

    /// <summary>Runs the smart raid commander in a real generated world's Breach Hold.</summary>
    static TitanStats RealRaid(int seed, int tierA, int tierB, int gear, bool titan, bool dfOnly = false)
    {
        var st = new TitanStats { Runs = 1 };
        var w = World.Create(seed);
        w.TitanEnabled = titan;
        w.Boss.OpenGate(w);
        Vec2 outward = (w.BasePos - w.ArenaCenter).Normalized;
        Vec2 outside = w.ArenaCenter + outward * (w.ArenaRadius + 8f);
        Squad Make(Vec2 at, int tier)
        {
            if (!dfOnly) return Scenarios.AddSquad(w, at, tier, CommandType.DefensiveFormation, gear);
            // Campaign-like Doctrine: veterans in Defensive Formation, untrained in everything else.
            var sq = Scenarios.AddSquad(w, at, 0, CommandType.DefensiveFormation, gear);
            foreach (var m in sq.Members)
            {
                m.Doctrine.Banked[(int)CommandType.DefensiveFormation] = Balance.DoctrineTierXp[tier];
                while (PlayerUnits.RefreshAbilitySlots(m) != null) { }
            }
            return sq;
        }
        var a = Make(w.Nav.NearestWalkable(outside + outward.Right * 4f, 6), tierA);
        var b = tierB >= 0 ? Make(w.Nav.NearestWalkable(outside - outward.Right * 4f, 6), tierB) : null;
        // The world's own starting kin-band stays home; these are the raiders.
        var squads = b != null ? new[] { a, b } : new[] { a };
        var ids = squads.Select(q => q.Id).ToList();
        foreach (var q in squads) PlayerBase.ApplyHallLevel(q, Hall);
        w.Issue(new PlayerOrder { Type = OrderType.Move, Pos = w.ArenaCenter + outward * 10f }.With(ids));
        int t = 0;
        bool started = false;
        CommandType last = CommandType.DefensiveFormation;
        for (; t < 20 * 400 && !w.Victory; t++)
        {
            var boss = w.Boss;
            if (boss.Active && t % 10 == 0)
            {
                CommandType want = CommandType.DefensiveFormation;
                if (boss.Phase == 1) want = boss.VentsExposed || boss.Staggered ? CommandType.Charge : CommandType.SpreadOut;
                else if (boss.Phase == 2) want = boss.MendersAlive > 0 ? CommandType.FocusFire : CommandType.DefensiveFormation;
                else if (boss.Phase == 3) want = boss.BlastPending || boss.NextBlastIn < 2.5f ? CommandType.Stack : CommandType.Charge;
                if (want != last)
                {
                    w.Issue(new PlayerOrder { Type = OrderType.SetCommand, Command = want }.With(ids));
                    last = want;
                }
                if (boss.Phase == 2 && t % 40 == 0)
                {
                    var mender = boss.Group.Members.FirstOrDefault(m => m.Kind == UnitKind.ScrapMender && m.IsActive);
                    if (mender != null) w.Issue(new PlayerOrder { Type = OrderType.Attack, TargetId = mender.Id }.With(ids));
                }
            }
            w.Step();
            foreach (var ev in w.Events)
            {
                switch (ev.Type)
                {
                    case SimEventType.TitanRise: st.Grows++; break;
                    case SimEventType.TitanClash: if (ev.Value > 0.5f) st.Anvils++; else st.Clashes++; break;
                    case SimEventType.TitanShelter: st.Sheltered++; break;
                    case SimEventType.TitanFade:
                        if (ev.Value == Titan.FadeCramped) st.Cramped++;
                        else if (ev.Value == Titan.FadeNoRoom) st.NoRoom++;
                        break;
                    case SimEventType.BossCrash: st.Crashes++; break;
                }
            }
            if (t % 20 == 0 && Titan.CheckInvariants(w) is string bad)
            {
                st.Violations++;
                st.FirstViolation ??= $"seed {seed} t={w.Time:0.0}: {bad}";
            }
            // As in the campaign, the attempt has failed once Rustmaw resets (nobody left in the arena).
            if (boss.Active) started = true;
            else if (started && boss.Phase == 0) break;
            Track(st, w, squads);
            if (squads.All(q => q.Wiped)) break;
        }
        if (w.Victory) st.Wins++;
        st.BossHp = w.Boss.Body.HpFraction;
        st.EndPhase[Math.Min(4, w.Boss.Phase)]++;
        st.Time = t / 20.0;
        st.Deaths = squads.Sum(q => q.Members.Count(m => m.Life == LifeState.Dead));
        return st;
    }

    static void RealRaids(int worlds, int tierA, int tierB, int gear, bool dfOnly)
    {
        Console.WriteLine($"Real-world raids: {worlds} generated worlds, smart commander, squad tiers {tierA}/{tierB}{(dfOnly ? " (Defensive Formation only)" : "")}, gear {gear}");
        foreach (bool titan in new[] { false, true })
        {
            var results = new TitanStats[worlds];
            System.Threading.Tasks.Parallel.For(0, worlds, i => results[i] = RealRaid(1000 + i, tierA, tierB, gear, titan, dfOnly));
            PrintTitan($"smart   titan {(titan ? "ON " : "off")}", results);
        }
    }

    static void PrintTitan(string label, TitanStats[] results)
    {
        var s = new TitanStats();
        foreach (var r in results)
        {
            s.Runs += r.Runs; s.Wins += r.Wins; s.Deaths += r.Deaths; s.Grows += r.Grows; s.Clashes += r.Clashes; s.Anvils += r.Anvils;
            s.Sheltered += r.Sheltered; s.Cramped += r.Cramped; s.NoRoom += r.NoRoom; s.Crashes += r.Crashes; s.Violations += r.Violations;
            s.Time += r.Time;
            s.BossHp += r.BossHp;
            s.HealerDry += r.HealerDry; s.Exhausted += r.Exhausted; s.FirstDown += r.FirstDown; s.TankWound += r.TankWound;
            for (int p = 0; p < 5; p++) { s.EndPhase[p] += r.EndPhase[p]; s.FirstDownRole[p] += r.FirstDownRole[p]; }
            foreach (var kv in r.DamageBy) s.DamageBy[kv.Key] = (s.DamageBy.TryGetValue(kv.Key, out double d) ? d : 0) + kv.Value;
            s.FirstViolation ??= r.FirstViolation;
        }
        double n = s.Runs;
        Console.WriteLine($"  {label}: win {100.0 * s.Wins / n,3:0}%  deaths {s.Deaths / n,4:0.0}  time {s.Time / n,4:0}s  " +
                          $"grows {s.Grows / n,4:0.0}  clashes {s.Clashes / n,4:0.00}  anvils {s.Anvils / n,4:0.00}  crashes {s.Crashes / n,4:0.00}  " +
                          $"sheltered {s.Sheltered / n,4:0.0}  cramped {s.Cramped}  no-room {s.NoRoom}  invariant violations {s.Violations}  " +
                          $"boss hp left {s.BossHp / n:P0}  ended in phase {string.Join(" ", s.EndPhase.Select((c, p) => $"P{p}={c}"))}");
        Console.WriteLine($"    healer dry {s.HealerDry / n:0}s  exhausted unit-s {s.Exhausted / n:0}  first down at {s.FirstDown / n:0}s " +
                          $"({string.Join(" ", s.FirstDownRole.Select((c, r) => $"{(Role)r}={c}"))})  tank wound {s.TankWound / n:P0}");
        if (Environment.GetEnvironmentVariable("KF_DMG") == "1")
            Console.WriteLine("    damage taken: " + string.Join("  ", s.DamageBy.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key} {kv.Value / n:0}")));
        if (s.FirstViolation != null) Console.WriteLine("    first violation: " + s.FirstViolation);
    }

    /// <summary>Paired seeds, Titan Form on vs off, passive and smart commanders: the feature's balance guardrails.</summary>
    static void TitanAb(int runs, int tierA, int tierB, int gear)
    {
        Console.WriteLine($"Titan Form A/B: {runs} paired seeds, squad tiers {tierA}/{tierB}, gear {gear}");
        foreach (bool smart in new[] { false, true })
        foreach (bool titan in new[] { false, true })
        {
            var results = new TitanStats[runs];
            System.Threading.Tasks.Parallel.For(0, runs, i => results[i] = BossRun(500 + i, tierA, tierB, gear, smart, titan));
            PrintTitan($"{(smart ? "smart  " : "passive")} titan {(titan ? "ON " : "off")}", results);
        }
    }
}