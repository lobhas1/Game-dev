using System.Globalization;

namespace Arena;

/// <summary>One `arena/generation.log` line: the auditable record of a single `generate` run,
/// keyed by the prompt hash it was generated under. No key material is ever stored.</summary>
public sealed record GenerationLogEntry(
    string Timestamp,
    string PromptSha,
    string Model,
    string Brief,
    int Tier,
    int KitSize,
    int Generated,
    int FirstPass,
    int Accepted,
    IReadOnlyList<string> Kits,
    IReadOnlyList<KeyValuePair<string, int>> Marginals);

public static class GenerationLog
{
    private const string Sep = " | ";

    public static string Format(GenerationLogEntry e) => string.Join(Sep,
        e.Timestamp,
        "promptSha=" + e.PromptSha,
        "model=" + e.Model,
        "brief=\"" + Sanitize(e.Brief) + "\"",
        "tier=" + I(e.Tier),
        "kitSize=" + I(e.KitSize),
        "generated=" + I(e.Generated),
        "firstPass=" + I(e.FirstPass),
        "accepted=" + I(e.Accepted),
        "kits=" + string.Join(",", e.Kits),
        "marginals=" + string.Join(",", e.Marginals.Select(kv => $"{kv.Key}={I(kv.Value)}")));

    public static GenerationLogEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Split(Sep);
        if (parts.Length < 11) return null;

        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            if (eq >= 0) f[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
        }

        return new GenerationLogEntry(
            parts[0].Trim(),
            f.GetValueOrDefault("promptSha", ""),
            f.GetValueOrDefault("model", ""),
            f.GetValueOrDefault("brief", "").Trim('"'),
            Int(f, "tier"), Int(f, "kitSize"), Int(f, "generated"), Int(f, "firstPass"), Int(f, "accepted"),
            SplitList(f.GetValueOrDefault("kits", "")),
            ParseMarginals(f.GetValueOrDefault("marginals", "")));
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static int Int(Dictionary<string, string> f, string k) =>
        int.TryParse(f.GetValueOrDefault(k, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static List<string> SplitList(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<KeyValuePair<string, int>> ParseMarginals(string s)
    {
        var list = new List<KeyValuePair<string, int>>();
        foreach (var tok in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = tok.IndexOf('=');
            if (eq >= 0 && int.TryParse(tok.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
                list.Add(new KeyValuePair<string, int>(tok.Substring(0, eq), c));
        }
        return list;
    }

    // The record's field separators must never appear inside the brief.
    private static string Sanitize(string s) =>
        s.Replace("|", "/").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
}
