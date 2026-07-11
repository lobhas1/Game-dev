using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Arena;

public sealed record FusionCoverage(string Name, int Tier, int Satisfied, int Mappable, IReadOnlyList<string> Unsatisfied)
{
    public double Pct => Mappable == 0 ? 0 : 100.0 * Satisfied / Mappable;
}

public sealed record FusionEvalResult
{
    public required string NamingSha { get; init; }
    public required string MechanicsSha { get; init; }
    public int MatchingRecords { get; init; }
    public int ExcludedStale { get; init; }
    /// <summary>Matching-sha records excluded from scoring because they are stub-sourced (or carry no
    /// origin). Must be 0 for a live verdict — a Phase 2 precondition.</summary>
    public int StubExcluded { get; init; }
    // F1
    public int MechProposals { get; init; }
    public int FirstPassOk { get; init; }
    public double F1Pct => MechProposals == 0 ? 0 : 100.0 * FirstPassOk / MechProposals;
    public bool F1Pass { get; init; }
    // F2
    public int GatedCount { get; init; }
    public int ScoredCount { get; init; }
    public double MeanRealPct { get; init; }
    public double MeanDecoyPct { get; init; }
    public bool F2Pass { get; init; }
    public required IReadOnlyList<FusionCoverage> PerFusion { get; init; }
    // quantization pressure
    public required IReadOnlyList<KeyValuePair<string, int>> Quantization { get; init; }
    public int FusionsWithUnmappable { get; init; }
}

/// <summary>evaluate-fusions: computes F1 and F2 (+ shift-by-one decoy baseline + quantization
/// pressure) over matching-two-sha records, renders F3 as PENDING, writes a timestamped
/// no-clobber verdict doc. OVERALL is INCOMPLETE until F3 is appended (Phase 3 record-f3).</summary>
public static class FusionEvaluator
{
    public const string NamingFailedSentinel = "(naming failed)";

