using System.Globalization;

namespace Arena;

/// <summary>An anchor's injected signature: an element paired with off-stereotype meaning-tags.
/// The `express` rule and the stereotype table are FROZEN by the anchor-round spec
/// (docs/specs/2026-07-14-anchor-round.md); ancestry-eval is the single tool computing A1/A2.</summary>
public sealed record AnchorSig(string Name, string Element, IReadOnlyList<string> Tags);

public sealed record SignatureResult(AnchorSig Sig, int Corpus, int Expressers, double BaselinePct,
    int Children, int ChildExpressers, double A1Pct, bool HasChildren);

public static class AncestryEval
{
    // Seed archetype tag per element — the "stereotype". nature/arcane have no seed, so every tag on
    // them is off-stereotype (the two wings this round opens).
    private static readonly Dictionary<string, string> StereotypeTag = new(StringComparer.Ordinal)
    {
        ["fire"] = "damage", ["water"] = "heal", ["earth"] = "ward",
        ["air"] = "movement", ["light"] = "perceive", ["shadow"] = "conceal",
    };

    // The five anchor signatures (frozen). perceive is unmappable — Rune's mechanized half is ward.
    public static readonly IReadOnlyList<AnchorSig> Anchors = new[]
    {
        new AnchorSig("Phoenix Tear", "fire", new[] { "heal" }),
        new AnchorSig("Riptide", "water", new[] { "damage" }),
        new AnchorSig("Wind Cutter", "air", new[] { "damage" }),
        new AnchorSig("Bramble", "nature", new[] { "control", "damage" }),
        new AnchorSig("Rune", "arcane", new[] { "ward", "perceive" }),
    };

    public const double A1MinFraction = 1.0 / 3.0;         // >= 1/3 of an anchor's children
    public const double A1MinOverBaseline = 0.25;          // AND >= baseline + 25 percentage points

    public static bool OffStereotype(string element, string tag) =>
        !StereotypeTag.TryGetValue(element, out var stereo) || stereo != tag;

    /// <summary>FROZEN express(): a fusion expresses signature (E_a, S) iff the concept wears element
    /// E_a AND, for some off-stereotype signature tag T in S, the concept CLAIMS T (tag-justified) and
    /// its mechanics DELIVER T (the F2 mapper). Element-scoped because the signature is element+verb
    /// ("fire+heal" = a fire concept that heals); the element-agnostic reading is vacuous (spec).</summary>
    public static bool Expresses(AnchorSig sig, FusionRecord r)
    {
        if (r.Spell is null || !string.Equals(r.Concept.Element, sig.Element, StringComparison.Ordinal)) return false;
        var facts = TagCoverage.Facts(r.Spell);
        var tags = r.Concept.Tags;
        return sig.Tags.Any(t => OffStereotype(sig.Element, t) && tags.Contains(t) && TagCoverage.Covers(t, facts));
    }

    private static bool IsChildOf(AnchorSig sig, FusionRecord r) =>
        string.Equals(r.ParentA, sig.Name, StringComparison.Ordinal) ||
        string.Equals(r.ParentB, sig.Name, StringComparison.Ordinal);

    // Pure — tests drive it with hand-built records.
    public static List<SignatureResult> Evaluate(IReadOnlyList<FusionRecord> records)
    {
        var gated = records.Where(r => r.Gated && r.Spell is not null).ToList();
        var results = new List<SignatureResult>();
        foreach (var sig in Anchors)
        {
            int expr = gated.Count(r => Expresses(sig, r));
            var children = gated.Where(r => IsChildOf(sig, r)).ToList();
            int childExpr = children.Count(r => Expresses(sig, r));
            results.Add(new SignatureResult(sig, gated.Count, expr,
                gated.Count == 0 ? 0 : 100.0 * expr / gated.Count,
                children.Count, childExpr,
                children.Count == 0 ? 0 : 100.0 * childExpr / children.Count,
                children.Count > 0));
        }
        return results;
    }

    public static (int Nature, int Arcane) WingCounts(IReadOnlyList<FusionRecord> records)
    {
        var gated = records.Where(r => r.Gated && r.Spell is not null).ToList();
        return (gated.Count(r => r.Concept.Element == "nature"), gated.Count(r => r.Concept.Element == "arcane"));
    }

    // ── mode wrapper: ancestry-eval <fusionsDir> ──
    public static int RunEvaluate(string fusionsDir, TextWriter outw)
    {
        var records = FusionEvaluator.LoadRecords(fusionsDir);
        var results = Evaluate(records);
        var (nature, arcane) = WingCounts(records);
        bool anyChildren = results.Any(r => r.HasChildren);

        outw.WriteLine("=== ANCESTRY EVAL — anchor signatures (spec docs/specs/2026-07-14-anchor-round.md) ===");
        outw.WriteLine($"corpus: {fusionsDir}  gated fusions: {results.FirstOrDefault()?.Corpus ?? 0}");
        outw.WriteLine(anyChildren
            ? "  A1 per anchor (children = fusions with the anchor as a parent):"
            : "  BASELINE (no anchor children in corpus) — corpus-wide signature-expression rate:");
        foreach (var r in results)
        {
            string sig = $"{r.Sig.Name} [{r.Sig.Element}+{string.Join("/", r.Sig.Tags)}]";
            if (r.HasChildren)
            {
                bool pass = r.A1Pct / 100.0 >= A1MinFraction && r.A1Pct / 100.0 >= r.BaselinePct / 100.0 + A1MinOverBaseline;
                outw.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {sig,-34} A1 {F(r.A1Pct)}% ({r.ChildExpressers}/{r.Children})  baseline {F(r.BaselinePct)}%");
            }
            else
            {
                outw.WriteLine($"  {sig,-41} baseline {F(r.BaselinePct)}% ({r.Expressers}/{r.Corpus})");
            }
        }
        outw.WriteLine($"  A2 wings — element nature: {nature} concept(s)   element arcane: {arcane} concept(s)");
        return 0;
    }

    private static string F(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);
}
