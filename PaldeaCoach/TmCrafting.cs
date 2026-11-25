using System.Globalization;
using System.Reflection;
using System.Text;
using PKHeX.Core;

public sealed record RecipeMaterial(string Name, int Count, int ItemId);

/// <summary>A TM Machine recipe: LP cost plus materials (Pokémon drops such as "Rockruff Rock").</summary>
public sealed record TmRecipe(int Number, string Move, int LP, IReadOnlyList<RecipeMaterial> Materials);

public sealed record CraftStatus(TmRecipe Recipe, bool Unlocked, int Owned, IReadOnlyList<(RecipeMaterial Material, int Have)> Short, bool EnoughLP)
{
    public bool CanCraft => Unlocked && EnoughLP && Short.Count == 0;
}

/// <summary>
/// TM crafting. Recipes come from Data/tm_recipes.csv if it was generated before building (tools/scrape_tm_recipes.py,
/// from pokemondb.net/scarlet-violet/tms; base game TM001-171,
/// DLC TMs aren't listed there). Unlocked recipes come from the save's KCanCraftTM### flags (Bool2 = unlocked).
/// </summary>
public static class TmCrafting
{
    // Spelling slips are corrected in tools/scrape_tm_recipes.py; this catches any that slip through later.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase);

    public static (List<TmRecipe> Recipes, List<string> UnknownMaterials) Load(GameStrings strings)
    {
        var itemIds = new Dictionary<string, int>();
        for (int i = 0; i < strings.Item.Count; i++)
            itemIds.TryAdd(Normalize(strings.Item[i]), i);

        var unknown = new List<string>();
        var rows = ReadCsv().GroupBy(r => (Tm: int.Parse(r[0]), Move: r[1], LP: int.Parse(r[2])));
        var recipes = rows.Select(g => new TmRecipe(g.Key.Tm, g.Key.Move, g.Key.LP, g.Select(r =>
        {
            var name = Aliases.GetValueOrDefault(r[3], r[3]);
            if (!itemIds.TryGetValue(Normalize(name), out var id))
            {
                unknown.Add($"TM{g.Key.Tm:000} {r[3]}");
                id = 0;
            }
            return new RecipeMaterial(name, int.Parse(r[4]), id);
        }).ToList())).OrderBy(r => r.Number).ToList();
        return (recipes, unknown);
    }

    /// <summary>TM numbers whose recipe the player has unlocked.</summary>
    public static HashSet<int> Unlocked(SAV9SV sav)
    {
        var blocks = sav.Accessor.BlockInfo.ToDictionary(b => b.Key);
        var unlocked = new HashSet<int>();
        foreach (var f in typeof(SaveBlockAccessor9SV).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (!f.IsLiteral || !f.Name.StartsWith("KCanCraftTM") || !int.TryParse(f.Name["KCanCraftTM".Length..], out var tm))
                continue;
            if (blocks.TryGetValue((uint)f.GetRawConstantValue()!, out var block) && block.Type == SCTypeCode.Bool2)
                unlocked.Add(tm);
        }
        return unlocked;
    }

    public static Dictionary<int, int> BagCounts(SAV9SV sav) =>
        sav.Inventory.SelectMany(p => p.Items).Where(i => i.Index != 0 && i.Count > 0)
            .GroupBy(i => i.Index).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));

    public static List<CraftStatus> Status(SAV9SV sav, IEnumerable<TmRecipe> recipes, GameStrings strings)
    {
        var unlocked = Unlocked(sav);
        var bag = BagCounts(sav);
        var ownedTms = Learnsets.OwnedTMs(sav, strings).ToDictionary(t => t.Number, t => t.Count);
        return recipes.Select(r =>
        {
            var shortOf = r.Materials.Select(m => (m, have: bag.GetValueOrDefault(m.ItemId))).Where(x => x.have < x.m.Count).ToList();
            return new CraftStatus(r, unlocked.Contains(r.Number), ownedTms.GetValueOrDefault(r.Number), shortOf, sav.LeaguePoints >= r.LP);
        }).ToList();
    }

    private static string Normalize(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Whether recipe data was embedded at build time (Data/tm_recipes.csv is optional).</summary>
    public static bool HasRecipes => Assembly.GetExecutingAssembly().GetManifestResourceInfo("tm_recipes.csv") is not null;

    public const string HowToEnable =
        "TM recipe data isn't included. To enable it, see \"TM crafting data\" in the README " +
        "(run PaldeaCoach/tools/scrape_tm_recipes.py, then rebuild).";

    private static IEnumerable<string[]> ReadCsv()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("tm_recipes.csv");
        if (stream is null)
            yield break;
        using var reader = new StreamReader(stream);
        reader.ReadLine(); // header: tm,move,lp,material,count
        while (reader.ReadLine() is { } line)
            if (line.Length > 0)
                yield return line.Split(',');
    }
}
