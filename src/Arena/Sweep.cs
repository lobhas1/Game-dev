using System.Globalization;
using System.Text;

namespace Arena;

public sealed record SweepRow(
    float HpScale, int Fights, double Median, double SwingPct, string EndMix,
    IReadOnlyList<(int Tier, double Median)> PerTier);

/// <summary>EXPLORATORY HP-scale calibration (spec §9): for each hpScale, a same-tier round-robin
/// over the corpus — same-tier median, swing %, end-reason mix, per-tier-class medians. No verdict.
/// The registered hpScale is chosen from this table by review.</summary>
public static class Sweep
{
    // Pure: no file I/O, so tests can drive it.
    public static List<SweepRow> Compute(IReadOnlyList<Kit> kits, IEnumerable<long> seeds, IEnumerable<float> hpScales, float mana)
    {
        var seedList = seeds.ToList();
        var tierClasses = kits.Select(k => k.Tier).Distinct().OrderBy(t => t).ToList();
        var rows = new List<SweepRow>();
        foreach (var scale in hpScales)
        {
            var res = Rounds.RoundRobin(kits, seedList, new FightConfig { HpScale = scale, Mana = mana }, sameTierOnly: true);
            int swings = res.Count(r => r.LeadChanges >= 1);
            string endMix = string.Join(" ", res.GroupBy(r => r.EndReason)
                .OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}:{e.Count()}"));
            var perTier = tierClasses
                .Select(t => (Tier: t, Median: Median(res.Where(r => r.TierA == t).Select(r => r.DurationSeconds))))
                .ToList();
            rows.Add(new SweepRow(scale, res.Count, Median(res.Select(r => r.DurationSeconds)),
                res.Count == 0 ? 0 : 100.0 * swings / res.Count, endMix, perTier));
        }
        return rows;
    }

    public static void Run(string kitsDir, IEnumerable<long> seeds, IEnumerable<float> hpScales, float mana, TextWriter outw)
    {
        var kitPaths = Directory.GetFiles(kitsDir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (kitPaths.Count < 2)
        {
            outw.WriteLine($"need at least 2 kits in {kitsDir}, found {kitPaths.Count}.");
            return;
        }
        var kits = kitPaths.Select(Kit.Load).ToList();
        var seedList = seeds.ToList();
        var tierClasses = kits.Select(k => k.Tier).Distinct().OrderBy(t => t).ToList();
        var rows = Compute(kits, seedList, hpScales, mana);

        var sb = new StringBuilder();
        sb.AppendLine("# Phase A v3 — HP-scale calibration sweep (EXPLORATORY)");
        sb.AppendLine();
        sb.AppendLine($"Same-tier round-robin over the corpus ({kits.Count} kits), both orderings, {seedList.Count} seeds, mana {F(mana)}. **Exploratory — no verdict, no PASS/FAIL.** The registered `hpScale` is chosen from this table by the human and reviewer, then written into the v3 spec.");
        sb.AppendLine();
        sb.AppendLine($"| hpScale | fights | same-tier median | swing % | end-reason mix | {string.Join(" | ", tierClasses.Select(t => $"T{t}vT{t}"))} |");
        sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", 5 + tierClasses.Count)));

        outw.WriteLine("=== CALIBRATION SWEEP (EXPLORATORY — same-tier, no verdict) ===");
        outw.WriteLine($"kits={kits.Count}  seeds={seedList.Count}  mana={F(mana)}  tier-classes=[{string.Join(",", tierClasses.Select(t => "T" + t))}]");
        foreach (var r in rows)
        {
            string perTier = string.Join(" | ", r.PerTier.Select(p => F(p.Median) + "s"));
            sb.AppendLine($"| {F(r.HpScale)} | {r.Fights} | {F(r.Median)}s | {P(r.SwingPct)} | {r.EndMix} | {perTier} |");
            outw.WriteLine($"  hpScale={F(r.HpScale)}  fights={r.Fights}  median={F(r.Median)}s  swings={P(r.SwingPct)}  ends=[{r.EndMix}]  " +
                $"per-tier=[{string.Join(" ", r.PerTier.Select(p => $"T{p.Tier}:{F(p.Median)}s"))}]");
        }

        string docDir = Path.Combine(PromptTemplate.RepoRoot(), "docs", "experiments");
        Directory.CreateDirectory(docDir);
        string docPath = Path.Combine(docDir, "2026-07-09-calibration-sweep.md");
        File.WriteAllText(docPath, sb.ToString());
        outw.WriteLine($"sweep doc: {docPath}");
    }

    private static double Median(IEnumerable<float> xs)
    {
        var s = xs.Select(x => (double)x).OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0;
    }

    private static string F(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);
    private static string P(double p) => p.ToString("0", CultureInfo.InvariantCulture) + "%";
}
