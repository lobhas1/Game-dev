using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using Arena;

namespace Arena.Tests;

// Prompt v4: register metrics, same-element decoys, mixed-sha refusal, and the banned-list
// single-source sync (C# FlavorEval is the source; spec, naming prompt, and the Python baseline
// script must all carry the same tokens).
public class PromptV4Tests
{
    // ── banned-list sync: single source, four embeddings ──

    public static IEnumerable<object[]> BannedEmbeddings()
    {
        yield return new object[] { "docs/specs/2026-07-11-prompt-v4.md" };
        yield return new object[] { "prompts/naming-oracle.md" };
        yield return new object[] { "scripts/flavor-stats.py" };
    }

    [Theory]
    [MemberData(nameof(BannedEmbeddings))]
    public void BannedList_EveryToken_InEmbedding(string relativePath)
    {
        string content = File.ReadAllText(PromptTemplate.LocateRepoFile(relativePath));
        Assert.NotEmpty(FlavorEval.BannedTokens);
        foreach (var token in FlavorEval.BannedTokens)
            Assert.Contains(token, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BannedList_PythonScript_IsSetEqual()
    {
        string py = File.ReadAllText(PromptTemplate.LocateRepoFile("scripts/flavor-stats.py"));
        var block = Regex.Match(py, @"BANNED\s*=\s*\[(.*?)\]", RegexOptions.Singleline).Groups[1].Value;
        var pyTokens = Regex.Matches(block, "\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(FlavorEval.BannedTokens.ToHashSet(StringComparer.Ordinal), pyTokens);
    }

    [Fact]
    public void Stopwords_PythonScript_IsSetEqual()
    {
        string py = File.ReadAllText(PromptTemplate.LocateRepoFile("scripts/flavor-stats.py"));
        var block = Regex.Match(py, @"STOPWORDS\s*=\s*\{(.*?)\}", RegexOptions.Singleline).Groups[1].Value;
        var pyWords = Regex.Matches(block, "\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(FlavorEval.Stopwords.ToHashSet(StringComparer.Ordinal), pyWords);
    }

    // ── flavor-eval on hand-built fixtures with known rates ──

    [Fact]
    public void FlavorEval_KnownRates()
    {
        var lines = new (string, string)[]
        {
            ("a", "A stone that argues with rivers."),          // clean
            ("b", "Ancient veils shroud the eternal realm."),   // banned (several)
            ("c", "A stone that remembers every kick."),        // repeats 'stone'
            ("d", "Rain that files itself into drawers."),      // clean
        };
        var s = FlavorEval.Compute(lines);
        Assert.Equal(4, s.Lines);
        Assert.Equal(1, s.BannedLines);
        Assert.Equal(25.0, s.V2aPct, 1);           // 1/4 > 10% → FAIL
        Assert.False(s.V2aPass);
        Assert.Equal("stone", s.TopWord);
        Assert.Equal(50.0, s.V2bPct, 1);            // 'stone' in 2/4 > 15% → FAIL
        Assert.False(s.V2bPass);
    }

    [Fact]
    public void FlavorEval_CleanCorpus_Passes()
    {
        var s = FlavorEval.Compute(new (string, string)[]
        {
            ("a", "A coal that wants a bigger job."),
            ("b", "Rain with an appointment to keep."),
            ("c", "Gravel that votes as one."),
            ("d", "Light bent into a fishing hook."),
            ("e", "Smoke practicing its handwriting."),
            ("f", "Frost that signs every window."),
            ("g", "Mud with strong opinions on shoes."),
            ("h", "Wind carrying somebody's ladder away."),
            ("i", "Sparks learning to march in step."),
            ("j", "Dew weighing down a spider's morning."),
        });
        Assert.True(s.V2aPass);
        Assert.True(s.V2bPass);
    }

    [Fact]
    public void BannedMatching_CatchesPlurals_NotSubstrings()
    {
        Assert.Contains("veil", FlavorEval.BannedHits("Seven veils over the door."));       // plural
        Assert.Contains("whisper", FlavorEval.BannedHits("The walls trade whispers."));     // whisper(s)
        Assert.Empty(FlavorEval.BannedHits("An unveiling of the new statue."));             // substring is NOT a hit
        Assert.Empty(FlavorEval.BannedHits("A bordered garden path."));                     // 'bordered' ≠ border(s)
    }

    // ── same-element decoys, with logged same-tier fallback ──

    [Fact]
    public void SameElementDecoys_PreferSameElement()
    {
        var recs = new[]
        {
            Rec("a-fire-1", "fire"), Rec("a-fire-2", "fire"), Rec("b-water", "water"),
        };
        var trials = FusionQuiz.BuildTrials(recs, 42, sameElementDecoys: true, notes: new List<string>());
        var fireTrial = trials.Single(t => t.RealName == "a-fire-1");
        Assert.Equal("a-fire-2", fireTrial.DecoyName); // the water peer is never picked for a fire real
    }

    [Fact]
    public void SameElementDecoys_FallBackToSameTier_WithNote()
    {
        var recs = new[] { Rec("a-fire", "fire"), Rec("b-water", "water") }; // no same-element peer anywhere
        var notes = new List<string>();
        var trials = FusionQuiz.BuildTrials(recs, 42, sameElementDecoys: true, notes: notes);
        Assert.Equal(2, trials.Count);                 // both trials still built (same-tier fallback)
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.Contains("same-tier fallback", n, StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultQuiz_Unchanged_NoElementFilter()
    {
        var recs = new[] { Rec("a-fire", "fire"), Rec("b-water", "water") };
        var trials = FusionQuiz.BuildTrials(recs, 42); // no flag: cross-element same-tier decoys are fine
        Assert.Equal(2, trials.Count);
    }

    // ── evaluate-fusions refuses mixed-sha corpora ──

    [Fact]
    public void MixedShaCorpus_Detected_OnlyWhenBothPresent()
    {
        var current = Rec("cur", "fire");
        var stale = Rec("old", "fire") with { NamingSha = "STALE" };

        Assert.True(FusionEvaluator.MixedShaCorpus(FusionEvaluator.Evaluate(new[] { current, stale }, "N", "M")));
        Assert.False(FusionEvaluator.MixedShaCorpus(FusionEvaluator.Evaluate(new[] { current }, "N", "M")));
        Assert.False(FusionEvaluator.MixedShaCorpus(FusionEvaluator.Evaluate(new[] { stale }, "N", "M"))); // all-stale ≠ mixed
    }

    // ── helper: a gated live T2 record with a chosen concept element ──

    private static FusionRecord Rec(string name, string element)
    {
        var spell = JsonNode.Parse($"{{\"id\":\"{name}\",\"tier\":2,\"delivery\":{{\"type\":\"self\"}},\"clauses\":[{{\"verb\":\"heal\",\"share\":1.0}}]}}");
        var concept = new Concept(name, "", element, new List<string> { "heal" }, "flavor " + name, "why");
        return new FusionRecord(name, 2, "A", "B", "a.json", "b.json", concept, spell,
            true, false, false, null, "N", "M", "live", "test-model", "2026-01-01T00:00:00Z");
    }
}
