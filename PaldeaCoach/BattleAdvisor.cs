using PKHeX.Core;

public sealed record MoveOption(PartyMove Move, double Effectiveness, double Damage);

/// <summary>
/// How one party member fares against an opponent.
/// Damage is a relative estimate (comparable between options, not HP). Threat is the worst multiplier
/// the opponent's own types deal to this member, assuming it attacks with same-type moves.
/// </summary>
public sealed record Recommendation(PartyMember Member, IReadOnlyList<MoveOption> Moves, double Threat, double Score)
{
    public MoveOption? Best => Moves.Count > 0 ? Moves[0] : null;
}

public static class BattleAdvisor
{
    private const double StabBonus = 1.5;
    private const double CritMultiplier = 1.5;
    private const double MeanDamageRoll = 0.925; // each hit is scaled by a uniform random 85..100%
    private const double VariablePowerEstimate = 60;
    private const double MinThreat = 0.25; // immune to both opponent types still isn't risk-free (coverage moves)

    /// <summary>Ranks party members against an opponent, best matchup first.</summary>
    public static List<Recommendation> Rank(IEnumerable<PartyMember> party, DexEntry opponent) =>
        party.Select(m => Evaluate(m, opponent)).OrderByDescending(r => r.Score).ToList();

    private static Recommendation Evaluate(PartyMember member, DexEntry opponent)
    {
        var moves = member.Moves
            .Where(m => m.Data.Category != MoveCategory.Status && m.Data.Power is not null)
            .Select(m => Rate(member, m, opponent))
            .OrderByDescending(o => o.Damage)
            .ToList();

        var threat = Math.Max(member.Dex.Against(opponent.Type1), member.Dex.Against(opponent.Type2));
        var score = (moves.Count > 0 ? moves[0].Damage : 0) / Math.Max(threat, MinThreat);
        return new Recommendation(member, moves, threat, score);
    }

    /// <summary>Expected damage against a neutral target with base defences of 100 (for comparing moves).</summary>
    public static double ExpectedDamage(PartyMember member, PartyMove move)
    {
        if (!Coverage.IsTypedAttack(move.Data))
            return 0;
        var stab = member.Dex.HasType(move.Type) ? StabBonus : 1;
        var critFactor = 1 + (CritMultiplier - 1) * move.Data.CritChance;
        var attack = move.Data.Category == MoveCategory.Physical ? member.Attack : member.SpAttack;
        // Variable-power moves (Low Kick, Electro Ball, Grass Knot...) average roughly 60 power against typical targets.
        var power = move.Data.Power is null ? VariablePowerEstimate * (move.Data.Accuracy ?? 100) / 100.0 : move.Data.ExpectedPower;
        return power * critFactor * MeanDamageRoll * stab * attack / 100.0;
    }

    private static MoveOption Rate(PartyMember member, PartyMove move, DexEntry opponent)
    {
        var effectiveness = TypeChart.Multiplier(move.Type, opponent.Type1, opponent.Type2);
        var stab = member.Dex.HasType(move.Type) ? StabBonus : 1;
        var critFactor = 1 + (CritMultiplier - 1) * move.Data.CritChance;
        var (attack, defense) = move.Data.Category == MoveCategory.Physical
            ? (member.Attack, opponent.BaseDef)
            : (member.SpAttack, opponent.BaseSpD);

        // Expected value: damage is linear in each random factor, and they're independent per hit,
        // so E[damage] = E[power over landed hits] * E[crit factor] * E[roll] * fixed multipliers.
        var damage = move.Data.ExpectedPower * critFactor * MeanDamageRoll * stab * effectiveness
            * attack / Math.Max(defense, 1);
        return new MoveOption(move, effectiveness, damage);
    }
}
