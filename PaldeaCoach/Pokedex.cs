using PKHeX.Core;

/// <summary>A species/form available in Scarlet/Violet, with its defensive matchups.</summary>
public sealed record DexEntry(ushort Species, byte Form, string Name, MoveType Type1, MoveType Type2, int BaseDef, int BaseSpD)
{
    public bool HasType(MoveType type) => Type1 == type || Type2 == type;

    public double Against(MoveType attack) => TypeChart.Multiplier(attack, Type1, Type2);

    /// <summary>Attacking types grouped by multiplier (4, 2, 0.5, 0.25, 0); neutral (1x) omitted.</summary>
    public ILookup<double, MoveType> Matchups =>
        TypeChart.All.Where(t => Against(t) != 1).ToLookup(Against);
}

public static class Pokedex
{
    /// <summary>Builds every species and form present in Scarlet/Violet, keyed by (species, form).</summary>
    public static Dictionary<(ushort Species, byte Form), DexEntry> BuildSV(GameStrings strings)
    {
        var table = PersonalTable.SV;
        var dex = new Dictionary<(ushort, byte), DexEntry>();
        for (ushort species = 1; species <= table.MaxSpeciesID; species++)
        {
            var formCount = table[species].FormCount;
            var formNames = FormConverter.GetFormList(species, strings.Types, strings.forms,
                GameInfo.GenderSymbolUnicode, EntityContext.Gen9);
            for (byte form = 0; form < formCount; form++)
            {
                var info = table.GetFormEntry(species, form);
                if (!info.IsPresentInGame)
                    continue;
                var name = strings.Species[species];
                if (form > 0 && form < formNames.Length && !string.IsNullOrWhiteSpace(formNames[form]))
                    name += $"-{formNames[form]}";
                dex[(species, form)] = new DexEntry(species, form, name, (MoveType)info.Type1, (MoveType)info.Type2, info.DEF, info.SPD);
            }
        }
        return dex;
    }
}
