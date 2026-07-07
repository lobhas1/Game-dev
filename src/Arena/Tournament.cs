using System.Globalization;
using System.Text;

namespace Arena;

/// <summary>Round-robin over all kit pairs × seeds → results.csv + a scorecard evaluating the
/// pre-registered decision rule.</summary>
public static class Tournament
{
    public static void Run(string kitsDir, IEnumerable<long> seeds, TextWriter outw)
    {
        var kitPaths = Directory.GetFiles(kitsDir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (kitPaths.Count < 2)
        {
            outw.WriteLine($"need at least 2 kits in {kitsDir}, found {kitPaths.Count}.");
            return;
        }
        var kits = kitPaths.Select(Kit.Load).ToList();
        var seedList = seeds.ToList();

        // Play BOTH orderings per pair — first-mover advantage is decisive (frost-first and
        // ember-first give different winner/duration/end reason), so a single ordering biases the
        // decision rule's raw material by filename.
        var results = new List<FightResult>();
        for (int i = 0; i < kits.Count; i++)
            for (int j = i + 1; j < kits.Count; j++)
                foreach (var s in seedList)
                {
                    results.Add(FightEngine.Run(kits[i], kits[j], s));
                    results.Add(FightEngine.Run(kits[j], kits[i], s));
                }

        string arenaDir = PromptTemplate.ArenaDir();
        Directory.CreateDirectory(arenaDir);
        string csvPath = Path.Combine(arenaDir, "results.csv");

        var csv = new StringBuilder();
        csv.AppendLine("kitA,kitB,seed,duration,endReason,winner,castsA,castsB,distinctVerbs,statuses,leadChanges,dmgToA,dmgToB");
        foreach (var r in results)
            csv.AppendLine(string.Join(",",
                r.KitA, r.KitB, r.Seed, F(r.DurationSeconds), r.EndReason, r.Winner,
                r.CastsA, r.CastsB, r.DistinctVerbs, r.StatusesApplied, r.LeadChanges,
                F(r.DamageToA), F(r.DamageToB)));
        File.WriteAllText(csvPath, csv.ToString());

        outw.WriteLine($"wrote {csvPath} ({results.Count} fights over {kits.Count} kits × {seedList.Count} seeds)");
        outw.WriteLine();
        Scorecard(results, outw);
    }

    private static void Scorecard(List<FightResult> results, TextWriter outw)
    {
        var durations = results.Select(r => r.DurationSeconds).OrderBy(x => x).ToList();
        float median = Median(durations);
        int verbsOk = results.Count(r => r.DistinctVerbs >= 3);
        int minVerbs = results.Count == 0 ? 0 : results.Min(r => r.DistinctVerbs);
        int swings = results.Count(r => r.LeadChanges >= 1);
        double swingPct = results.Count == 0 ? 0 : 100.0 * swings / results.Count;

        // Distinct outcomes ignore the seed: with crit/evasion at zero no RNG is drawn, so replicate
        // seeds produce byte-identical fights. Reporting this keeps "N fights" from inflating the read.
        int distinctOutcomes = results
            .Select(r => string.Join("|", r.KitA, r.KitB, r.EndReason, r.Winner, F(r.DurationSeconds),
                r.CastsA, r.CastsB, r.DistinctVerbs, r.StatusesApplied, r.LeadChanges, F(r.DamageToA), F(r.DamageToB)))
            .Distinct().Count();

        bool c2 = median >= 15f && median <= 90f;
        bool c3 = results.Count > 0 && verbsOk == results.Count;
        bool c4 = swingPct >= 50.0;

        outw.WriteLine("=== SCORECARD — pre-registered decision rule ===");
        outw.WriteLine("(first-pass validity >=70%; median fight duration 15-90s sim-time; >=3 distinct");
        outw.WriteLine(" verbs firing per fight; >=50% of fights show a lead change or status-driven swing)");
        outw.WriteLine($"fights: {results.Count}  (distinct outcomes: {distinctOutcomes}; seeds replicate when no RNG is drawn)");
        outw.WriteLine("  [n/a ] 1. first-pass validity >=70%    (generate-mode metric; not measured offline)");
        outw.WriteLine($"  [{Mark(c2)}] 2. median duration 15-90s      median = {F(median)}s");
        outw.WriteLine($"  [{Mark(c3)}] 3. >=3 distinct verbs / fight  {verbsOk}/{results.Count} fights (min {minVerbs})");
        outw.WriteLine($"  [{Mark(c4)}] 4. >=50% show a lead swing     {swings}/{results.Count} ({swingPct.ToString("0", CultureInfo.InvariantCulture)}%)");
    }

    private static string Mark(bool b) => b ? "PASS" : "FAIL";

    private static float Median(List<float> xs) =>
        xs.Count == 0 ? 0
        : xs.Count % 2 == 1 ? xs[xs.Count / 2]
        : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2f;

    private static string F(float x) => x.ToString("0.0", CultureInfo.InvariantCulture);
}
