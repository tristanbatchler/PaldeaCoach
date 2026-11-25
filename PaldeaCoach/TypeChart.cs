using PKHeX.Core;
using static PKHeX.Core.MoveType;

/// <summary>Standard type-effectiveness chart (Gen 6+, includes Fairy).</summary>
public static class TypeChart
{
    // The 18 battle types, in PKHeX's MoveType order.
    public static readonly MoveType[] All = Enumerable.Range(0, 18).Select(i => (MoveType)i).ToArray();

    // Per attacking type: (super effective against, not very effective against, no effect against).
    private static readonly Dictionary<MoveType, (MoveType[] Super, MoveType[] Weak, MoveType[] None)> Attacks = new()
    {
        [Normal]   = ([], [Rock, Steel], [Ghost]),
        [Fighting] = ([Normal, Rock, Steel, Ice, Dark], [Flying, Poison, Bug, Psychic, Fairy], [Ghost]),
        [Flying]   = ([Fighting, Bug, Grass], [Rock, Steel, Electric], []),
        [Poison]   = ([Grass, Fairy], [Poison, Ground, Rock, Ghost], [Steel]),
        [Ground]   = ([Poison, Rock, Steel, Fire, Electric], [Bug, Grass], [Flying]),
        [Rock]     = ([Flying, Bug, Fire, Ice], [Fighting, Ground, Steel], []),
        [Bug]      = ([Grass, Psychic, Dark], [Fighting, Flying, Poison, Ghost, Steel, Fire, Fairy], []),
        [Ghost]    = ([Ghost, Psychic], [Dark], [Normal]),
        [Steel]    = ([Rock, Ice, Fairy], [Steel, Fire, Water, Electric], []),
        [Fire]     = ([Bug, Steel, Grass, Ice], [Rock, Fire, Water, Dragon], []),
        [Water]    = ([Ground, Rock, Fire], [Water, Grass, Dragon], []),
        [Grass]    = ([Ground, Rock, Water], [Flying, Poison, Bug, Steel, Fire, Grass, Dragon], []),
        [Electric] = ([Flying, Water], [Grass, Electric, Dragon], [Ground]),
        [Psychic]  = ([Fighting, Poison], [Steel, Psychic], [Dark]),
        [Ice]      = ([Flying, Ground, Grass, Dragon], [Steel, Fire, Water, Ice], []),
        [Dragon]   = ([Dragon], [Steel], [Fairy]),
        [Dark]     = ([Ghost, Psychic], [Fighting, Dark, Fairy], []),
        [Fairy]    = ([Fighting, Dragon, Dark], [Poison, Steel, Fire], []),
    };

    public static double Multiplier(MoveType attack, MoveType defend)
    {
        var (super, weak, none) = Attacks[attack];
        if (none.Contains(defend)) return 0;
        if (super.Contains(defend)) return 2;
        if (weak.Contains(defend)) return 0.5;
        return 1;
    }

    /// <summary>Damage multiplier against a defender; pass type2 == type1 for mono-typed Pokémon.</summary>
    public static double Multiplier(MoveType attack, MoveType type1, MoveType type2) =>
        type1 == type2 ? Multiplier(attack, type1) : Multiplier(attack, type1) * Multiplier(attack, type2);
}
