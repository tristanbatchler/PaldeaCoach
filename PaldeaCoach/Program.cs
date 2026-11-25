using PKHeX.Core;

const string Usage = """
    Paldea Coach — reads a Scarlet/Violet save and answers team-building questions. Never writes to the save.

    Usage: paldea <command> [args] [save]

    The save defaults to $POKEMON_SAVE, then ./main, then ../main.

    Your team
      summary [save]                          trainer, party (moves, stats, items, matchups) and first boxes
      coverage [save] [--keep Name,Name]      party type coverage gaps, plus box swaps / typings that fill them
      bagplan [save]                          best TMs to teach, who eats Exp. Candies, held items, evolution items
      boxes [save]                            every boxed Pokémon with its moves (traded ones marked)
      bag [filter] [save]                     items in the bag, optionally filtered by name

    Battles
      vs <pokemon> [save]                     which party member and move to use against an opponent
      movecalc <member> <move,move,...> [save]  expected damage of candidate moves for a party member

    Planning
      whatif <pokemon> [save]                 team coverage if <pokemon> replaced each party member
      learn <pokemon> [save]                  level-up moves, and which TMs in your bag it can learn
      whocan <tm number> [save]               which party members can learn a TM (owned or not)
      craft [save] [--all]                    TM recipes: craftable now / what's missing (needs optional recipe data)
      dex [name]                              type matchups for every SV species/form (no save needed)

    Save research (for finding undocumented save data)
      savediff <old> <new>                    which save blocks changed between two saves
      keynames <hex key>...                   PKHeX's internal names for save block keys
      pkdiff <old> <new>                      raw byte diff of the lead party Pokémon between two saves
    """;

string[] commands = ["summary", "dex", "vs", "whatif", "learn", "whocan", "savediff", "keynames", "pkdiff",
    "craft", "movecalc", "bagplan", "bag", "boxes", "coverage"];

if (args is [] or ["help" or "--help" or "-h", ..])
{
    // No arguments and a save available: keep the old behaviour of printing the summary.
    if (args.Length == 0 && File.Exists(DefaultSave()))
        args = ["summary"];
    else
    {
        Console.WriteLine(Usage);
        return 0;
    }
}
if (!commands.Contains(args[0]))
{
    if (!File.Exists(args[0]))
    {
        Console.Error.WriteLine($"Unknown command '{args[0]}'. Run 'help' for the list of commands.");
        return 1;
    }
    args = ["summary", .. args]; // a bare save path summarises that save
}

var strings = GameInfo.GetStrings("en");
var dex = Pokedex.BuildSV(strings);

