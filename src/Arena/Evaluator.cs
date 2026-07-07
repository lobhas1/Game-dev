using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Arena;

public enum Verdict { Pass, Fail, Incomplete }

public sealed record BriefValidity(string Brief, int Generated, int FirstPass)
{
    public double Pct => Generated == 0 ? 0 : 100.0 * FirstPass / Generated;
}

/// <summary>Everything the verdict doc renders, all computed from files — no hand-authored numbers.</summary>
public sealed record EvaluationResult
{
    public required string PromptSha { get; init; }
    public required IReadOnlyList<string> Models { get; init; }
    public required IReadOnlyList<BriefValidity> PerBrief { get; init; }
    public bool C1HasData { get; init; }
    public int C1Generated { get; init; }
    public int C1FirstPass { get; init; }
    public double C1Pct => C1Generated == 0 ? 0 : 100.0 * C1FirstPass / C1Generated;
    public bool C1Pass { get; init; }
    public required IReadOnlyList<KeyValuePair<string, int>> Marginals { get; init; }
    public int FightCount { get; init; }
    public int DistinctOutcomes { get; init; }
    public double C2Median { get; init; }
    public bool C2Pass { get; init; }
    public int C3MinVerbs { get; init; }
    public int C3FightsOk { get; init; }
    public bool C3Pass { get; init; }
    public int C4Swings { get; init; }
    public double C4Pct { get; init; }
    public bool C4Pass { get; init; }
    public required IReadOnlyList<string> UnrecordedKits { get; init; }
    public Verdict Overall { get; init; }
    public required IReadOnlyList<string> ConfigKeys { get; init; }
    public bool ConfigMixed => ConfigKeys.Count > 1;
    public string ConfigDisplay { get; init; } = "";
}

