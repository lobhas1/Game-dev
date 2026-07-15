using System.Text.Json.Nodes;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// The Anchor Round (spec docs/specs/2026-07-14-anchor-round.md): anchors gate-clean (A0),
// ancestry-eval express/A1/A2 on known fixtures, anchors isolated from seed globs, and card ↔
// frozen-signature-table consistency.
public class AnchorRoundTests
{
    public static IEnumerable<object[]> AnchorFiles()
    {
        foreach (var f in new[] { "phoenix-tear", "riptide", "wind-cutter", "bramble", "rune" })
            yield return new object[] { f };
    }

    // ── A0: every anchor passes the strict gate first-try as a tier-1 proposal ──
    [Theory]
    [MemberData(nameof(AnchorFiles))]
    public void A0_AnchorGatesClean_Tier1(string file)
    {
        var seed = Seed.Load(PromptTemplate.LocateRepoFile($"fixtures/anchors/{file}.seed.json"));
        var spell = SpellCompiler.Compile(SpellJson.Parse(seed.Spell.ToJsonString()));
        Assert.Equal(1, spell.Tier);
        Assert.NotEmpty(seed.Tags);
    }

    // ── anchor cards agree with the frozen signature table (element + signature tags) ──
    [Fact]
    public void AnchorCards_MatchFrozenSignatureTable()
    {
        var byName = new Dictionary<string, string>
        {
            ["Phoenix Tear"] = "phoenix-tear", ["Riptide"] = "riptide", ["Wind Cutter"] = "wind-cutter",
            ["Bramble"] = "bramble", ["Rune"] = "rune",
        };
        foreach (var sig in AncestryEval.Anchors)
        {
            var seed = Seed.Load(PromptTemplate.LocateRepoFile($"fixtures/anchors/{byName[sig.Name]}.seed.json"));
            Assert.Equal(sig.Element, seed.Element);
            foreach (var t in sig.Tags) Assert.Contains(t, seed.Tags); // the card claims its signature tags
        }
    }

    // ── anchors are NOT picked up by any seed glob (protect existing tests + showcase-batch) ──
    private static string RepoDir(string rel) =>
        Path.Combine(PromptTemplate.RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Anchors_NotInSeedGlob()
    {
        var seedFiles = Directory.GetFiles(RepoDir("fixtures/seeds"), "*.seed.json")
            .Select(Path.GetFileName).ToList();
        Assert.Equal(6, seedFiles.Count); // still exactly the original six
        Assert.DoesNotContain("phoenix-tear.seed.json", seedFiles);
        Assert.Equal(5, Directory.GetFiles(RepoDir("fixtures/anchors"), "*.seed.json").Length);
    }

    // ── express(): element-scoped AND tag-justified ──
    private static readonly AnchorSig Phoenix = AncestryEval.Anchors.Single(a => a.Name == "Phoenix Tear");
    private static readonly AnchorSig Riptide = AncestryEval.Anchors.Single(a => a.Name == "Riptide");

    [Fact]
    public void Express_FireHeal_TaggedAndDelivered_Expresses()
        => Assert.True(AncestryEval.Expresses(Phoenix, Rec("fire", new[] { "heal" }, Heal)));

    [Fact]
    public void Express_FireDamage_Stereotype_DoesNotExpress()
        => Assert.False(AncestryEval.Expresses(Phoenix, Rec("fire", new[] { "damage" }, Dmg("fire"))));

    [Fact]
    public void Express_WrongElement_DoesNotExpress()
        => Assert.False(AncestryEval.Expresses(Phoenix, Rec("water", new[] { "heal" }, Heal))); // water+heal is stereotype, not fire+heal

    [Fact]
    public void Express_HealsButUntagged_NotTagJustified_DoesNotExpress()
        => Assert.False(AncestryEval.Expresses(Phoenix, Rec("fire", new[] { "area" }, Heal))); // delivers heal, never claims it

    [Fact]
    public void Express_WaterDamage_Expresses()
        => Assert.True(AncestryEval.Expresses(Riptide, Rec("water", new[] { "damage" }, Dmg("water"))));

    // ── A1 / A2 over a known corpus ──
    [Fact]
    public void Evaluate_A1_PerAnchor_KnownRate()
    {
        var recs = new[]
        {
            Child("Phoenix Tear", "kid1", "fire", new[] { "heal" }, Heal),      // expresses
            Child("Phoenix Tear", "kid2", "fire", new[] { "damage" }, Dmg("fire")), // fire+damage stereotype → no
            Child("Riptide", "kid3", "water", new[] { "damage" }, Dmg("water")),   // expresses
        };
        var res = AncestryEval.Evaluate(recs);
        var ph = res.Single(r => r.Sig.Name == "Phoenix Tear");
        Assert.True(ph.HasChildren);
        Assert.Equal(2, ph.Children);
        Assert.Equal(1, ph.ChildExpressers);
        Assert.Equal(50.0, ph.A1Pct, 1);                 // 1/2 → passes (>=1/3 and >= 0+25pp)
        var rip = res.Single(r => r.Sig.Name == "Riptide");
        Assert.Equal(100.0, rip.A1Pct, 1);
    }

    [Fact]
    public void Evaluate_A2_WingCounts()
    {
        var (nat, arc) = AncestryEval.WingCounts(new[]
        {
            Rec("nature", new[] { "control" }, Ctrl), Rec("arcane", new[] { "ward" }, Shield), Rec("fire", new[] { "damage" }, Dmg("fire")),
        });
        Assert.Equal(1, nat);
        Assert.Equal(1, arc);
    }

    [Fact]
    public void Baseline_V4Corpus_AllZero_NonVacuous()
    {
        var res = AncestryEval.Evaluate(FusionEvaluator.LoadRecords(RepoDir("arena/fusions")));
        Assert.All(res, r => Assert.Equal(0.0, r.BaselinePct, 1)); // pinned: no signature already occurs
    }

    // ── helpers ──
    private const string Heal = "[{\"verb\":\"heal\",\"share\":1.0}]";
    private const string Ctrl = "[{\"verb\":\"applyStatus\",\"status\":\"root\",\"duration\":\"medium\",\"share\":1.0}]";
    private const string Shield = "[{\"verb\":\"shield\",\"share\":1.0,\"duration\":\"medium\"}]";
    private static string Dmg(string el) => $"[{{\"verb\":\"damage\",\"element\":\"{el}\",\"share\":1.0}}]";

    private static FusionRecord Rec(string element, string[] tags, string clausesJson) =>
        Child("PA", "r-" + element + string.Concat(tags), element, tags, clausesJson);

    private static FusionRecord Child(string parentA, string name, string element, string[] tags, string clausesJson)
    {
        var spell = JsonNode.Parse($"{{\"id\":\"{name}\",\"tier\":2,\"delivery\":{{\"type\":\"self\"}},\"clauses\":{clausesJson}}}");
        var concept = new Concept(name, "", element, tags.ToList(), "flavor", "why");
        return new FusionRecord(name, 2, parentA, "seed", "a.json", "b.json", concept, spell,
            true, false, false, null, "N", "M", "live", "test-model", "2026-01-01T00:00:00Z");
    }
}
