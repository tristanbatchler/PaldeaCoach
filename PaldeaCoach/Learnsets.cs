using PKHeX.Core;

/// <summary>Scarlet/Violet level-up moves and TM compatibility, from PKHeX's game data.</summary>
public static class Learnsets
{
    /// <summary>(level, move ID) pairs; level 0 = learned on evolving.</summary>
    public static List<(int Level, ushort Move)> LevelUp(ushort species, byte form)
    {
        var ls = LearnSource9SV.Instance.GetLearnset(species, form);
        var moves = ls.GetMoveRange(100);
        var levels = ls.GetAllLevels();
        var result = new List<(int, ushort)>();
        for (int i = 0; i < moves.Length; i++)
            result.Add((levels[i], moves[i]));
        return result;
    }

    /// <summary>Move taught by a TM number, or 0 if none. PKHeX's table has an unused TM000 at index 0, so TMxxx is at index xxx.</summary>
    public static ushort TMMove(int number)
    {
        var tms = PersonalInfo9SV.MachineMoves;
        return number >= 1 && number < tms.Length ? tms[number] : (ushort)0;
    }

    public static bool CanLearnTM(ushort species, byte form, int number) =>
        PersonalTable.SV.GetFormEntry(species, form).GetIsLearnTM(number);

    /// <summary>TM numbers in the bag, parsed from item names like "TM05".</summary>
    public static List<(int Number, int Count)> OwnedTMs(SAV9SV sav, GameStrings strings) =>
        sav.Inventory.SelectMany(p => p.Items)
            .Where(i => i.Index != 0 && i.Count > 0 && strings.Item[i.Index].StartsWith("TM"))
            .Select(i => (int.Parse(strings.Item[i.Index][2..]), i.Count))
            .ToList();
}
