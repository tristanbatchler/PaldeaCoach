using System.Reflection;

public enum MoveCategory { Status = 1, Physical = 2, Special = 3 }

/// <summary>
/// Battle data for a move.
/// Power is per hit, null for status and fixed/variable-damage moves (e.g. Low Kick, Seismic Toss).
/// Accuracy is null for never-miss moves. CritStage is the move's critical-hit stage bonus (6 = always crits).
/// PerHitAccuracy: each hit rolls accuracy and the move stops at the first miss (Triple Axel, Population Bomb).
/// EscalatingPower: hit k has Power * k (Triple Kick 10/20/30, Triple Axel 20/40/60).
/// </summary>
public sealed record MoveData(
    ushort Id, int? Power, int? Accuracy, MoveCategory Category,
    int MinHits = 1, int MaxHits = 1, int CritStage = 0,
    bool PerHitAccuracy = false, bool EscalatingPower = false)
{
    /// <summary>
    /// Expected total base power delivered, accounting for accuracy and the hit-count distribution:
    /// sum over hits k of P(hit k lands) * power of hit k.
    /// </summary>
    public double ExpectedPower
    {
        get
        {
            if (Power is not { } power)
                return 0;
            var accuracy = (Accuracy ?? 100) / 100.0;
            double total = 0;
            for (int k = 1; k <= MaxHits; k++)
            {
                var landed = PerHitAccuracy ? Math.Pow(accuracy, k) : accuracy * ProbabilityAtLeastHits(k);
                total += landed * (EscalatingPower ? power * k : power);
            }
            return total;
        }
    }

    /// <summary>Chance each hit is critical (Gen 7+): stage 0 = 1/24, 1 = 1/8, 2 = 1/2, 3+ = always.</summary>
    public double CritChance => CritStage switch { 0 => 1 / 24.0, 1 => 1 / 8.0, 2 => 1 / 2.0, _ => 1 };

    // P(the move rolls at least k hits) when the hit count is chosen once up front.
    private double ProbabilityAtLeastHits(int k)
    {
        if (k <= MinHits) return 1;
        if (k > MaxHits) return 0;
        // 2-5 hit moves (Gen 5+): 2 and 3 hits at 35% each, 4 and 5 hits at 15% each.
        if ((MinHits, MaxHits) == (2, 5))
            return k switch { 3 => 0.65, 4 => 0.30, _ => 0.15 };
        // Otherwise assume uniform over MinHits..MaxHits.
        return (MaxHits - k + 1) / (double)(MaxHits - MinHits + 1);
    }
}

public static class MoveTable
{
    /// <summary>Loads the embedded PokeAPI move CSVs, keyed by game move ID.</summary>
    public static IReadOnlyDictionary<ushort, MoveData> Load()
    {
        var meta = ReadCsv("move_meta.csv").ToDictionary(row => int.Parse(row["move_id"]));

        // PokeAPI uses power 0/1 as placeholders for fixed or variable damage (Ruination, Comeuppance, Hard Press).
        static int? Power(string s) => s.Length == 0 || int.Parse(s) <= 1 ? null : int.Parse(s);
        static int? Optional(string s) => s.Length == 0 ? null : int.Parse(s);

        var moves = new Dictionary<ushort, MoveData>();
        foreach (var row in ReadCsv("moves.csv"))
        {
            var id = int.Parse(row["id"]);
            if (id >= 10000) // PokeAPI uses 10000+ for non-mainline moves (e.g. Shadow moves)
                continue;

            var move = new MoveData((ushort)id, Power(row["power"]), Optional(row["accuracy"]),
                (MoveCategory)int.Parse(row["damage_class_id"]));
            if (meta.TryGetValue(id, out var m))
            {
                move = move with { CritStage = int.Parse(m["crit_rate"]) };
                if (m["min_hits"].Length > 0)
                    move = move with { MinHits = int.Parse(m["min_hits"]), MaxHits = int.Parse(m["max_hits"]) };
            }
            moves[(ushort)id] = ApplyCorrections(move);
        }
        return moves;
    }

    // Mechanics PokeAPI doesn't encode (per-hit accuracy, escalating power), and Gen 8/9 moves missing from move_meta.csv.
    private static MoveData ApplyCorrections(MoveData m) => m.Id switch
    {
        167 or 813 => m with { PerHitAccuracy = true, EscalatingPower = true }, // Triple Kick, Triple Axel
        860 => m with { MinHits = 1, MaxHits = 10, PerHitAccuracy = true },     // Population Bomb
        865 => m with { MinHits = 3, MaxHits = 3 },                             // Triple Dive
        888 => m with { MinHits = 2, MaxHits = 2 },                             // Twin Beam
        911 => m with { MinHits = 2, MaxHits = 2, Accuracy = null },            // Tachyon Cutter (CSV lists accuracy 0)
        870 => m with { CritStage = 6 },                                        // Flower Trick
        840 or 843 or 895 or 904 => m with { CritStage = 1 },                   // Esper Wing, Triple Arrows, Aqua Cutter, Ivy Cudgel
        _ => m,
    };

    // These PokeAPI CSVs are plain numeric/identifier columns with no quoted fields, so a simple split suffices.
    private static IEnumerable<Dictionary<string, string>> ReadCsv(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded {resource} is missing.");
        using var reader = new StreamReader(stream);
        var header = reader.ReadLine()?.Split(',') ?? throw new InvalidDataException($"{resource} is empty.");
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            if (fields.Length != header.Length)
                throw new InvalidDataException($"{resource}: expected {header.Length} columns, got {fields.Length}: {line}");
            yield return header.Zip(fields).ToDictionary(p => p.First, p => p.Second);
        }
    }
}
