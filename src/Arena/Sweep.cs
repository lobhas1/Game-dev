using System.Globalization;
using System.Text;
using Spellcraft;

namespace Arena;

public sealed record SweepRow(
    float HpScale, int Fights, double Median, double SwingPct, string EndMix,
    IReadOnlyList<(int Tier, double Median)> PerTier);

public sealed record AmplifyRow(
    float AmplifyMajor, int Fights, double Median, double SwingPct, string EndMix,
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
        var kits = kitPaths.Select(p => Kit.Load(p)).ToList(); // hpScale axis loads at the default amplify
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

    // ── amplify axis (v4): fixed hpScale/mana, vary the ifStatus 'major' multiplier. The corpus is
    //    re-loaded (re-priced) per value — no re-generation, the closed-vocabulary architecture. ──

    // Pure over its inputs (loads kit files, no doc I/O) so tests can drive it.
    public static List<AmplifyRow> ComputeAmplify(
        IReadOnlyList<string> kitPaths, IEnumerable<long> seeds, float hpScale, float mana, IEnumerable<float> amplifyValues)
    {
        var seedList = seeds.ToList();
        var rows = new List<AmplifyRow>();
        foreach (var amp in amplifyValues)
        {
            var overrides = new BalanceOverrides { AmplifyMajor = amp };
            var kits = kitPaths.Select(p => Kit.Load(p, overrides)).ToList();
            var tierClasses = kits.Select(k => k.Tier).Distinct().OrderBy(t => t).ToList();
            var config = new FightConfig { HpScale = hpScale, Mana = mana, AmplifyMajor = amp };
            var res = Rounds.RoundRobin(kits, seedList, config, sameTierOnly: true);
            int swings = res.Count(r => r.LeadChanges >= 1);
            string endMix = string.Join(" ", res.GroupBy(r => r.EndReason)
                .OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}:{e.Count()}"));
            var perTier = tierClasses
                .Select(t => (Tier: t, Median: Median(res.Where(r => r.TierA == t).Select(r => r.DurationSeconds))))
                .ToList();
            rows.Add(new AmplifyRow(amp, res.Count, Median(res.Select(r => r.DurationSeconds)),
                res.Count == 0 ? 0 : 100.0 * swings / res.Count, endMix, perTier));
        }
        return rows;
    }

    public static void RunAmplify(
        string kitsDir, IEnumerable<long> seeds, float hpScale, float mana, IEnumerable<float> amplifyValues, TextWriter outw)
    {
        var kitPaths = Directory.GetFiles(kitsDir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (kitPaths.Count < 2)
        {
            outw.WriteLine($"need at least 2 kits in {kitsDir}, found {kitPaths.Count}.");
            return;
        }
        var seedList = seeds.ToList();
        var rows = ComputeAmplify(kitPaths, seedList, hpScale, mana, amplifyValues.ToList());
        var tierClasses = rows.Count == 0 ? new List<int>() : rows[0].PerTier.Select(p => p.Tier).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Phase A v4 — amplify 'major' calibration sweep (EXPLORATORY)");
        sb.AppendLine();
        sb.AppendLine($"Same-tier round-robin over the corpus ({kitPaths.Count} kits), both orderings, {seedList.Count} seeds, hpScale {F(hpScale)}, mana {F(mana)}. The `major` amplify multiplier is re-priced per row — the oracle emits bands and the compiler prices them, so the same committed kits re-price for free. **Exploratory — no verdict, no PASS/FAIL.** The `2.5` row is the control and reproduces v3's criterion (median 13.9s, swing 76.9%). The registered `amplifyMajor` is chosen from this table by the human and reviewer, then written into the v4 spec.");
        sb.AppendLine();
        sb.AppendLine($"| amplifyMajor | fights | same-tier median | swing % | end-reason mix | {string.Join(" | ", tierClasses.Select(t => $"T{t}vT{t}"))} |");
        sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", 5 + tierClasses.Count)));

        outw.WriteLine("=== AMPLIFY SWEEP (EXPLORATORY — same-tier, no verdict) ===");
        outw.WriteLine($"kits={kitPaths.Count}  seeds={seedList.Count}  hpScale={F(hpScale)}  mana={F(mana)}  tier-classes=[{string.Join(",", tierClasses.Select(t => "T" + t))}]");
        foreach (var r in rows)
        {
            string perTier = string.Join(" | ", r.PerTier.Select(p => F(p.Median) + "s"));
            sb.AppendLine($"| {FA(r.AmplifyMajor)} | {r.Fights} | {F(r.Median)}s | {P1(r.SwingPct)} | {r.EndMix} | {perTier} |");
            outw.WriteLine($"  amplifyMajor={FA(r.AmplifyMajor)}  fights={r.Fights}  median={F(r.Median)}s  swings={P1(r.SwingPct)}  ends=[{r.EndMix}]  " +
                $"per-tier=[{string.Join(" ", r.PerTier.Select(p => $"T{p.Tier}:{F(p.Median)}s"))}]");
        }

        string docDir = Path.Combine(PromptTemplate.RepoRoot(), "docs", "experiments");
        Directory.CreateDirectory(docDir);
        string docPath = Path.Combine(docDir, "2026-07-10-amplify-sweep.md");
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
    // One-decimal percent for the amplify sweep, so the 2.5 control row matches v3's criterion
    // swing (76.9%) verbatim rather than rounding to a whole percent.
    private static string P1(double p) => p.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    // Amplify values can be two-decimal (2.25, 1.75) — format faithfully so the review picks the
    // value that was actually swept, not a one-decimal rounding of it.
    private static string FA(double x) => x.ToString("0.0#", CultureInfo.InvariantCulture);
}