switch (args)
{
    case ["dex", ..]:
        var filter = args.Length > 1 ? args[1] : "";
        var matches = dex.Values.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var entry in matches)
            Console.WriteLine(DescribeEntry(entry));
        Console.WriteLine($"{matches.Count} of {dex.Count} species/forms");
        return 0;

    case ["vs"]:
        Console.Error.WriteLine("Usage: vs <pokemon name> [save path]");
        return 1;

    case ["vs", var opponentName, ..]:
    {
        var opponents = FindOpponents(opponentName);
        if (opponents.Count == 0)
        {
            Console.Error.WriteLine($"No Scarlet/Violet Pokémon matches '{opponentName}'.");
            return 1;
        }
        if (LoadSave(args.Length > 2 ? args[2] : DefaultSave()) is not { } sav)
            return 1;

        var party = Party.Get(sav, dex, MoveTable.Load(), strings);
        foreach (var opponent in opponents)
            PrintAdvice(opponent, BattleAdvisor.Rank(party, opponent));
        return 0;
    }

    case ["whatif", var whatName, ..]:
    {
        if (FindOpponents(whatName).FirstOrDefault() is not { } entry)
        {
            Console.Error.WriteLine($"No Scarlet/Violet Pokémon matches '{whatName}'.");
            return 1;
        }
        if (LoadSave(args.Length > 2 ? args[2] : DefaultSave()) is not { } sav)
            return 1;
        var team = Party.Get(sav, dex, MoveTable.Load(), strings);
        var typings = Coverage.Typings(dex.Values);
        var before = Coverage.Analyse(team, typings);
        Console.WriteLine($"Current team: {before.OffencePercent:0}% SE, {before.Exposure} unanswered weaknesses, score {before.Score:0.0}");
        Console.WriteLine($"{entry.Name} [{FormatTypes(entry)}] replacing:");
        var cand = team[0] with { Name = entry.Name, Dex = entry, Moves = [] };
        foreach (var outgoing in team)
        {
            var r = Coverage.Analyse(team.Select(m => m == outgoing ? cand : m).ToList(), typings, new HashSet<PartyMember> { cand });
            var fixedTypes = r.Defence.Zip(before.Defence).Where(p => p.First.Exposure < p.Second.Exposure).Select(p => TypeName(p.First.Attack));
            var newTypes = r.Defence.Zip(before.Defence).Where(p => p.First.Exposure > p.Second.Exposure).Select(p => TypeName(p.First.Attack));
            Console.WriteLine($"  {r.Score - before.Score,5:+0.0;-0.0}  {outgoing.Dex.Name,-10} {r.OffencePercent:0}% SE, {r.Exposure} unanswered" +
                $"  better vs: {string.Join(", ", fixedTypes)}  worse vs: {string.Join(", ", newTypes)}");
        }
        return 0;
    }

    case ["learn", var learnName, ..]:
    {
        if (FindOpponents(learnName).FirstOrDefault() is not { } entry)
        {
            Console.Error.WriteLine($"No Scarlet/Violet Pokémon matches '{learnName}'.");
            return 1;
        }
        var table = MoveTable.Load();
        string MoveLine(ushort id)
        {
            var d = table.GetValueOrDefault(id);
            var type = TypeName((MoveType)MoveInfo.GetType(id, EntityContext.Gen9));
            var power = d?.Power is { } p ? $"{p} pow" : "-";
            return $"{strings.Move[id],-16} {type,-9} {d?.Category,-8} {power,-7} {(d?.Accuracy is { } a ? a + "%" : "")}";
        }
        Console.WriteLine($"{entry.Name} [{FormatTypes(entry)}] level-up moves:");
        foreach (var (lvl, move) in Learnsets.LevelUp(entry.Species, entry.Form))
            Console.WriteLine($"  {(lvl == 0 ? "Evo" : "Lv" + lvl),-5} {MoveLine(move)}");
        if (LoadSave(args.Length > 2 ? args[2] : DefaultSave()) is not { } sav)
            return 1;
        Console.WriteLine("TMs in your bag it can learn:");
        foreach (var (num, count) in Learnsets.OwnedTMs(sav, strings).Where(t => Learnsets.CanLearnTM(entry.Species, entry.Form, t.Number)))
            Console.WriteLine($"  TM{num:000} {MoveLine(Learnsets.TMMove(num))} x{count}");
        return 0;
    }

    case ["whocan", var tmArg, ..] when int.TryParse(tmArg, out var tmNum):
    {
        if (LoadSave(args.Length > 2 ? args[2] : DefaultSave()) is not { } sav)
            return 1;
        Console.WriteLine($"TM{tmNum:000} {strings.Move[Learnsets.TMMove(tmNum)]}:");
        foreach (var m in Party.Get(sav, dex, MoveTable.Load(), strings))
            Console.WriteLine($"  {m.Dex.Name,-12} {(Learnsets.CanLearnTM(m.Dex.Species, m.Dex.Form, tmNum) ? "yes" : "no")}");
        return 0;
    }

    case ["savediff", var oldPath, var newPath]:
    {
        if (LoadSave(oldPath) is not { } a || LoadSave(newPath) is not { } b)
            return 1;
        var oldBlocks = a.Accessor.BlockInfo.ToDictionary(x => x.Key);
        foreach (var nb in b.Accessor.BlockInfo)
        {
            if (!oldBlocks.TryGetValue(nb.Key, out var ob)) { Console.WriteLine($"{nb.Key:X8} new block"); continue; }
            if (ob.Type != nb.Type || ob.Data.Length != nb.Data.Length)
            {
                Console.WriteLine($"{nb.Key:X8} {ob.Type}->{nb.Type} len {ob.Data.Length}->{nb.Data.Length}");
                continue;
            }
            if (ob.Type is SCTypeCode.Bool1 or SCTypeCode.Bool2 or SCTypeCode.Bool3)
                continue; // bool value lives in the type code, already compared above
            if (ob.Type is not (SCTypeCode.Object or SCTypeCode.Array))
            {
                if (!Equals(ob.GetValue(), nb.GetValue()))
                    Console.WriteLine($"{nb.Key:X8} {nb.Type,-7} {ob.GetValue()} -> {nb.GetValue()}");
                continue;
            }
            var diffs = Enumerable.Range(0, nb.Data.Length).Where(i => ob.Data[i] != nb.Data[i]).ToList();
            if (diffs.Count > 0)
                Console.WriteLine($"{nb.Key:X8} {nb.Type,-7} len {nb.Data.Length,7}: {diffs.Count} bytes differ, first at " +
                    string.Join(" ", diffs.Take(8).Select(i => $"0x{i:X}({ob.Data[i]:X2}->{nb.Data[i]:X2})")));
        }
        return 0;
    }

    case ["keynames", ..]:
    {
        var want = args.Skip(1).Select(x => Convert.ToUInt32(x, 16)).ToHashSet();
        foreach (var t in typeof(SAV9SV).Assembly.GetTypes())
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(uint) && want.Contains((uint)f.GetRawConstantValue()!))
                    Console.WriteLine($"{(uint)f.GetRawConstantValue()!:X8} {t.Name}.{f.Name}");
        return 0;
    }

    case ["pkdiff", var pa, var pb]:
    {
        var sa = LoadSave(pa)!; var sb = LoadSave(pb)!;
        var x = (PK9)sa.GetPartySlotAtIndex(0); var y = (PK9)sb.GetPartySlotAtIndex(0);
        Console.WriteLine($"{strings.Species[x.Species]} exp {x.EXP}->{y.EXP} friendship {x.CurrentFriendship}->{y.CurrentFriendship}");
        var da = x.Data; var db = y.Data;
        for (int i = 0; i < Math.Min(da.Length, db.Length); i++)
            if (da[i] != db[i]) Console.Write($"0x{i:X}:{da[i]:X2}->{db[i]:X2} ");
        Console.WriteLine();
        return 0;
    }

    case ["craft", ..]:
    {
        var showAll = args.Contains("--all");
        var path = args.Skip(1).FirstOrDefault(a => a != "--all") ?? DefaultSave();
        if (LoadSave(path) is not { } sav)
            return 1;
        if (!TmCrafting.HasRecipes)
        {
            Console.Error.WriteLine(TmCrafting.HowToEnable);
            return 1;
        }
        var (recipes, unknown) = TmCrafting.Load(strings);
        foreach (var u in unknown)
            Console.Error.WriteLine($"warning: recipe material not found in game items: {u}");
        var party = Party.Get(sav, dex, MoveTable.Load(), strings);
        var table = MoveTable.Load();
        var status = TmCrafting.Status(sav, recipes, strings).Where(s => showAll || s.Unlocked).ToList();

        string Users(TmRecipe r)
        {
            var move = Learnsets.TMMove(r.Number);
            var who = party.Where(m => Learnsets.CanLearnTM(m.Dex.Species, m.Dex.Form, r.Number))
                           .Select(m => m.Moves.Any(x => x.Id == move) ? $"{m.Dex.Name}(knows)" : m.Dex.Name).ToList();
            return who.Count == 0 ? "" : "  for: " + string.Join(", ", who);
        }
        string Label(TmRecipe r)
        {
            var id = Learnsets.TMMove(r.Number);
            var d = table.GetValueOrDefault(id);
            var info = $"{TypeName((MoveType)MoveInfo.GetType(id, EntityContext.Gen9))} {d?.Category}{(d?.Power is { } p ? $" {p}" : "")}";
            return $"TM{r.Number:000} {r.Move,-16} {info,-20}";
        }

        Console.WriteLine($"LP: {sav.LeaguePoints:N0}   Unlocked recipes: {TmCrafting.Unlocked(sav).Count}\n");
        Console.WriteLine("CAN CRAFT NOW");
        foreach (var s in status.Where(s => s.CanCraft))
            Console.WriteLine($"  {Label(s.Recipe)} {s.Recipe.LP,6:N0} LP  own {s.Owned}{Users(s.Recipe)}");
        Console.WriteLine();
        Console.WriteLine(showAll ? "MISSING SOMETHING (* = recipe not unlocked)" : "UNLOCKED BUT MISSING SOMETHING");
        foreach (var s in status.Where(s => !s.CanCraft))
        {
            var need = s.Short.Select(x => $"{x.Material.Name} {x.Have}/{x.Material.Count}").ToList();
            if (!s.EnoughLP) need.Add($"LP {sav.LeaguePoints:N0}/{s.Recipe.LP:N0}");
            var what = need.Count == 0 ? "have everything; recipe not unlocked yet" : "need " + string.Join(", ", need);
            Console.WriteLine($" {(s.Unlocked ? " " : "*")}{Label(s.Recipe)} {what}{Users(s.Recipe)}");
        }
        return 0;
    }

    case ["movecalc", var memberName, var moveList, ..]:
    {
        if (LoadSave(args.Length > 3 ? args[3] : DefaultSave()) is not { } sav)
            return 1;
        var table = MoveTable.Load();
        var member = Party.Get(sav, dex, table, strings).FirstOrDefault(m => m.Name.Contains(memberName, StringComparison.OrdinalIgnoreCase));
        if (member is null) { Console.Error.WriteLine($"No party member matches '{memberName}'."); return 1; }
        Console.WriteLine($"{member.Name} Lv{member.Level} (Atk {member.Attack}, SpA {member.SpAttack}), expected damage vs a neutral target, relative:");
        foreach (var name in moveList.Split(',', StringSplitOptions.TrimEntries))
        {
            var idx = strings.Move.ToList().FindIndex(m => m.Equals(name, StringComparison.OrdinalIgnoreCase));
            var id = (ushort)Math.Max(idx, 0);
            if (idx <= 0 || !table.TryGetValue(id, out var data)) { Console.WriteLine($"  {name}: unknown move"); continue; }
            var move = new PartyMove(id, strings.Move[id], (MoveType)MoveInfo.GetType(id, EntityContext.Gen9), data);
            var dmg = BattleAdvisor.ExpectedDamage(member, move);
            Console.WriteLine($"  {move.Name,-16} {TypeName(move.Type),-9} {data.Category,-8} pow {data.Power?.ToString() ?? "-",-4} acc {data.Accuracy?.ToString() ?? "-",-4} -> {dmg,6:0}");
        }
        return 0;
    }

    case ["bagplan", ..]:
    {
        if (LoadSave(args.Length > 1 ? args[1] : DefaultSave()) is not { } sav)
            return 1;
        PrintBagPlan(sav);
        return 0;
    }

    case ["bag", ..]:
    {
        var itemFilter = args.Length > 1 && !File.Exists(args[1]) ? args[1] : "";
        var bagSave = args.Skip(1).FirstOrDefault(File.Exists) ?? DefaultSave();
        if (LoadSave(bagSave) is not { } sav)
            return 1;
        foreach (var pouch in sav.Inventory)
            foreach (var item in pouch.Items.Where(i => i.Index != 0 && i.Count > 0))
                if (strings.Item[item.Index].Contains(itemFilter, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"  {pouch.Type,-10} {strings.Item[item.Index]} x{item.Count}");
        return 0;
    }

    case ["boxes", ..]:
    {
        if (LoadSave(args.Length > 1 ? args[1] : DefaultSave()) is not { } sav)
            return 1;
        foreach (var m in Party.GetBoxed(sav, dex, MoveTable.Load(), strings))
        {
            var traded = m.Entity.OriginalTrainerName != sav.OT || m.Entity.ID32 != sav.ID32 ? $" (traded, OT {m.Entity.OriginalTrainerName})" : "";
            Console.WriteLine($"  {Describe(m.Entity)} [{FormatTypes(m.Dex)}]{traded}: " + string.Join(", ", m.Moves.Select(x => x.Name)));
        }
        return 0;
    }

    case ["coverage", ..]:
    {
        var rest = args.Skip(1).ToList();
        var keepAt = rest.IndexOf("--keep");
        var keepNames = keepAt >= 0 && keepAt + 1 < rest.Count ? rest[keepAt + 1].Split(',', StringSplitOptions.TrimEntries) : [];
        if (keepAt >= 0)
            rest.RemoveRange(keepAt, Math.Min(2, rest.Count - keepAt));
        if (LoadSave(rest.Count > 0 ? rest[0] : DefaultSave()) is not { } sav)
            return 1;
        var moveTable = MoveTable.Load();
        var party = Party.Get(sav, dex, moveTable, strings);
        var keep = party.Where(m => keepNames.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToHashSet();
        PrintCoverage(party, Party.GetBoxed(sav, dex, moveTable, strings), keep);
        return 0;
    }

    case ["summary", ..]:
    {
        if (LoadSave(args.Length > 1 ? args[1] : DefaultSave()) is not { } sav)
            return 1;
        PrintSummary(sav);
        return 0;
    }

    default:
        Console.Error.WriteLine($"Missing or invalid arguments for '{args[0]}'. Run 'help' for usage.");
        return 1;
}

// $POKEMON_SAVE, else "main" in the current directory, else "../main" (running from inside PaldeaCoach/).
static string DefaultSave() =>
    Environment.GetEnvironmentVariable("POKEMON_SAVE") is { Length: > 0 } env ? env
    : File.Exists("main") ? "main"
    : "../main";

SAV9SV? LoadSave(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Save file not found: {Path.GetFullPath(path)}");
        Console.Error.WriteLine("Pass the save path as the last argument, or set POKEMON_SAVE to your Scarlet/Violet 'main' save file.");
        return null;
    }
    // SaveUtil detects the game/format from the file contents and decrypts it.
    if (SaveUtil.GetSaveFile(path) is SAV9SV sav)
        return sav;
    Console.Error.WriteLine("Not a recognised Scarlet/Violet save file.");
    return null;
}

