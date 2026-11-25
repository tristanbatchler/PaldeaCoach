using PKHeX.Core;

/// <summary>One recommended TM use: teach <see cref="Move"/> to <see cref="Member"/> over <see cref="Replaces"/>.</summary>
public sealed record TmUse(int Tm, bool Craft, PartyMember Member, PartyMove Move, PartyMove? Replaces,
    double ScoreBefore, double ScoreAfter, double DamageNew, double DamageOld)
{
    public double ScoreGain => ScoreAfter - ScoreBefore;
}

/// <summary>Effect of feeding Exp. Candies to one member.</summary>
public sealed record CandyResult(PartyMember Member, int LevelBefore, int LevelAfter, string StatGain, IReadOnlyList<string> Unlocks);

/// <summary>
/// Works out how to spend bag items on the party: which TMs to teach (and over which move), who should eat the
/// Exp. Candies, and which evolution items apply to anything you own.
/// </summary>
public static class BagPlan
{
    // A swap that adds coverage may cost some damage, but not leave the slot much weaker than what it replaced.
    private const double MinDamageKeptForCoverage = 0.6;
    // A same-coverage swap must be a real damage upgrade, not noise.
    private const double MinDamageUpgrade = 1.15;
    // Moves whose damage depends on throwing away/consuming the held item (Fling, Natural Gift): never worth a TM here.
    private static readonly HashSet<ushort> ItemDependent = [363, 374];

    /// <summary>
    /// Greedy plan: repeatedly apply the single TM swap that most improves team coverage score (ties broken by damage gain),
    /// then same-coverage swaps that add at least 15% damage to the slot. Status moves are never overwritten — they're
    /// deliberate picks (Protect, Amnesia, Light Screen, Revival Blessing) that damage maths can't value.
    /// </summary>
    public static List<TmUse> PlanTMs(List<PartyMember> party, Dictionary<int, int> owned, ISet<int> craftable,
        IReadOnlyList<Typing> typings, IReadOnlyDictionary<ushort, MoveData> moveTable, GameStrings strings)
    {
        var team = party.ToList();
        var stock = new Dictionary<int, int>(owned);
        var crafted = new HashSet<int>(craftable);
        var plan = new List<TmUse>();

        while (true)
        {
            var baseline = Coverage.Analyse(team, typings).Score;
            TmUse? best = null;
            double bestKey = double.MinValue;
            foreach (var tm in stock.Where(s => s.Value > 0).Select(s => s.Key).Concat(crafted).Distinct())
            {
                var id = Learnsets.TMMove(tm);
                if (id == 0 || !moveTable.TryGetValue(id, out var data) || !Coverage.IsTypedAttack(data) || ItemDependent.Contains(id))
                    continue;
                var move = new PartyMove(id, strings.Move[id], (MoveType)MoveInfo.GetType(id, EntityContext.Gen9), data);
                foreach (var member in team)
                {
                    if (!Learnsets.CanLearnTM(member.Dex.Species, member.Dex.Form, tm) || member.Moves.Any(m => m.Id == id))
                        continue;
                    var dNew = BattleAdvisor.ExpectedDamage(member, move);
                    // Empty slot, or overwrite one damaging move.
                    var slots = member.Moves.Count < 4 ? [null] : member.Moves.Where(m => Coverage.IsTypedAttack(m.Data)).Cast<PartyMove?>().ToList();
                    foreach (var old in slots)
                    {
                        var dOld = old is null ? 0 : BattleAdvisor.ExpectedDamage(member, old);
                        var newMoves = member.Moves.Where(m => m != old).Append(move).ToList();
                        var newMember = member with { Moves = newMoves };
                        var newTeam = team.Select(m => m == member ? newMember : m).ToList();
                        var score = Coverage.Analyse(newTeam, typings).Score;
                        var gain = score - baseline;
                        var ok = gain > 0.05 ? dNew >= MinDamageKeptForCoverage * dOld
                               : gain > -0.05 && dNew >= MinDamageUpgrade * Math.Max(dOld, 1)
                                 // A pure damage swap mustn't duplicate a type the member already attacks with (loses coverage depth).
                                 && !member.Moves.Any(m => m != old && Coverage.IsTypedAttack(m.Data) && m.Type == move.Type);
                        if (!ok)
                            continue;
                        // Coverage first; within equal coverage, raw damage gained.
                        var key = gain * 1000 + (dNew - dOld) - (crafted.Contains(tm) && !stock.ContainsKey(tm) ? 0.5 : 0);
                        if (key > bestKey)
                        {
                            bestKey = key;
                            best = new TmUse(tm, !stock.ContainsKey(tm) || stock[tm] == 0, member, move, old, baseline, score, dNew, dOld);
                        }
                    }
                }
            }
            if (best is null)
                return plan;

            plan.Add(best);
            if (!best.Craft) stock[best.Tm]--;
            else crafted.Remove(best.Tm);
            var updated = best.Member with { Moves = best.Member.Moves.Where(m => m != best.Replaces).Append(best.Move).ToList() };
            team = team.Select(m => m == best.Member ? updated : m).ToList();
        }
    }

    public static readonly (string Name, int Exp)[] Candies =
        [("Exp. Candy XS", 100), ("Exp. Candy S", 800), ("Exp. Candy M", 3000), ("Exp. Candy L", 10000), ("Exp. Candy XL", 30000), ("Rare Candy", -1)];