    // Pure: drive with hand-built records in tests.
    public static FusionEvalResult Evaluate(IReadOnlyList<FusionRecord> records, string namingSha, string mechanicsSha)
    {
        var matching = records.Where(r =>
            string.Equals(r.NamingSha, namingSha, StringComparison.Ordinal) &&
            string.Equals(r.MechanicsSha, mechanicsSha, StringComparison.Ordinal)).ToList();
        int excluded = records.Count - matching.Count;

        // Origin guard: stub records carry the same prompt shas as live ones, so the fingerprint
        // cannot tell them apart. Score ONLY live records; a canned text file must never pass F1/F2.
        var live = matching.Where(r => r.IsLive).ToList();
        int stubExcluded = matching.Count - live.Count;

        // F1 — first-try gate rate over LIVE records that reached the mechanics gate (naming succeeded).
        var mechProposals = live.Where(r => r.Concept.Name != NamingFailedSentinel).ToList();
        int firstPass = mechProposals.Count(r => r.FirstPassOk);
        bool f1 = mechProposals.Count > 0 && 100.0 * firstPass / mechProposals.Count >= 70.0;

        // F2 — coverage over gated fusions (first-pass or repaired), scored where ≥1 mappable tag.
        var gated = mechProposals.Where(r => r.Gated && r.Spell is not null).ToList();
        var covs = gated.Select(r =>
        {
            var (sat, map, unsat) = TagCoverage.Score(r.Concept.Tags, r.Spell!);
            return new FusionCoverage(r.Name, r.Tier, sat, map, unsat);
        }).ToList();

        var scored = gated.Where(r => TagCoverage.Score(r.Concept.Tags, r.Spell!).Mappable > 0)
            .OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        double meanReal = scored.Count == 0 ? 0 : scored.Average(r =>
        {
            var (sat, map, _) = TagCoverage.Score(r.Concept.Tags, r.Spell!);
            return (double)sat / map;
        });
        double meanDecoy = 0;
        if (scored.Count >= 2)
        {
            double sum = 0;
            for (int i = 0; i < scored.Count; i++)
            {
                var decoy = scored[(i + 1) % scored.Count].Spell!; // shift-by-one derangement
                var (sat, map, _) = TagCoverage.Score(scored[i].Concept.Tags, decoy);
                sum += map == 0 ? 0 : (double)sat / map;
            }
            meanDecoy = sum / scored.Count;
        }
        bool f2 = scored.Count > 0 && meanReal >= 0.70 && meanReal > meanDecoy;

        // quantization pressure — unmappable tags demanded across the gated concepts.
        var quant = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int withUnmappable = 0;
        foreach (var r in gated)
        {
            var unmapped = r.Concept.Tags.Where(TagCoverage.Unmappable.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (unmapped.Count > 0) withUnmappable++;
            foreach (var t in unmapped) quant[t] = quant.GetValueOrDefault(t) + 1;
        }

        return new FusionEvalResult
        {
            NamingSha = namingSha, MechanicsSha = mechanicsSha,
            MatchingRecords = matching.Count, ExcludedStale = excluded, StubExcluded = stubExcluded,
            MechProposals = mechProposals.Count, FirstPassOk = firstPass, F1Pass = f1,
            GatedCount = gated.Count, ScoredCount = scored.Count,
            MeanRealPct = 100.0 * meanReal, MeanDecoyPct = 100.0 * meanDecoy, F2Pass = f2,
            PerFusion = covs,
            Quantization = quant.ToList(), FusionsWithUnmappable = withUnmappable
        };
    }

    /// <summary>A corpus holding BOTH current-sha and stale-sha records is a prompt-generation mix
    /// (e.g. v3 records left in place after a v4 prompt change) — scoring the matching subset would
    /// hide the contamination. Archive the old corpus first.</summary>
    public static bool MixedShaCorpus(FusionEvalResult r) => r.MatchingRecords > 0 && r.ExcludedStale > 0;

    // ── mode wrapper ──
    public static int RunEvaluate(string fusionsDir, TextWriter outw)
    {
        string namingSha = Evaluator.Sha256Hex(File.ReadAllText(PromptTemplate.LocateRepoFile("prompts/naming-oracle.md")));
        string mechSha = Evaluator.Sha256Hex(File.ReadAllText(PromptTemplate.LocateRepoFile("prompts/fusion-mechanics-oracle.md")));
        var records = LoadRecords(fusionsDir);
        var r = Evaluate(records, namingSha, mechSha);

        if (MixedShaCorpus(r))
        {
            outw.WriteLine($"ERROR: corpus mixes prompt-sha generations — {r.MatchingRecords} record(s) match the current prompts (naming {namingSha[..8]}…, mechanics {mechSha[..8]}…) and {r.ExcludedStale} carry stale shas. Refusing to score a mixed corpus; archive the old generation first (e.g. git mv arena/fusions arena/fusions-v3-archive).");
            return 5;
        }

        outw.WriteLine(FormatScorecard(r));

        var now = DateTime.UtcNow;
        string docDir = Path.Combine(PromptTemplate.RepoRoot(), "docs", "experiments");
        Directory.CreateDirectory(docDir);
        string docPath = Path.Combine(docDir, $"{now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}-phase-b-fusion-verdict.md");
        if (File.Exists(docPath)) throw new IOException($"refusing to overwrite an existing verdict file: {docPath}");
        File.WriteAllText(docPath, FormatDoc(r, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        outw.WriteLine($"verdict doc: {docPath}");
        return 2; // INCOMPLETE until F3 appended
    }

    public static List<FusionRecord> LoadRecords(string dir)
    {
        var list = new List<FusionRecord>();
        if (!Directory.Exists(dir)) return list;
        foreach (var path in Directory.GetFiles(dir, "*.record.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is not null) list.Add(FusionRecord.FromJson(node));
        }
        return list;
    }

    public static string FormatScorecard(FusionEvalResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PHASE B FUSION VERDICT — pre-registered criteria ===");
        sb.AppendLine($"naming sha: {r.NamingSha}");
        sb.AppendLine($"mechanics sha: {r.MechanicsSha}");
        sb.AppendLine($"matching-sha records: {r.MatchingRecords}  (excluded stale: {r.ExcludedStale})");
        sb.AppendLine(r.StubExcluded > 0
            ? $"stub records EXCLUDED from scoring: {r.StubExcluded}  ⚠ corpus contaminated — a live verdict requires zero"
            : "stub records excluded from scoring: 0");
        sb.AppendLine($"  [{Mark(r.F1Pass)}] F1 gate validity >=70% first-try   {Pct(r.F1Pct)} ({r.FirstPassOk}/{r.MechProposals})");
        sb.AppendLine($"  [{Mark(r.F2Pass)}] F2 coverage >=70% AND > decoy       real={Pct(r.MeanRealPct)}  decoy={Pct(r.MeanDecoyPct)}  ({r.ScoredCount} scored)");
        sb.AppendLine($"  [PEND] F3 human blind match >=14/20           PENDING");
        sb.AppendLine($"quantization pressure: {(r.Quantization.Count == 0 ? "(none)" : string.Join(", ", r.Quantization.Select(kv => $"{kv.Key}={kv.Value}")))}  ({r.FusionsWithUnmappable} fusions)");
        sb.AppendLine("OVERALL: INCOMPLETE  (F3 pending)");
        return sb.ToString().TrimEnd();
    }

    public static string FormatDoc(FusionEvalResult r, string date)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase B fusion verdict");
        sb.AppendLine();
        sb.AppendLine($"- **Date:** {date}");
        sb.AppendLine("- **Protocol:** phase-b, spec docs/specs/2026-07-11-phase-b-fusion-mechanics.md");
        sb.AppendLine($"- **Naming prompt sha:** `{r.NamingSha}`");
        sb.AppendLine($"- **Mechanics prompt sha:** `{r.MechanicsSha}`");
        sb.AppendLine($"- **Machine:** f1pass={r.F1Pass.ToString().ToLowerInvariant()} f2pass={r.F2Pass.ToString().ToLowerInvariant()} f3=PENDING");
        sb.AppendLine("- **OVERALL:** INCOMPLETE (F3 pending)");
        sb.AppendLine();
        sb.AppendLine("Computed from `arena/fusions/*.record.json` (matching both prompt shas). No number hand-authored.");
        sb.AppendLine();

        sb.AppendLine("## Corpus origin");
        sb.AppendLine();
        sb.AppendLine(r.StubExcluded > 0
            ? $"⚠ **{r.StubExcluded} stub record(s) excluded from scoring.** Stub records carry the same prompt shas as live ones, so origin — not the fingerprint — is the guard; they never count toward F1/F2. A live verdict requires **zero stub records** (Phase 2 precondition) — this corpus is contaminated."
            : "All scored records are live-sourced; 0 stub records in the corpus.");
        sb.AppendLine();

        sb.AppendLine("## F1 — gate validity (first-try)");
        sb.AppendLine();
        sb.AppendLine($"{r.FirstPassOk}/{r.MechProposals} = {Pct(r.F1Pct)} — **{Mark(r.F1Pass)}** (threshold ≥70%, live records only). Excluded {r.ExcludedStale} stale-sha and {r.StubExcluded} stub record(s).");
        sb.AppendLine();

        sb.AppendLine("## F2 — semantic coverage");
        sb.AppendLine();
        sb.AppendLine($"Mean tag-coverage **{Pct(r.MeanRealPct)}** vs decoy baseline **{Pct(r.MeanDecoyPct)}** (shift-by-one derangement) — **{Mark(r.F2Pass)}** (threshold ≥70% AND strictly above decoy). {r.ScoredCount} fusion(s) had ≥1 mappable tag.");
        sb.AppendLine();
        sb.AppendLine("| fusion | tier | coverage | unsatisfied tags |");
        sb.AppendLine("|---|---:|---:|---|");
        foreach (var f in r.PerFusion.OrderBy(f => f.Name, StringComparer.Ordinal))
            sb.AppendLine($"| {f.Name} | {f.Tier} | {f.Satisfied}/{f.Mappable} ({Pct(f.Pct)}) | {(f.Unsatisfied.Count == 0 ? "—" : string.Join(", ", f.Unsatisfied))} |");
        sb.AppendLine();

        sb.AppendLine("## Quantization pressure (unmappable tags demanded)");
        sb.AppendLine();
        sb.AppendLine(r.Quantization.Count == 0
            ? "(none) — no gated concept demanded summon/transform/perceive."
            : string.Join(", ", r.Quantization.Select(kv => $"{kv.Key}={kv.Value}")) + $"  ({r.FusionsWithUnmappable} fusion(s) carried ≥1 unmappable tag — the widening signal, by design).");
        sb.AppendLine();

        sb.AppendLine("## F3 — human blind matching");
        sb.AppendLine();
        sb.AppendLine("**PENDING.** Run the quiz (concept + two clause lists, real vs same-tier decoy, sides randomized), 20 trials, ≥14/20. Append with `record-f3` — OVERALL stays INCOMPLETE until then.");
        sb.AppendLine();

        sb.AppendLine("## Pinned criteria (pre-registered, immutable)");
        sb.AppendLine();
        sb.AppendLine("- **F1** — ≥70% of fusion-mechanics proposals pass the gate first-try (one repair allowed; repairs and discards logged).");
        sb.AppendLine("- **F2** — mean tag-coverage ≥70% AND strictly greater than the decoy baseline (concepts scored against a derangement of the other fusions' clause lists). summon/transform/perceive are unmappable: excluded from the denominator, counted as quantization pressure.");
        sb.AppendLine("- **F3** — a stranger matches concept → clause list vs a same-tier decoy for 20 trials; ≥14/20 correct.");
        sb.AppendLine("- **OVERALL** — INCOMPLETE until F3 appended; then PASS iff F1, F2, F3 all pass, else FAIL.");
        return sb.ToString();
    }

    // ── Phase 3: append F3 + final OVERALL, append-only, refuses if already set ──
    public static int RunRecordF3(string verdictDoc, string score, TextWriter outw)
    {
        if (!File.Exists(verdictDoc)) { outw.WriteLine($"no such verdict doc: {verdictDoc}"); return 2; }
        string text = File.ReadAllText(verdictDoc);
        if (text.Contains("## F3 — human result", StringComparison.Ordinal))
        {
            outw.WriteLine("refusing: F3 is already recorded in this verdict doc (append-only).");
            return 4;
        }

        var parts = score.Split('/', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int n) || !int.TryParse(parts[1], out int d) || d <= 0)
        { outw.WriteLine($"bad --score '{score}', expected n/20."); return 2; }

        bool f1 = text.Contains("f1pass=true", StringComparison.Ordinal);
        bool f2 = text.Contains("f2pass=true", StringComparison.Ordinal);
        bool f3 = n >= 14 && d == 20;
        string overall = (f1 && f2 && f3) ? "PASS" : "FAIL";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## F3 — human result");
        sb.AppendLine();
        sb.AppendLine($"- **F3:** {n}/{d} — **{(f3 ? "PASS" : "FAIL")}** (threshold ≥14/20).");
        sb.AppendLine($"- **OVERALL (final):** {overall}  (f1pass={f1.ToString().ToLowerInvariant()}, f2pass={f2.ToString().ToLowerInvariant()}, f3pass={f3.ToString().ToLowerInvariant()}).");
        File.AppendAllText(verdictDoc, sb.ToString());
        outw.WriteLine($"recorded F3 {n}/{d} → OVERALL: {overall}");
        return overall == "PASS" ? 0 : 3;
    }

    private static string Mark(bool b) => b ? "PASS" : "FAIL";
    private static string Pct(double p) => p.ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
