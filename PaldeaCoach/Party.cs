using PKHeX.Core;

public sealed record PartyMove(ushort Id, string Name, MoveType Type, MoveData Data);

/// <summary>A battle-ready party member: its species data (types/matchups), Tera type, stats and learned moves.</summary>
public sealed record PartyMember(
    int Slot,
    string Name,
    int Level,
    DexEntry Dex,
    MoveType TeraType,
    int Attack,
    int SpAttack,
    IReadOnlyList<PartyMove> Moves,
    PK9 Entity);

public static class Party
{
    /// <summary>The player's current party in slot order, excluding eggs (they can't battle).</summary>
    public static List<PartyMember> Get(SAV9SV sav, IReadOnlyDictionary<(ushort, byte), DexEntry> dex,
        IReadOnlyDictionary<ushort, MoveData> moveTable, GameStrings strings)
    {
        var party = new List<PartyMember>();
        for (int slot = 0; slot < sav.PartyCount; slot++)
        {
            var pk = (PK9)sav.GetPartySlotAtIndex(slot);
            if (pk.Species == 0 || pk.IsEgg)
                continue;
            party.Add(FromEntity(pk, slot + 1, dex, moveTable, strings));
        }
        return party;
    }

    /// <summary>Every non-egg Pokémon in the boxes, as battle members (Slot = position in box storage, 1-based).</summary>
    public static List<PartyMember> GetBoxed(SAV9SV sav, IReadOnlyDictionary<(ushort, byte), DexEntry> dex,
        IReadOnlyDictionary<ushort, MoveData> moveTable, GameStrings strings) =>
        sav.BoxData.Select((pk, i) => (pk: (PK9)pk, i))
            .Where(x => x.pk.Species != 0 && !x.pk.IsEgg)
            .Select(x => FromEntity(x.pk, x.i + 1, dex, moveTable, strings))
            .ToList();

    public static PartyMember FromEntity(PK9 pk, int slot, IReadOnlyDictionary<(ushort, byte), DexEntry> dex,
        IReadOnlyDictionary<ushort, MoveData> moveTable, GameStrings strings)
    {
        var species = strings.Species[pk.Species];
        var name = pk.IsNicknamed ? $"{pk.Nickname} ({species})" : species;

        var moves = new List<PartyMove>();
        foreach (var move in (ReadOnlySpan<ushort>)[pk.Move1, pk.Move2, pk.Move3, pk.Move4])
        {
            if (move == 0)
                continue;
            if (!moveTable.TryGetValue(move, out var data))
                throw new InvalidDataException($"No move data for move ID {move} ({strings.Move[move]}).");
            var type = (MoveType)MoveInfo.GetType(move, EntityContext.Gen9);
            moves.Add(new PartyMove(move, strings.Move[move], type, data));
        }

        return new PartyMember(slot, name, pk.CurrentLevel, dex[(pk.Species, pk.Form)], pk.TeraType,
            pk.Stat_ATK, pk.Stat_SPA, moves, pk);
    }
}