    /// <summary>What happens if this member eats all the given experience (rare candies = whole levels).</summary>
    public static CandyResult FeedCandy(PartyMember member, int exp, int rareCandies, GameStrings strings,
        IReadOnlyDictionary<(ushort, byte), DexEntry> dex)
    {
        var pk = (PK9)member.Entity.Clone();
        var growth = pk.PersonalInfo.EXPGrowth;
        var before = pk.CurrentLevel;
        var newExp = Math.Min(pk.EXP + (uint)exp, Experience.GetEXP(100, growth));
        var level = (byte)Math.Min(100, Experience.GetLevel(newExp, growth) + rareCandies);
        if (rareCandies > 0) newExp = Math.Max(newExp, Experience.GetEXP(level, growth));
        var oldStats = (pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPA, pk.Stat_SPD, pk.Stat_SPE);
        pk.EXP = newExp;
        pk.CurrentLevel = level;
        pk.ResetPartyStats();
        var gain = $"HP+{pk.Stat_HPMax - oldStats.Item1} Atk+{pk.Stat_ATK - oldStats.Item2} Def+{pk.Stat_DEF - oldStats.Item3} " +
                   $"SpA+{pk.Stat_SPA - oldStats.Item4} SpD+{pk.Stat_SPD - oldStats.Item5} Spe+{pk.Stat_SPE - oldStats.Item6}";

        var unlocks = new List<string>();
        foreach (var (lvl, move) in Learnsets.LevelUp(pk.Species, pk.Form))
            if (lvl > before && lvl <= level)
                unlocks.Add($"Lv{lvl} learns {strings.Move[move]}");
        foreach (var evo in LevelEvolutions(pk.Species, pk.Form))
            if (evo.Level > before && evo.Level <= level)
                unlocks.Add($"Lv{evo.Level} EVOLVES into {dex.GetValueOrDefault((evo.Species, evo.Form))?.Name ?? strings.Species[evo.Species]}");
        return new CandyResult(member, before, level, gain, unlocks);
    }

    /// <summary>Plain level-up evolutions (no item/trade/friendship conditions) of a species.</summary>
    public static IEnumerable<(int Level, ushort Species, byte Form)> LevelEvolutions(ushort species, byte form)
    {
        var tree = EvolutionTree.GetEvolutionTree(EntityContext.Gen9);
        foreach (var evo in tree.Forward.GetForward(species, form).ToArray())
            if (evo.Method is EvolutionType.LevelUp && evo.Level > 0)
                yield return (evo.Level, evo.Species, (byte)evo.Form);
    }

    /// <summary>Evolutions triggered by using a bag item on the Pokémon (stones etc.).</summary>
    public static IEnumerable<(int Item, ushort Species, byte Form)> ItemEvolutions(ushort species, byte form)
    {
        var tree = EvolutionTree.GetEvolutionTree(EntityContext.Gen9);
        foreach (var evo in tree.Forward.GetForward(species, form).ToArray())
            if (evo.Method is EvolutionType.UseItem or EvolutionType.UseItemMale or EvolutionType.UseItemFemale)
                yield return (evo.Argument, evo.Species, (byte)evo.Form);
    }

    /// <summary>What a holdable item does for battling, and the damage multiplier it gives a move (1 = none).</summary>
    public static (string Effect, Func<PartyMove, double> Boost) HeldEffect(string item) => item switch
    {
        "Miracle Seed" => ("Grass moves 1.2x", Typed(MoveType.Grass)),
        "Charcoal" => ("Fire moves 1.2x", Typed(MoveType.Fire)),
        "Mystic Water" => ("Water moves 1.2x", Typed(MoveType.Water)),
        "Magnet" => ("Electric moves 1.2x", Typed(MoveType.Electric)),
        "Black Belt" => ("Fighting moves 1.2x", Typed(MoveType.Fighting)),
        "Poison Barb" => ("Poison moves 1.2x", Typed(MoveType.Poison)),
        "Soft Sand" => ("Ground moves 1.2x", Typed(MoveType.Ground)),
        "Hard Stone" => ("Rock moves 1.2x", Typed(MoveType.Rock)),
        "Metal Coat" => ("Steel moves 1.2x", Typed(MoveType.Steel)),
        "Fairy Feather" => ("Fairy moves 1.2x", Typed(MoveType.Fairy)),
        "Black Glasses" => ("Dark moves 1.2x", Typed(MoveType.Dark)),
        "Silk Scarf" => ("Normal moves 1.2x", Typed(MoveType.Normal)),
        "Sharp Beak" => ("Flying moves 1.2x", Typed(MoveType.Flying)),
        "Muscle Band" => ("physical moves 1.1x", m => m.Data.Category == MoveCategory.Physical ? 1.1 : 1),
        "Wise Glasses" => ("special moves 1.1x", m => m.Data.Category == MoveCategory.Special ? 1.1 : 1),
        "Life Orb" => ("all moves 1.3x, costs 10% HP per hit", _ => 1.3),
        "Leftovers" => ("heals 1/16 HP every turn — best tank item", None),
        "Shell Bell" => ("heals 1/8 of damage dealt", None),
        "Quick Claw" => ("20% chance to move first", None),
        "Heat Rock" => ("sun lasts 8 turns — useless unless someone uses Sunny Day", None),
        "Float Stone" => ("halves holder's weight (only matters vs Low Kick/Grass Knot/Heavy Slam)", None),
        "Oval Stone" => ("evolves Happiny when levelled at night — nothing else", None),
        "Power Bracer" or "Power Belt" or "Power Lens" or "Power Band" or "Power Anklet" or "Power Weight" =>
            ("+8 EVs per KO in one stat, halves Speed while held — training only", None),
        _ when item.EndsWith(" Berry") => ("one-time cure/heal when triggered", None),
        _ => ("no battle effect modelled", None),
    };

    private static Func<PartyMove, double> Typed(MoveType t) => m => m.Type == t ? 1.2 : 1;
    private static double None(PartyMove _) => 1;
}