// Exact species name returns all its forms ("meowth" -> every Meowth form); otherwise fall back to a substring match.
List<DexEntry> FindOpponents(string name)
{
    var exact = dex.Values.Where(e => strings.Species[e.Species].Equals(name, StringComparison.OrdinalIgnoreCase)
        || e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (exact.Count > 0)
        return exact.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && e.Form != 0)
            ? exact.Where(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList()
            : DistinctByTyping(exact);
    return DistinctByTyping(dex.Values.Where(e => e.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList());
}

// Cosmetic forms (e.g. Vivillon patterns) share typing and stats, so only advise once for them.
static List<DexEntry> DistinctByTyping(List<DexEntry> entries) =>
    entries.DistinctBy(e => (e.Species, e.Type1, e.Type2, e.BaseDef, e.BaseSpD)).ToList();

void PrintAdvice(DexEntry opponent, List<Recommendation> ranked)
{
    Console.WriteLine($"vs {opponent.Name} [{FormatTypes(opponent)}]");
    var best = ranked.FirstOrDefault(r => r.Best is not null);
    if (best is null)
        Console.WriteLine("  No party member has a damaging move.");
    else
        Console.WriteLine($"  Send {best.Member.Name} and use {best.Best!.Move.Name} ({FormatEffect(best.Best.Effectiveness)}).");

    Console.WriteLine();
    foreach (var r in ranked)
    {
        var move = r.Best is { } b ? $"{b.Move.Name} ({TypeName(b.Move.Type)}, {FormatEffect(b.Effectiveness)})" : "no damaging moves";
        Console.WriteLine($"  {r.Score,6:0}  {r.Member.Name,-12} best: {move,-36} takes {FormatMultiplier(r.Threat)} from {opponent.Name}'s types");
    }
    Console.WriteLine();
}

void PrintSummary(SAV9SV sav)
{
    Console.WriteLine($"Trainer: {sav.OT} (TID {sav.DisplayTID:000000})");
    Console.WriteLine($"Game:    {sav.Version}");
    Console.WriteLine($"Money:   {sav.Money:N0}");
    Console.WriteLine($"LP:      {sav.LeaguePoints:N0}");
    // Global walk counters (names from PKHeX). Observed: AIBOUNOAKASI ("partner's proof") rises ~460/min while walking
    // and rolls over at 10000 without changing the lead Pokémon's data, so it is NOT the Pawmo/Rellor/Bramblin
    // 1000-step evolution counter; that counter wasn't found in the save (may live only in memory).
    Console.WriteLine($"Walk:    partner counter {sav.Accessor.GetBlock(0x9C677EC6).GetValue()}/10000" +
        $" (temp {sav.Accessor.GetBlock(0x2523D6E1).GetValue()})");
    Console.WriteLine($"Played:  {sav.PlayedHours}h {sav.PlayedMinutes}m");
    Console.WriteLine();

    Console.WriteLine("Party:");
    foreach (var member in Party.Get(sav, dex, MoveTable.Load(), strings))
    {
        Console.WriteLine($"  {member.Slot}. {member.Name} Lv{member.Level} [{FormatTypes(member.Dex)}] Tera:{TypeName(member.TeraType)}");
        Console.WriteLine("     Moves: " + string.Join(", ", member.Moves.Select(m => $"{m.Name} ({TypeName(m.Type)})")));
        Console.WriteLine("     Defence: " + FormatMatchups(member.Dex));
        var pk = member.Entity;
        Console.WriteLine($"     Ability: {strings.Ability[pk.Ability]}  Item: {(pk.HeldItem == 0 ? "none" : strings.Item[pk.HeldItem])}  " +
            $"HP {pk.Stat_HPMax} Atk {pk.Stat_ATK} Def {pk.Stat_DEF} SpA {pk.Stat_SPA} SpD {pk.Stat_SPD} Spe {pk.Stat_SPE}  {strings.Natures[(int)pk.Nature]}" +
            (pk.StatNature != pk.Nature ? $" (minted: stats as {strings.Natures[(int)pk.StatNature]})" : ""));
    }

    var boxed = sav.BoxData.Where(pk => pk.Species != 0).ToList();
    Console.WriteLine();
    Console.WriteLine($"Boxes: {boxed.Count} Pokémon across {sav.BoxCount} boxes");
    foreach (var pk in boxed.Take(10))
        Console.WriteLine("  " + Describe(pk));
    if (boxed.Count > 10)
        Console.WriteLine($"  ... and {boxed.Count - 10} more");
}

void PrintCoverage(List<PartyMember> party, List<PartyMember> boxed, ISet<PartyMember> keep)
{
    var typings = Coverage.Typings(dex.Values);
    var report = Coverage.Analyse(party, typings);
    if (keep.Count > 0)
        Console.WriteLine($"Keeping: {Names(keep)}\n");
    string Names(IEnumerable<PartyMember> ms) => string.Join(", ", ms.Select(m => m.Dex.Name));
    string Typ(Typing t) => t.Type1 == t.Type2 ? TypeName(t.Type1) : $"{TypeName(t.Type1)}/{TypeName(t.Type2)}";

    Console.WriteLine("OFFENCE (moves your party actually knows)");
    foreach (var m in party)
        Console.WriteLine($"  {m.Dex.Name,-12} {string.Join(", ", Coverage.AttackTypes(m).Select(TypeName))}");
    Console.WriteLine($"  Super-effective vs {report.SpeciesCovered}/{report.SpeciesTotal} SV species/forms ({report.OffencePercent:0}%).");
    var resisted = report.BestVsMono.Where(kv => kv.Value < 2).OrderBy(kv => kv.Value).ToList();
    Console.WriteLine("  Mono types you can't hit super-effectively: " +
        string.Join(", ", resisted.Select(kv => $"{TypeName(kv.Key)} ({FormatMultiplier(kv.Value)})")));
    var walls = report.Unanswered.Where(u => u.Best < 1).ToList();
    if (walls.Count > 0)
        Console.WriteLine("  WALLS (everything you have is resisted): " +
            string.Join(", ", walls.Select(w => $"{Typ(w.Typing)} e.g. {w.Typing.Entries[0].Name}")));
    Console.WriteLine("  Attack types that would add the most:");
    foreach (var (type, n) in Coverage.MissingAttackTypes(report).Take(6))
        Console.WriteLine($"    {TypeName(type),-9} +{n} species");

    Console.WriteLine();
    Console.WriteLine("DEFENCE (weak = takes 2x+, resist = ½x or less / immune)");
    foreach (var d in report.Defence.OrderByDescending(d => d.Exposure).ThenByDescending(d => d.Weak.Count))
    {
        var flag = d.Exposure >= 2 ? "!!" : d.Exposure == 1 ? "! " : "  ";
        Console.WriteLine($"  {flag}{TypeName(d.Attack),-9} weak {d.Weak.Count} [{Names(d.Weak)}]  resist {d.Resist.Count} [{Names(d.Resist)}]");
    }
    Console.WriteLine($"  Unanswered weaknesses: {report.Exposure}   Team score: {report.Score:0.0}");

    Console.WriteLine();
    Console.WriteLine("FROM YOUR BOXES (best single swap each; box mons credited with STAB moves they'll learn)");
    var swaps = Coverage.BestSwaps(party, boxed, typings, keep);
    if (swaps.Count == 0)
        Console.WriteLine("  No boxed Pokémon improves coverage.");
    foreach (var s in swaps.Take(8))
        Console.WriteLine($"  {s.Result.Score - report.Score,5:+0.0}  {s.In.Dex.Name} Lv{s.In.Level} [{FormatTypes(s.In.Dex)}] in for {s.Out.Dex.Name}" +
            $"  -> {s.Result.OffencePercent:0}% SE, {s.Result.Exposure} unanswered");

    Console.WriteLine();
    Console.WriteLine("TYPINGS TO LOOK FOR (best single swap, any SV Pokémon with STAB moves)");
    foreach (var (t, outgoing, r) in Coverage.BestTypingsToAdd(party, typings, keep).Take(25))
        Console.WriteLine($"  {r.Score - report.Score,5:+0.0}  {Typ(t),-17} in for {outgoing.Dex.Name,-10} e.g. {string.Join(", ", t.Entries.OrderBy(e => SpeciesCategory.IsLegendary(e.Species) || SpeciesCategory.IsSubLegendary(e.Species) || SpeciesCategory.IsMythical(e.Species)).Take(4).Select(e => e.Name))}");
}

void PrintBagPlan(SAV9SV sav)
{
    var table = MoveTable.Load();
    var party = Party.Get(sav, dex, table, strings);
    var typings = Coverage.Typings(dex.Values);
    var bag = TmCrafting.BagCounts(sav);
    var start = Coverage.Analyse(party, typings);
    Console.WriteLine($"Team score now: {start.Score:0.0} ({start.OffencePercent:0}% of species hit super-effectively, {start.Exposure} unanswered weaknesses)\n");

    // 1. TMs
    var owned = Learnsets.OwnedTMs(sav, strings).ToDictionary(t => t.Number, t => t.Count);
    var (recipes, _) = TmCrafting.Load(strings);
    var craftable = TmCrafting.Status(sav, recipes, strings).Where(s => s.CanCraft).Select(s => s.Recipe.Number).ToHashSet();
    var plan = BagPlan.PlanTMs(party, owned, craftable, typings, table, strings);
    Console.WriteLine($"TMs ({owned.Count} kinds in bag, " +
        (TmCrafting.HasRecipes ? $"{craftable.Count} craftable now" : "crafting data not installed") + ") — best uses, in order:");
    if (plan.Count == 0)
        Console.WriteLine("  None improve the team.");
    foreach (var u in plan)
    {
        var relearn = u.Replaces is { } r && Learnsets.LevelUp(u.Member.Dex.Species, u.Member.Dex.Form).Any(l => l.Move == r.Id && l.Level <= u.Member.Level)
            ? " (can relearn later)" : u.Replaces is null ? "" : " (TM-only: gone for good)";
        var over = u.Replaces is { } o ? $"over {o.Name} ({u.DamageOld:0})" : "into empty slot";
        var why = u.ScoreGain > 0.05 ? $"score {u.ScoreBefore:0.0} -> {u.ScoreAfter:0.0}" : $"damage {u.DamageOld:0} -> {u.DamageNew:0}";
        Console.WriteLine($"  {(u.Craft ? "CRAFT " : "")}TM{u.Tm:000} {u.Move.Name} ({TypeName(u.Move.Type)}, dmg {u.DamageNew:0}) -> {u.Member.Dex.Name} {over}{relearn}  [{why}]");
    }

    // 2. Exp. Candies
    Console.WriteLine();
    var candyExp = 0; var rare = 0; var candyDesc = new List<string>();
    foreach (var (name, exp) in BagPlan.Candies)
    {
        var id = strings.Item.ToList().IndexOf(name);
        var n = id > 0 ? bag.GetValueOrDefault(id) : 0;
        if (n == 0) continue;
        candyDesc.Add($"{name} x{n}");
        if (exp < 0) rare += n; else candyExp += exp * n;
    }
    Console.WriteLine($"EXP CANDIES: {(candyDesc.Count == 0 ? "none" : string.Join(", ", candyDesc) + $" = {candyExp:N0} exp" + (rare > 0 ? $" + {rare} levels" : ""))}");
    if (candyDesc.Count > 0)
    {
        var results = party.Select(m => BagPlan.FeedCandy(m, candyExp, rare, strings, dex))
            .OrderByDescending(r => r.Unlocks.Any(u => u.Contains("EVOLVES"))).ThenByDescending(r => r.LevelAfter - r.LevelBefore).ToList();
        foreach (var r in results)
            Console.WriteLine($"  {r.Member.Dex.Name,-11} Lv{r.LevelBefore} -> Lv{r.LevelAfter}  {r.StatGain}" +
                (r.Unlocks.Count > 0 ? "  ** " + string.Join("; ", r.Unlocks) : ""));
    }

    // 3. Held items
    Console.WriteLine();
    Console.WriteLine("HELD ITEMS (damage = average over the member's attacking moves)");
    var holdable = sav.Inventory.Where(p => p.Type is InventoryType.Items or InventoryType.Berries)
        .SelectMany(p => p.Items).Where(i => i.Index != 0 && i.Count > 0).Select(i => strings.Item[i.Index])
        .Where(n => !n.StartsWith("Exp. Candy") && !n.EndsWith(" Stone") || n is "Float Stone" or "Oval Stone")
        .Distinct().ToList();
    foreach (var m in party)
    {
        var attacks = m.Moves.Where(x => Coverage.IsTypedAttack(x.Data)).ToList();
        double Avg(string item) => attacks.Count == 0 ? 0 : attacks.Average(x => BattleAdvisor.ExpectedDamage(m, x) * BagPlan.HeldEffect(item).Boost(x));
        var current = m.Entity.HeldItem == 0 ? "" : strings.Item[m.Entity.HeldItem];
        var baseDmg = Avg("");
        var bestBag = holdable.Select(i => (i, d: Avg(i))).MaxBy(x => x.d);
        Console.WriteLine($"  {m.Dex.Name,-11} holds {(current == "" ? "nothing" : current)}: {BagPlan.HeldEffect(current).Effect}" +
            (Avg(current) > baseDmg + 0.5 ? $" (avg dmg {baseDmg:0} -> {Avg(current):0})" : "") +
            (bestBag.d > Math.Max(baseDmg, Avg(current)) + 0.5 ? $"  | bag better: {bestBag.i} ({bestBag.d:0})" : ""));
    }
    Console.WriteLine("  Holdable in bag:");
    foreach (var i in holdable.Where(i => !i.EndsWith(" Berry")))
        Console.WriteLine($"    {i}: {BagPlan.HeldEffect(i).Effect}");
    var berries = holdable.Where(i => i.EndsWith(" Berry")).ToList();
    if (berries.Count > 0)
        Console.WriteLine($"    Berries ({string.Join(", ", berries)}): one-time status cure/heal");

    // 4. Evolution items
    Console.WriteLine();
    Console.WriteLine("EVOLUTION ITEMS IN BAG that work on something you own:");
    var any = false;
    foreach (var (m, where) in party.Select(m => (m, "party")).Concat(Party.GetBoxed(sav, dex, table, strings).Select(m => (m, "box"))))
        foreach (var (item, sp, form) in BagPlan.ItemEvolutions(m.Dex.Species, m.Dex.Form))
            if (bag.GetValueOrDefault(item) > 0)
            {
                any = true;
                var target = dex.GetValueOrDefault((sp, form));
                Console.WriteLine($"  {strings.Item[item]} -> {m.Name} Lv{m.Level} ({where}) becomes {target?.Name ?? strings.Species[sp]}" +
                    (target is null ? "" : $" [{FormatTypes(target)}]"));
            }
    if (!any)
        Console.WriteLine("  none");
}

string Describe(PKM pk)
{
    var species = strings.Species[pk.Species];
    var name = pk.IsNicknamed ? $"{pk.Nickname} ({species})" : species;
    var shiny = pk.IsShiny ? " ★" : "";
    var tera = pk is PK9 pk9 ? $" Tera:{TypeName(pk9.TeraType)}" : "";
    return $"{name}{shiny} Lv{pk.CurrentLevel} {strings.Natures[(int)pk.Nature]}{tera}";
}

string TypeName(MoveType t) => strings.Types[(int)t];

string FormatTypes(DexEntry e) =>
    e.Type1 == e.Type2 ? TypeName(e.Type1) : $"{TypeName(e.Type1)}/{TypeName(e.Type2)}";

static string FormatMultiplier(double m) => m switch { 0.25 => "¼x", 0.5 => "½x", _ => $"{m:0.##}x" };

static string FormatEffect(double m) => m switch
{
    0 => "no effect",
    > 1 => $"super effective {FormatMultiplier(m)}",
    < 1 => $"not very effective {FormatMultiplier(m)}",
    _ => "neutral",
};

string DescribeEntry(DexEntry e) => $"#{e.Species:0000} {e.Name} [{FormatTypes(e)}]\n    {FormatMatchups(e)}";

string FormatMatchups(DexEntry e)
{
    var m = e.Matchups;
    string Group(string label, double mult) =>
        m[mult].Any() ? $"{label}: {string.Join(", ", m[mult].Select(TypeName))}" : "";
    var parts = new[] { Group("4x", 4), Group("2x", 2), Group("½x", 0.5), Group("¼x", 0.25), Group("0x", 0) };
    return string.Join(" | ", parts.Where(p => p.Length > 0));
}
