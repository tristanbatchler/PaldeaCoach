using PKHeX.Core;

/// <summary>A distinct defensive typing that exists in Scarlet/Violet, with the species/forms that have it.</summary>
public sealed record Typing(MoveType Type1, MoveType Type2, IReadOnlyList<DexEntry> Entries)
{
    public int Count => Entries.Count;
}

/// <summary>How a team handles one attacking type: who is hit super-effectively and who walls it.</summary>
public sealed record DefenceRow(MoveType Attack, IReadOnlyList<PartyMember> Weak, IReadOnlyList<PartyMember> Resist)
{
    /// <summary>Weak members not offset by a resisting/immune switch-in.</summary>
    public int Exposure => Math.Max(0, Weak.Count - Resist.Count);
}

/// <summary>Offensive and defensive type coverage of a team.</summary>
public sealed record CoverageReport(
    IReadOnlyList<PartyMember> Team,
    IReadOnlyDictionary<MoveType, double> BestVsMono,  // best multiplier any attack gets vs each mono type
    IReadOnlyList<(Typing Typing, double Best)> Unanswered, // typings nothing hits super-effectively, worst first
    IReadOnlyList<DefenceRow> Defence,
    int SpeciesCovered, int SpeciesTotal)
{
    public double OffencePercent => 100.0 * SpeciesCovered / SpeciesTotal;
    public int Exposure => Defence.Sum(d => d.Exposure);
    /// <summary>One number to compare teams: % of species hit super-effectively, minus a penalty per unanswered weakness.</summary>
    public double Score => OffencePercent - 4 * Exposure;
}

public sealed record Swap(PartyMember Out, PartyMember In, CoverageReport Result);

public static class Coverage
{
    public static List<Typing> Typings(IEnumerable<DexEntry> dex) =>
        dex.GroupBy(e => e.Type1 == e.Type2 ? (e.Type1, e.Type1) : e.Type1 < e.Type2 ? (e.Type1, e.Type2) : (e.Type2, e.Type1))
           .Select(g => new Typing(g.Key.Item1, g.Key.Item2, g.DistinctBy(e => e.Species).ToList()))
           .ToList();

    // Damaging moves whose damage ignores type effectiveness (fixed damage, counters, OHKOs), so they add no coverage.
    private static readonly HashSet<ushort> UntypedDamage =
        [12, 32, 49, 68, 69, 82, 90, 101, 117, 149, 162, 243, 283, 329, 368, 515, 717, 877, 894];

    /// <summary>A damaging move that benefits from type effectiveness, including variable-power ones like Low Kick or Electro Ball.</summary>
    public static bool IsTypedAttack(MoveData m) => m.Category != MoveCategory.Status && !UntypedDamage.Contains(m.Id);

    /// <summary>Attacking types a member can use. With <paramref name="withStab"/>, its own types count even if it hasn't learned such a move yet.</summary>
    public static IEnumerable<MoveType> AttackTypes(PartyMember m, bool withStab = false)
    {
        var types = m.Moves.Where(x => IsTypedAttack(x.Data)).Select(x => x.Type);
        if (withStab)
            types = types.Append(m.Dex.Type1).Append(m.Dex.Type2);
        return types.Distinct();
    }

    /// <param name="stabFor">Members whose STAB types count as attacks even without a matching move (e.g. low-level box candidates).</param>
    public static CoverageReport Analyse(IReadOnlyList<PartyMember> team, IReadOnlyList<Typing> typings,
        ISet<PartyMember>? stabFor = null)
    {
        var attacks = team.SelectMany(m => AttackTypes(m, stabFor?.Contains(m) == true)).Distinct().ToList();
        double Best(MoveType t1, MoveType t2) => attacks.Count == 0 ? 0 : attacks.Max(a => TypeChart.Multiplier(a, t1, t2));

        var bestVsMono = TypeChart.All.ToDictionary(t => t, t => Best(t, t));
        var scored = typings.Select(t => (Typing: t, Best: Best(t.Type1, t.Type2))).ToList();
        var unanswered = scored.Where(s => s.Best < 2).OrderBy(s => s.Best).ThenByDescending(s => s.Typing.Count).ToList();

        var defence = TypeChart.All.Select(a => new DefenceRow(a,
            team.Where(m => m.Dex.Against(a) > 1).ToList(),
            team.Where(m => m.Dex.Against(a) < 1).ToList())).ToList();

        var total = typings.Sum(t => t.Count);
        var covered = scored.Where(s => s.Best >= 2).Sum(s => s.Typing.Count);
        return new CoverageReport(team, bestVsMono, unanswered, defence, covered, total);
    }

    /// <summary>Move types ranked by how many currently-unanswered species they would hit super-effectively.</summary>
    public static List<(MoveType Type, int Species)> MissingAttackTypes(CoverageReport report) =>
        TypeChart.All
            .Select(a => (a, report.Unanswered.Where(u => TypeChart.Multiplier(a, u.Typing.Type1, u.Typing.Type2) >= 2).Sum(u => u.Typing.Count)))
            .Where(x => x.Item2 > 0)
            .OrderByDescending(x => x.Item2)
            .ToList();

    /// <summary>Tries swapping every team member for every candidate; best resulting team first. Candidates are credited with their STAB types.</summary>
    public static List<Swap> BestSwaps(IReadOnlyList<PartyMember> team, IEnumerable<PartyMember> candidates, IReadOnlyList<Typing> typings,
        ISet<PartyMember>? keep = null)
    {
        var baseline = Analyse(team, typings).Score;
        var swaps = new List<Swap>();
        foreach (var cand in candidates.DistinctBy(c => (c.Dex.Species, c.Dex.Form)))
            foreach (var outgoing in team.Where(m => keep?.Contains(m) != true))
            {
                var newTeam = team.Select(m => m == outgoing ? cand : m).ToList();
                var result = Analyse(newTeam, typings, new HashSet<PartyMember> { cand });
                if (result.Score > baseline)
                    swaps.Add(new Swap(outgoing, cand, result));
            }
        // Keep each candidate's best swap only.
        return swaps.GroupBy(s => s.In).Select(g => g.MaxBy(s => s.Result.Score)!)
                    .OrderByDescending(s => s.Result.Score).ToList();
    }

    /// <summary>Typings (from the whole SV dex) that would most improve the team if one member were replaced, assuming STAB moves.</summary>
    public static List<(Typing Typing, PartyMember Out, CoverageReport Result)> BestTypingsToAdd(
        IReadOnlyList<PartyMember> team, IReadOnlyList<Typing> typings, ISet<PartyMember>? keep = null)
    {
        var results = new List<(Typing, PartyMember, CoverageReport)>();
        foreach (var t in typings)
        {
            var dummy = team[0] with { Name = "?", Dex = t.Entries[0], Moves = [] };
            var best = team.Where(m => keep?.Contains(m) != true).Select(o => (o, r: Analyse(team.Select(m => m == o ? dummy : m).ToList(), typings, new HashSet<PartyMember> { dummy })))
                           .MaxBy(x => x.r.Score);
            results.Add((t, best.o, best.r));
        }
        return results.OrderByDescending(x => x.Item3.Score).ToList();
    }
}
