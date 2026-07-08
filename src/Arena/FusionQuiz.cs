using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arena;

public sealed record QuizTrial(
    int Index, string RealName, string DecoyName, int Tier,
    string ConceptName, string Element, IReadOnlyList<string> Tags, string Flavor,
    JsonNode ClausesA, JsonNode ClausesB, char RealSide);

/// <summary>quiz mode: from the fusion records, build a blind concept→clauses matching quiz (real vs
/// a same-tier decoy, sides randomized) and a SEPARATE answer key. Deterministic given --seed:
/// same records + seed → identical trials, order, and sides.</summary>
public static class FusionQuiz
{
    public const int MaxTrials = 20;

    // A tiny deterministic PRNG (xorshift64) — culture- and platform-independent, unlike a shuffle
    // that leans on Random's internal changes; the quiz must reproduce byte-for-byte from --seed.
    private sealed class Rng
    {
        private ulong _s;
        public Rng(long seed) => _s = ((ulong)seed ^ 0x9E3779B97F4A7C15UL) | 1UL;
        private ulong Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return _s; }
        public int Int(int n) => n <= 0 ? 0 : (int)(Next() % (ulong)n);
        public bool Bool() => (Next() & 1UL) == 0UL;
    }

    public static List<QuizTrial> BuildTrials(IReadOnlyList<FusionRecord> records, long seed)
    {
        var gated = records.Where(r => r.Gated && r.Spell is not null)
            .OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        var rng = new Rng(seed);
        var trials = new List<QuizTrial>();

        foreach (var real in gated)
        {
            if (trials.Count >= MaxTrials) break;
            var peers = gated.Where(o => o.Tier == real.Tier && !string.Equals(o.Name, real.Name, StringComparison.Ordinal)).ToList();
            if (peers.Count == 0) continue; // no same-tier decoy → skip (logged by caller)
            var decoy = peers[rng.Int(peers.Count)];

            var realClauses = (real.Spell!["clauses"] ?? new JsonArray()).DeepClone();
            var decoyClauses = (decoy.Spell!["clauses"] ?? new JsonArray()).DeepClone();
            char realSide = rng.Bool() ? 'A' : 'B';

            trials.Add(new QuizTrial(
                trials.Count + 1, real.Name, decoy.Name, real.Tier,
                real.Concept.Name, real.Concept.Element, real.Concept.Tags, real.Concept.Flavor,
                realSide == 'A' ? realClauses : decoyClauses,
                realSide == 'A' ? decoyClauses : realClauses,
                realSide));
        }
        return trials;
    }

    public static string RenderQuiz(IReadOnlyList<QuizTrial> trials, long seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Phase B — fusion quiz (seed {seed})");
        sb.AppendLine();
        sb.AppendLine("For each concept, pick the clause list — **A** or **B** — whose mechanics express it. One side is the real fusion; the other is a same-tier decoy. Answers are in a separate key file; do not look until done.");
        sb.AppendLine();
        foreach (var t in trials)
        {
            sb.AppendLine($"## Trial {t.Index}");
            sb.AppendLine();
            sb.AppendLine($"**Concept:** {t.ConceptName} — element: {t.Element}, tags: [{string.Join(", ", t.Tags)}]");
            sb.AppendLine($"**Flavor:** {t.Flavor}");
            sb.AppendLine();
            sb.AppendLine("A:");
            sb.AppendLine("```json");
            sb.AppendLine(t.ClausesA.ToJsonString(Indent));
            sb.AppendLine("```");
            sb.AppendLine("B:");
            sb.AppendLine("```json");
            sb.AppendLine(t.ClausesB.ToJsonString(Indent));
            sb.AppendLine("```");
            sb.AppendLine("**Your answer (A/B):** ____");
            sb.AppendLine();
        }
        sb.AppendLine($"— {trials.Count} trials. Score = correct / {trials.Count}. F3 threshold on a 20-trial run is ≥14/20.");
        return sb.ToString();
    }

    public static string RenderKey(IReadOnlyList<QuizTrial> trials, long seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Phase B — quiz ANSWER KEY (seed {seed})");
        sb.AppendLine();
        sb.AppendLine("Do not show this to the tester. One row per trial.");
        sb.AppendLine();
        sb.AppendLine("| trial | correct side | real fusion | same-tier decoy |");
        sb.AppendLine("|---:|:---:|---|---|");
        foreach (var t in trials)
            sb.AppendLine($"| {t.Index} | {t.RealSide} | {t.RealName} | {t.DecoyName} |");
        return sb.ToString();
    }

    public static int RunQuiz(string fusionsDir, long seed, TextWriter outw)
    {
        var records = FusionEvaluator.LoadRecords(fusionsDir);
        var trials = BuildTrials(records, seed);
        int gated = records.Count(r => r.Gated);
        if (trials.Count < gated)
            outw.WriteLine($"note: {gated - trials.Count} gated fusion(s) had no same-tier decoy and were skipped.");

        var now = DateTime.UtcNow;
        string stamp = now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        string docDir = Path.Combine(PromptTemplate.RepoRoot(), "docs", "experiments");
        Directory.CreateDirectory(docDir);
        string quizPath = Path.Combine(docDir, $"{stamp}-phase-b-quiz.md");
        string keyPath = Path.Combine(docDir, $"{stamp}-phase-b-answer-key.md");
        if (File.Exists(quizPath) || File.Exists(keyPath)) throw new IOException("refusing to overwrite an existing quiz/key file.");
        File.WriteAllText(quizPath, RenderQuiz(trials, seed));
        File.WriteAllText(keyPath, RenderKey(trials, seed));

        outw.WriteLine($"quiz:  {quizPath} ({trials.Count} trials)");
        outw.WriteLine($"key:   {keyPath}");
        return 0;
    }

    private static readonly JsonSerializerOptions Indent = new() { WriteIndented = true };
}
