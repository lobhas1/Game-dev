using System.Globalization;
using System.Text.RegularExpressions;

namespace Arena;

public sealed record FlavorStats(
    int Lines, int BannedLines, double V2aPct,
    string TopWord, int TopCount, double V2bPct,
    IReadOnlyList<(string Name, string Flavor, IReadOnlyList<string> Hits)> BannedDetail,
    IReadOnlyList<KeyValuePair<string, int>> TopWords)
{
    public bool V2aPass => V2aPct <= FlavorEval.V2aMaxPct;
    public bool V2bPass => V2bPct <= FlavorEval.V2bMaxPct;
}

/// <summary>Prompt-v4 register metrics (spec docs/specs/2026-07-11-prompt-v4.md): V2a banned-lexicon
/// rate and V2b content-word repetition over a fusion corpus's flavor lines. The banned and stopword
/// lists here are the SINGLE SOURCE — the spec, the naming prompt's never-use line, and
/// scripts/flavor-stats.py mirror them, pinned by the banned-list sync test.</summary>
public static class FlavorEval
{
    public const double V2aMaxPct = 10.0;
    public const double V2bMaxPct = 15.0;

    /// <summary>FROZEN banned lexicon. Matching: case-insensitive whole word, optional plural 's'.</summary>
    public static readonly string[] BannedTokens =
    {
        "threshold", "border", "veil", "whisper", "essence", "ancient", "forgotten",
        "eternal", "realm", "shroud", "twilight", "betwixt", "unseen", "beyond",
    };

    /// <summary>FROZEN stopword list: a content word is an alphabetic token, length ≥3, not in here.
    /// Mirrored by scripts/flavor-stats.py (sync-tested).</summary>
    public static readonly string[] Stopwords =
    {
        "the", "and", "that", "this", "these", "those", "its", "was", "were", "are",
        "been", "being", "has", "have", "had", "does", "did", "will", "would", "can",
        "could", "may", "might", "shall", "should", "must", "not", "nor", "than", "then",
        "when", "where", "while", "who", "whom", "whose", "which", "what", "how", "why",
        "for", "with", "into", "over", "under", "out", "off", "onto", "from", "upon",
        "about", "above", "below", "between", "through", "until", "again", "once", "here",
        "there", "all", "any", "both", "each", "few", "more", "most", "other", "some",
        "such", "only", "own", "same", "too", "very", "just", "still", "yet", "ever",
        "never", "also", "but", "his", "her", "their", "your", "our", "one", "two",
    };

    private static readonly HashSet<string> StopSet = new(Stopwords, StringComparer.Ordinal);

    public static IReadOnlyList<string> BannedHits(string flavor)
    {
        string low = flavor.ToLowerInvariant();
        return BannedTokens.Where(t => Regex.IsMatch(low, $@"\b{t}s?\b")).ToList();
    }

    public static IEnumerable<string> ContentWords(string flavor) =>
        Regex.Matches(flavor.ToLowerInvariant(), "[a-z]+")
            .Select(m => m.Value)
            .Where(w => w.Length >= 3 && !StopSet.Contains(w))
            .Distinct(StringComparer.Ordinal);

    // Pure — tests drive it with hand-built (name, flavor) pairs.
    public static FlavorStats Compute(IReadOnlyList<(string Name, string Flavor)> lines)
    {
        var banned = lines
            .Select(l => (l.Name, l.Flavor, Hits: BannedHits(l.Flavor)))
            .Where(l => l.Hits.Count > 0)
            .Select(l => (l.Name, l.Flavor, (IReadOnlyList<string>)l.Hits))
            .ToList();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, flavor) in lines)
            foreach (var w in ContentWords(flavor))
                counts[w] = counts.GetValueOrDefault(w) + 1;
        var top = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var (topWord, topCount) = top.Count == 0 ? ("", 0) : (top[0].Key, top[0].Value);

        int n = lines.Count;
        return new FlavorStats(
            n, banned.Count, n == 0 ? 0 : 100.0 * banned.Count / n,
            topWord, topCount, n == 0 ? 0 : 100.0 * topCount / n,
            banned, top.Take(8).ToList());
    }

    // ── mode wrapper: flavor-eval <fusionsDir> ──
    public static int RunEvaluate(string fusionsDir, TextWriter outw)
    {
        var lines = FusionEvaluator.LoadRecords(fusionsDir)
            .Where(r => !string.IsNullOrWhiteSpace(r.Concept.Flavor))
            .Select(r => (r.Name, r.Concept.Flavor))
            .ToList();
        if (lines.Count == 0) { outw.WriteLine($"no flavor lines found in {fusionsDir}."); return 2; }

        var s = Compute(lines);
        outw.WriteLine($"=== FLAVOR EVAL — prompt v4 register metrics (spec 2026-07-11-prompt-v4.md) ===");
        outw.WriteLine($"corpus: {fusionsDir}  flavor lines: {s.Lines}");
        outw.WriteLine($"  [{Mark(s.V2aPass)}] V2a banned-lexicon rate <= {F(V2aMaxPct)}%   {F(s.V2aPct)}% ({s.BannedLines}/{s.Lines})");
        foreach (var (name, flavor, hits) in s.BannedDetail)
            outw.WriteLine($"      {name}: [{string.Join(", ", hits)}] \"{flavor}\"");
        outw.WriteLine($"  [{Mark(s.V2bPass)}] V2b max word repetition <= {F(V2bMaxPct)}%    {F(s.V2bPct)}% ('{s.TopWord}' in {s.TopCount}/{s.Lines})");
        outw.WriteLine($"      top content words: {string.Join(", ", s.TopWords.Select(kv => $"{kv.Key}={kv.Value}"))}");
        return s.V2aPass && s.V2bPass ? 0 : 3;
    }

    private static string Mark(bool b) => b ? "PASS" : "FAIL";
    private static string F(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);
}