/// <summary>Renders the pre-registered decision rule from committed data. The rule and these
/// interpretations are immutable (spec §9.2); a live failure gets a written diagnosis, not a tweak.</summary>
public static class Evaluator
{
    // Hash the LINE-ENDING-NORMALIZED text (CRLF and lone CR → LF): a Windows autocrlf checkout and
    // an LF checkout of the same committed prompt must hash identically, or generation.log entries
    // minted on one machine read as INCOMPLETE on another — defeating independent re-derivation.
    public static string Sha256Hex(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static EvaluationResult Evaluate(
        string generationLog, string resultsCsv, string promptText, IEnumerable<string> kitFilenames)
    {
        string promptSha = Sha256Hex(promptText);

        var matching = generationLog.Split('\n')
            .Select(l => GenerationLog.Parse(l.TrimEnd('\r')))
            .Where(e => e is not null).Select(e => e!)
            .Where(e => string.Equals(e.PromptSha, promptSha, StringComparison.Ordinal))
            .ToList();

        // C1 — first-pass validity over matching-hash rows only (stale-prompt rows excluded).
        int c1Gen = matching.Sum(e => e.Generated);
        int c1Fp = matching.Sum(e => e.FirstPass);
        bool c1HasData = matching.Count > 0 && c1Gen > 0;
        bool c1Pass = c1HasData && 100.0 * c1Fp / c1Gen >= 70.0;

        var perBrief = matching
            .GroupBy(e => e.Brief, StringComparer.Ordinal)
            .Select(g => new BriefValidity(g.Key, g.Sum(e => e.Generated), g.Sum(e => e.FirstPass)))
            .OrderBy(b => b.Brief, StringComparer.Ordinal).ToList();

        var marg = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in matching) foreach (var kv in e.Marginals) marg[kv.Key] = marg.GetValueOrDefault(kv.Key) + kv.Value;

        var models = matching.Select(e => e.Model).Where(m => m.Length > 0)
            .Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList();

        // Every kit in the corpus must appear in a matching-hash row's kits= list.
        var recorded = new HashSet<string>(matching.SelectMany(e => e.Kits).Select(FileName), StringComparer.OrdinalIgnoreCase);
        var unrecorded = kitFilenames.Select(FileName)
            .Where(k => !recorded.Contains(k)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

        // C2–C4 from results.csv, exactly as run.
        var fights = ParseFights(resultsCsv);
        var configKeys = fights.Select(f => f.ConfigKey).Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        string configDisplay = configKeys.Count == 0 ? "(no fights)"
            : configKeys.Count == 1 ? DescribeConfig(configKeys[0])
            : "MIXED — " + string.Join(" ; ", configKeys.Select(DescribeConfig));
        int fightCount = fights.Count;
        int distinct = fights.Select(f => f.OutcomeKey).Distinct(StringComparer.Ordinal).Count();
        double median = Median(fights.Select(f => f.Duration).OrderBy(x => x).ToList());
        bool c2 = fightCount > 0 && median >= 15.0 && median <= 90.0;
        int c3Ok = fights.Count(f => f.DistinctVerbs >= 3);
        int c3Min = fightCount == 0 ? 0 : fights.Min(f => f.DistinctVerbs);
        bool c3 = fightCount > 0 && c3Ok == fightCount;
        int swings = fights.Count(f => f.LeadChanges >= 1);
        double c4Pct = fightCount == 0 ? 0 : 100.0 * swings / fightCount;
        bool c4 = fightCount > 0 && c4Pct >= 50.0;

        Verdict overall =
            !c1HasData || unrecorded.Count > 0 ? Verdict.Incomplete
            : c1Pass && c2 && c3 && c4 ? Verdict.Pass
            : Verdict.Fail;

        return new EvaluationResult
        {
            PromptSha = promptSha,
            Models = models,
            PerBrief = perBrief,
            C1HasData = c1HasData, C1Generated = c1Gen, C1FirstPass = c1Fp, C1Pass = c1Pass,
            Marginals = marg.ToList(),
            FightCount = fightCount, DistinctOutcomes = distinct,
            C2Median = median, C2Pass = c2,
            C3MinVerbs = c3Min, C3FightsOk = c3Ok, C3Pass = c3,
            C4Swings = swings, C4Pct = c4Pct, C4Pass = c4,
            UnrecordedKits = unrecorded,
            Overall = overall,
            ConfigKeys = configKeys,
            ConfigDisplay = configDisplay
        };
    }

    // ── mode wrapper: read files, evaluate, write the verdict doc, print the scorecard ──
    public static int RunEvaluate(string kitsDir, TextWriter outw)
    {
        string arenaDir = PromptTemplate.ArenaDir();
        string genLog = ReadIfExists(Path.Combine(arenaDir, "generation.log"));
        string results = ReadIfExists(Path.Combine(arenaDir, "results.csv"));
        string prompt = PromptTemplate.LoadPrompt();
        var kits = Directory.Exists(kitsDir)
            ? Directory.GetFiles(kitsDir, "*.json").Select(p => Path.GetFileName(p)!).ToList()
            : new List<string>();

        var result = Evaluate(genLog, results, prompt, kits);
        if (result.ConfigMixed)
        {
            outw.WriteLine("ERROR: results.csv mixes fight configs — refusing to render a verdict over incommensurate fights:");
            foreach (var k in result.ConfigKeys) outw.WriteLine("  " + DescribeConfig(k));
            return 4;
        }

        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string docDir = Path.Combine(PromptTemplate.RepoRoot(), "docs", "experiments");
        Directory.CreateDirectory(docDir);
        string docPath = Path.Combine(docDir, $"{date}-phase-a-verdict.md");
        File.WriteAllText(docPath, FormatVerdictDoc(result, date));

        outw.WriteLine(FormatScorecard(result));
        outw.WriteLine($"verdict doc: {docPath}");
        return result.Overall switch { Verdict.Pass => 0, Verdict.Fail => 3, _ => 2 };
    }

    // ── formatting ──

    public static string FormatScorecard(EvaluationResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PHASE A VERDICT — pre-registered decision rule ===");
        sb.AppendLine($"prompt sha: {r.PromptSha}");
        sb.AppendLine($"config: {r.ConfigDisplay}");
        sb.AppendLine(r.C1HasData
            ? $"  [{Mark(r.C1Pass)}] C1 first-pass validity >=70%   {Pct(r.C1Pct)} ({r.C1FirstPass}/{r.C1Generated}, matching-hash runs)"
            : "  [----] C1 first-pass validity >=70%   NO matching-hash generation rows");
        sb.AppendLine($"  [{Mark(r.C2Pass)}] C2 median duration 15-90s      median={F(r.C2Median)}s  ({r.FightCount} fights, {r.DistinctOutcomes} distinct)");
        sb.AppendLine($"  [{Mark(r.C3Pass)}] C3 >=3 distinct verbs / fight  {r.C3FightsOk}/{r.FightCount} (min {r.C3MinVerbs})");
        sb.AppendLine($"  [{Mark(r.C4Pass)}] C4 >=50% show a lead swing     {r.C4Swings}/{r.FightCount} ({Pct(r.C4Pct)})");
        sb.AppendLine($"OVERALL: {r.Overall.ToString().ToUpperInvariant()}");
        if (r.Overall == Verdict.Incomplete)
        {
            if (!r.C1HasData) sb.AppendLine("  incomplete: no generation.log rows match the current prompt hash");
            if (r.UnrecordedKits.Count > 0)
                sb.AppendLine($"  incomplete: kits with no matching-hash generation record: {string.Join(", ", r.UnrecordedKits)}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string FormatVerdictDoc(EvaluationResult r, string date)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase A verdict");
        sb.AppendLine();
        sb.AppendLine($"- **Date:** {date}");
        sb.AppendLine($"- **Model(s):** {(r.Models.Count == 0 ? "(none)" : string.Join(", ", r.Models))}");
        sb.AppendLine($"- **Prompt sha:** `{r.PromptSha}`");
        sb.AppendLine($"- **Config:** {r.ConfigDisplay}");
        sb.AppendLine($"- **OVERALL:** {r.Overall.ToString().ToUpperInvariant()}");
        sb.AppendLine();
        sb.AppendLine("Generated from `arena/generation.log`, `arena/results.csv`, and the committed prompt. No number here is hand-authored.");
        sb.AppendLine();

        sb.AppendLine("## First-pass validity by brief (C1, matching-hash runs)");
        sb.AppendLine();
        sb.AppendLine("| brief | generated | first-pass | validity |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var b in r.PerBrief)
            sb.AppendLine($"| {b.Brief} | {b.Generated} | {b.FirstPass} | {Pct(b.Pct)} |");
        sb.AppendLine(r.C1HasData
            ? $"| **aggregate** | **{r.C1Generated}** | **{r.C1FirstPass}** | **{Pct(r.C1Pct)}** |"
            : "| **aggregate** | — | — | **no matching-hash data** |");
        sb.AppendLine();

        sb.AppendLine("## Verb marginals (matching-hash runs, recursive)");
        sb.AppendLine();
        sb.AppendLine(r.Marginals.Count == 0 ? "(none)" : string.Join(", ", r.Marginals.Select(kv => $"{kv.Key}={kv.Value}")));
        sb.AppendLine();

        sb.AppendLine("## Fights");
        sb.AppendLine();
        sb.AppendLine($"{r.FightCount} fights in `results.csv`; {r.DistinctOutcomes} distinct outcomes (seeds replicate when no RNG is drawn).");
        sb.AppendLine();

        sb.AppendLine("## Scorecard");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(FormatScorecard(r));
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Pinned interpretations (pre-registered, immutable)");
        sb.AppendLine();
        sb.AppendLine("> first-pass validity ≥70%; median fight duration 15–90s sim-time; ≥3 distinct verbs firing per fight; ≥50% of fights show a lead change or status-driven swing (sign flip of the HP difference, sampled 1/s).");
        sb.AppendLine();
        sb.AppendLine("- **C1** — first-pass-accepted ÷ generated, over all `generation.log` rows whose `promptSha` equals `sha256(prompts/proposal-oracle.md)`. ≥70% passes.");
        sb.AppendLine("- **C2** — median over all fights in `results.csv` as run (mirrors and seed replicates included). 15–90s passes.");
        sb.AppendLine("- **C3** — EVERY fight has ≥3 distinct verbs firing.");
        sb.AppendLine("- **C4** — ≥50% of fights have `leadChanges ≥ 1`.");
        sb.AppendLine("- **OVERALL** — PASS iff C1–C4 all PASS. INCOMPLETE if C1 has no matching-hash data, or any kit lacks a matching-hash generation record.");

        if (r.Overall == Verdict.Incomplete)
        {
            sb.AppendLine();
            sb.AppendLine("## Incomplete — what is missing");
            sb.AppendLine();
            if (!r.C1HasData) sb.AppendLine("- No `generation.log` rows match the current prompt hash; C1 cannot be computed.");
            if (r.UnrecordedKits.Count > 0)
                foreach (var k in r.UnrecordedKits)
                    sb.AppendLine($"- Kit `{k}` has no matching-hash generation record.");
        }

        if (r.Overall == Verdict.Fail)
            AppendDiagnosisTemplate(sb, r);

        return sb.ToString();
    }

    private static void AppendDiagnosisTemplate(StringBuilder sb, EvaluationResult r)
    {
        sb.AppendLine();
        sb.AppendLine("## Diagnosis (data only)");
        sb.AppendLine();
        sb.AppendLine("OVERALL is FAIL. Fill each cause from the evidence below; make **no** fixes here — raw data and this section go to review first.");
        sb.AppendLine();
        sb.AppendLine("### 1. Oracle composition");
        sb.AppendLine($"Evidence — aggregate first-pass {Pct(r.C1Pct)} ({r.C1FirstPass}/{r.C1Generated}); marginals: {(r.Marginals.Count == 0 ? "(none)" : string.Join(", ", r.Marginals.Select(kv => $"{kv.Key}={kv.Value}")))}.");
        sb.AppendLine("- ");
        sb.AppendLine();
        sb.AppendLine("### 2. Pricing (e.g. ifStatus amplify)");
        sb.AppendLine($"Evidence — median duration {F(r.C2Median)}s vs 15–90s window; {r.FightCount} fights, {r.DistinctOutcomes} distinct.");
        sb.AppendLine("- ");
        sb.AppendLine();
        sb.AppendLine("### 3. Arena constants");
        sb.AppendLine($"Evidence — lead swings {r.C4Swings}/{r.FightCount} ({Pct(r.C4Pct)}); distinct verbs min {r.C3MinVerbs}, {r.C3FightsOk}/{r.FightCount} fights ≥3.");
        sb.AppendLine("- ");
    }

    // ── results.csv parsing ──

    private sealed record FightRow(double Duration, int DistinctVerbs, int LeadChanges, string OutcomeKey, string ConfigKey);

    private static List<FightRow> ParseFights(string csv)
    {
        var rows = new List<FightRow>();
        foreach (var raw in csv.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("kitA", StringComparison.Ordinal)) continue;
            var c = line.Split(',');
            if (c.Length < 13) continue;
            if (!double.TryParse(c[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)) continue;
            int dv = ParseInt(c[8]);
            int lc = ParseInt(c[10]);
            // outcome key = every column except the seed (index 2)
            string key = string.Join("|", c.Where((_, i) => i != 2));
            // config key = hpScale|mana; a 13-column legacy row is the legacy config (flat 300 / 250).
            string configKey = c.Length >= 17 ? $"{c[13]}|{c[16]}" : "0|250";
            rows.Add(new FightRow(dur, dv, lc, key, configKey));
        }
        return rows;
    }

    private static string DescribeConfig(string key)
    {
        var p = key.Split('|');
        string scale = p.Length > 0 ? p[0] : "0";
        string mana = p.Length > 1 ? p[1] : "250";
        return scale is "0" or "0.0" ? $"legacy flat 300 HP, mana {mana}" : $"hpScale {scale}, mana {mana}";
    }

    private static int ParseInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double Median(List<double> xs) =>
        xs.Count == 0 ? 0
        : xs.Count % 2 == 1 ? xs[xs.Count / 2]
        : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2.0;

    private static string FileName(string s) => s.Replace('\\', '/').TrimEnd('/').Split('/').Last();
    private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : "";
    private static string Mark(bool b) => b ? "PASS" : "FAIL";
    private static string Pct(double p) => p.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    private static string F(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);
}
