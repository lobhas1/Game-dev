using System.Text.Json.Nodes;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

public class FusionTests
{
    // ── seeds gate-clean ──
    [Theory]
    [InlineData("ember")] [InlineData("droplet")] [InlineData("pebble")]
    [InlineData("gust")] [InlineData("glimmer")] [InlineData("umbra")]
    public void Seed_BodyGatesClean(string name)
    {
        var seed = Seed.Load(PromptTemplate.LocateRepoFile($"fixtures/seeds/{name}.seed.json"));
        var spell = SpellCompiler.Compile(SpellJson.Parse(seed.Spell.ToJsonString()));
        Assert.Equal(1, spell.Tier);
        Assert.NotEmpty(seed.Tags);
    }

    // ── tier law: equal ascends, unequal keeps higher, capped at 3 ──
    [Fact]
    public void TierLaw_Cases()
    {
        Assert.Equal(2, TierLaw.Of(1, 1));
        Assert.Equal(3, TierLaw.Of(2, 2));
        Assert.Equal(3, TierLaw.Of(3, 3)); // cap holds
        Assert.Equal(2, TierLaw.Of(1, 2));
        Assert.Equal(3, TierLaw.Of(1, 3));
    }

    // ── fuse pipeline via StubOracle ──
    [Fact]
    public async Task Fuse_FirstPass_Gates()
    {
        var (a, b) = Parents();
        var oracle = new StubOracle(NamingSteam, MechSteam);
        var rec = await Fuse(oracle, a, b);
        Assert.True(rec.Gated);
        Assert.True(rec.FirstPassOk);
        Assert.False(rec.Repaired);
        Assert.Equal("steam", rec.Name);
        Assert.Equal(2, rec.Tier);            // T1 + T1 → T2
        Assert.Equal(2, oracle.CallCount);    // naming + mechanics, no repair
        Assert.Equal("stub", rec.Source);     // origin recorded — not scorable as live
        Assert.False(rec.IsLive);
    }

    [Fact]
    public async Task Fuse_RepairPath_GatesOnSecondTry()
    {
        var (a, b) = Parents();
        var oracle = new StubOracle(NamingSteam, BadMech, GoodMech);
        var rec = await Fuse(oracle, a, b);
        Assert.True(rec.Gated);
        Assert.False(rec.FirstPassOk);
        Assert.True(rec.Repaired);
        Assert.Equal(3, oracle.CallCount);    // naming + mechanics + one repair
    }

    [Fact]
    public async Task Fuse_DiscardPath_WhenRepairAlsoFails()
    {
        var (a, b) = Parents();
        var oracle = new StubOracle(NamingSteam, BadMech, BadMech);
        var rec = await Fuse(oracle, a, b);
        Assert.False(rec.Gated);
        Assert.True(rec.Discarded);
        Assert.NotNull(rec.GateError);
    }

    [Fact]
    public async Task Fuse_NamingFailure_DiscardsBeforeMechanics()
    {
        var (a, b) = Parents();
        var oracle = new StubOracle("no json here at all");
        var rec = await Fuse(oracle, a, b);
        Assert.True(rec.Discarded);
        Assert.Equal(FusionEvaluator.NamingFailedSentinel, rec.Concept.Name);
        Assert.Equal(1, oracle.CallCount);    // mechanics never reached
    }

    // ── tier is enforced AT the gate: a wrong tier is a gate failure that flows into repair ──
    [Fact]
    public async Task Fuse_WrongTier_RepairsToLawTier()
    {
        var (a, b) = Parents();                                  // law tier = T1 + T1 → 2
        string wrongTier = MechSteam.Replace("\"tier\":2", "\"tier\":3");
        var oracle = new StubOracle(NamingSteam, wrongTier, MechSteam);
        var rec = await Fuse(oracle, a, b);
        Assert.True(rec.Gated);
        Assert.False(rec.FirstPassOk);      // T3 spell under a T2 law failed the gate
        Assert.True(rec.Repaired);
        Assert.Equal(2, rec.Tier);
        Assert.Equal(3, oracle.CallCount);  // naming + wrong-tier mechanics + repair
    }

    [Fact]
    public async Task Fuse_WrongTierTwice_Discards()
    {
        var (a, b) = Parents();
        string wrongTier = MechSteam.Replace("\"tier\":2", "\"tier\":3");
        var oracle = new StubOracle(NamingSteam, wrongTier, wrongTier);
        var rec = await Fuse(oracle, a, b);
        Assert.True(rec.Discarded);
        Assert.Contains("tier mismatch", rec.GateError);
    }

    // ── a fusion record loaded as a parent takes its concept name, not the kebab file-id ──
    [Fact]
    public void Seed_LoadsFusionRecordAsParent_FromConcept()
    {
        string recJson = @"{""name"":""steam"",""tier"":2,""origin"":{""source"":""live"",""model"":""m""},""concept"":{""name"":""Steam"",""element"":""water"",""tags"":[""area"",""control""],""flavor"":""vapor""},""spell"":{""id"":""steam"",""tier"":2,""delivery"":{""type"":""self""},""clauses"":[{""verb"":""heal"",""share"":1.0}]}}";
        string path = Path.Combine(Path.GetTempPath(), "steam-" + Guid.NewGuid().ToString("N") + ".record.json");
        File.WriteAllText(path, recJson);
        var seed = Seed.Load(path);
        Assert.Equal("Steam", seed.Name);   // concept.name, not the kebab "steam"
        Assert.Equal("water", seed.Element);
        Assert.Contains("area", seed.Tags);
        Assert.Contains("control", seed.Tags);
    }

    // ── F2 scorer: known coverage and decoy baseline ──
    [Fact]
    public void F2_RealBeatsDecoy_OnCleanCorpus()
    {
        var recs = new[]
        {
            Rec("aaa", new[] { "damage" }, Dmg),
            Rec("bbb", new[] { "heal" }, Heal),
        };
        var r = FusionEvaluator.Evaluate(recs, "N", "M");
        Assert.Equal(2, r.ScoredCount);
        Assert.Equal(100.0, r.MeanRealPct, 1);
        Assert.Equal(0.0, r.MeanDecoyPct, 1);   // damage-concept vs heal-clauses and vice-versa
        Assert.True(r.F1Pass);
        Assert.True(r.F2Pass);
    }

    [Fact]
    public void F2_PartialCoverage_IsFractional()
    {
        // concept wants damage AND ward; clauses only deal damage → 1/2 = 50%.
        var r = FusionEvaluator.Evaluate(new[] { Rec("x", new[] { "damage", "ward" }, Dmg) }, "N", "M");
        var cov = Assert.Single(r.PerFusion);
        Assert.Equal(1, cov.Satisfied);
        Assert.Equal(2, cov.Mappable);
        Assert.Contains("ward", cov.Unsatisfied);
    }

    // ── unmappable tags: excluded from the denominator, counted as quantization pressure ──
    [Fact]
    public void Quantization_CountsUnmappable_AndExcludesFromDenominator()
    {
        var r = FusionEvaluator.Evaluate(new[] { Rec("ccc", new[] { "damage", "summon" }, Dmg) }, "N", "M");
        var cov = Assert.Single(r.PerFusion);
        Assert.Equal(1, cov.Mappable);          // summon not counted
        Assert.Equal(1, cov.Satisfied);
        Assert.Contains(r.Quantization, kv => kv.Key == "summon" && kv.Value == 1);
        Assert.Equal(1, r.FusionsWithUnmappable);
    }

    // ── origin guard: stub records (same shas as live) are excluded from scoring, reported ──
    [Fact]
    public void StubRecords_ExcludedFromScoring_AndReported()
    {
        var recs = new[]
        {
            Rec("live1", new[] { "damage" }, Dmg, source: "live"),
            Rec("stub1", new[] { "damage" }, Dmg, source: "stub"),
        };
        var r = FusionEvaluator.Evaluate(recs, "N", "M");
        Assert.Equal(2, r.MatchingRecords);   // both carry matching shas
        Assert.Equal(1, r.StubExcluded);      // the stub record is excluded from scoring
        Assert.Equal(1, r.MechProposals);     // only the live record is a mechanics proposal
        Assert.Single(r.PerFusion);           // only the live record is scored
    }

    [Fact]
    public void Record_WithoutOrigin_ReadsAsStub_FailSafe()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            @"{""name"":""x"",""tier"":2,""concept"":{""name"":""X"",""tags"":[""damage""]},""gate"":{""firstPassOk"":true},""shas"":{""naming"":""N"",""mechanics"":""M""},""spell"":{""id"":""x"",""tier"":2,""delivery"":{""type"":""self""},""clauses"":[]}}");
        var rec = FusionRecord.FromJson(json!);
        Assert.False(rec.IsLive);             // no origin block → treated as stub → never scored
        Assert.Equal("stub", rec.Source);
    }

    // ── two-sha integrity: stale naming OR mechanics sha excludes a record ──
    [Fact]
    public void TwoShaFilter_ExcludesStaleRecords()
    {
        var recs = new[]
        {
            Rec("live", new[] { "damage" }, Dmg, nSha: "N", mSha: "M"),
            Rec("staleMech", new[] { "damage" }, Dmg, nSha: "N", mSha: "STALE"),
            Rec("staleName", new[] { "damage" }, Dmg, nSha: "STALE", mSha: "M"),
        };
        var r = FusionEvaluator.Evaluate(recs, "N", "M");
        Assert.Equal(1, r.MatchingRecords);
        Assert.Equal(2, r.ExcludedStale);
    }

    [Fact]
    public void NamingFailure_ExcludedFromF1Denominator()
    {
        var good = Rec("aaa", new[] { "damage" }, Dmg);
        var nf = new FusionRecord("nf", 2, "A", "B", "a", "b",
            new Concept(FusionEvaluator.NamingFailedSentinel, "", "", new List<string>(), "", ""),
            null, false, false, true, "naming reply unparseable", "N", "M", "live", "test-model", "ts");
        var r = FusionEvaluator.Evaluate(new[] { good, nf }, "N", "M");
        Assert.Equal(2, r.MatchingRecords);
        Assert.Equal(1, r.MechProposals);       // naming-failure is not a mechanics proposal
    }

    // ── quiz: deterministic given seed, and the key is separate from the quiz ──
    [Fact]
    public void Quiz_DeterministicBySeed_AndKeySeparate()
    {
        var recs = new[]
        {
            Rec("steam", new[] { "area", "control" }, Steam),
            Rec("cinder", new[] { "damage", "duration" }, Cinder),
        };
        var t1 = FusionQuiz.BuildTrials(recs, 7);
        var t2 = FusionQuiz.BuildTrials(recs, 7);
        Assert.Equal(2, t1.Count);
        Assert.Equal(t1.Count, t2.Count);
        for (int i = 0; i < t1.Count; i++)
        {
            Assert.Equal(t1[i].RealName, t2[i].RealName);
            Assert.Equal(t1[i].DecoyName, t2[i].DecoyName);
            Assert.Equal(t1[i].RealSide, t2[i].RealSide);   // sides reproduce from the seed
        }

        string quiz = FusionQuiz.RenderQuiz(t1, 7);
        string key = FusionQuiz.RenderKey(t1, 7);
        Assert.Contains("Your answer (A/B):", quiz);
        Assert.DoesNotContain("correct side", quiz);        // no answers leak into the quiz
        Assert.Contains("correct side", key);               // the key holds them
    }

    // ── collision-proof filenames: convergence must not destroy the archive ──
    [Fact]
    public void SameName_DifferentParents_WritesTwoFiles_AndLogsRediscovery()
    {
        string arena = NewTempArena();
        FusionPipeline.WriteRecord(RecNamed("murk", "Murk", "Droplet", "Umbra", "fixtures/seeds/droplet.seed.json", "fixtures/seeds/umbra.seed.json"), arena, TextWriter.Null);
        FusionPipeline.WriteRecord(RecNamed("murk", "Murk", "Gust", "Umbra", "fixtures/seeds/gust.seed.json", "fixtures/seeds/umbra.seed.json"), arena, TextWriter.Null);

        Assert.Equal(2, Directory.GetFiles(Path.Combine(arena, "fusions"), "murk-*.record.json").Length); // distinct parents-hash → no overwrite
        Assert.Contains("rediscovered: Murk via Gust+Umbra (first seen: Droplet+Umbra)",
            File.ReadAllText(Path.Combine(arena, "fusions.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void SamePair_FusedAgain_SelfOverwrites_OneFile()
    {
        string arena = NewTempArena();
        var pair = RecNamed("murk", "Murk", "Droplet", "Umbra", "fixtures/seeds/droplet.seed.json", "fixtures/seeds/umbra.seed.json");
        FusionPipeline.WriteRecord(pair, arena, TextWriter.Null);
        FusionPipeline.WriteRecord(pair, arena, TextWriter.Null);

        Assert.Single(Directory.GetFiles(Path.Combine(arena, "fusions"), "murk-*.record.json")); // same hash → same file, self-overwrite is fine
        Assert.DoesNotContain("rediscovered", File.ReadAllText(Path.Combine(arena, "fusions.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void Readers_CountBothFilenamePatterns()
    {
        string arena = NewTempArena();
        string dir = Path.Combine(arena, "fusions");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "steam.record.json"),               // legacy name-only
            RecNamed("steam", "Steam", "Ember", "Droplet", "fixtures/seeds/ember.seed.json", "fixtures/seeds/droplet.seed.json").ToJson().ToJsonString());
        File.WriteAllText(Path.Combine(dir, "murk-a1b2c3d4.record.json"),        // new name-hash
            RecNamed("murk", "Murk", "Droplet", "Umbra", "fixtures/seeds/droplet.seed.json", "fixtures/seeds/umbra.seed.json").ToJson().ToJsonString());

        var recs = FusionEvaluator.LoadRecords(dir);
        Assert.Equal(2, recs.Count);                                            // evaluator counts both
        Assert.Equal(2, FusionEvaluator.Evaluate(recs, "N", "M").MechProposals);
        Assert.Equal(2, FusionQuiz.BuildTrials(recs, 1).Count);                 // quiz counts both (T2 each → same-tier decoys)
    }

    // ── helpers ──
    private const string NamingSteam = @"{""name"":""Steam"",""emoji"":""x"",""element"":""water"",""tags"":[""area"",""control""],""flavor"":""f"",""why"":""w""}";
    private const string MechSteam = @"{""id"":""steam"",""tier"":2,""cast"":{""mode"":""instant"",""cooldown"":""medium"",""cost"":{""resource"":""mana"",""amount"":""moderate""}},""delivery"":{""type"":""groundAoE"",""shape"":""circle"",""size"":""medium""},""clauses"":[{""verb"":""applyStatus"",""status"":""slow"",""duration"":""medium"",""share"":0.5},{""verb"":""damage"",""element"":""water"",""share"":0.5}]}";
    private const string BadMech = @"{""id"":""x"",""tier"":2,""cast"":{""mode"":""instant"",""cooldown"":""short"",""cost"":{""resource"":""mana"",""amount"":""low""}},""delivery"":{""type"":""self""},""clauses"":[{""verb"":""bogus"",""share"":1.0}]}";
    private const string GoodMech = @"{""id"":""steam"",""tier"":2,""cast"":{""mode"":""instant"",""cooldown"":""short"",""cost"":{""resource"":""mana"",""amount"":""low""}},""delivery"":{""type"":""self""},""clauses"":[{""verb"":""heal"",""share"":1.0}]}";

    private const string Dmg = @"[{""verb"":""damage"",""element"":""fire"",""share"":1.0}]";
    private const string Heal = @"[{""verb"":""heal"",""share"":1.0}]";
    private const string Steam = @"[{""verb"":""applyStatus"",""status"":""slow"",""duration"":""medium"",""share"":0.5},{""verb"":""damage"",""element"":""water"",""share"":0.5}]";
    private const string Cinder = @"[{""verb"":""damage"",""element"":""fire"",""share"":0.6},{""verb"":""applyStatus"",""status"":""burn"",""duration"":""long"",""share"":0.4}]";

    private static (Seed, Seed) Parents() =>
        (Seed.Load(PromptTemplate.LocateRepoFile("fixtures/seeds/ember.seed.json")),
         Seed.Load(PromptTemplate.LocateRepoFile("fixtures/seeds/droplet.seed.json")));

    private static Task<FusionRecord> Fuse(IOracle oracle, Seed a, Seed b) =>
        FusionPipeline.FuseAsync(oracle, a, b, null, "naming {{TIER}}", "mechanics {{TIER}}",
            "N", "M", "stub", "stub:test", "2026-01-01T00:00:00Z", "fixtures/seeds/ember.seed.json", "fixtures/seeds/droplet.seed.json");

    private static FusionRecord Rec(string name, string[] tags, string clausesJson,
        string nSha = "N", string mSha = "M", int tier = 2, bool firstPass = true, string source = "live")
    {
        var spell = JsonNode.Parse($"{{\"id\":\"{name}\",\"tier\":{tier},\"delivery\":{{\"type\":\"self\"}},\"clauses\":{clausesJson}}}");
        var concept = new Concept(name, "", "fire", tags.ToList(), "flavor " + name, "why");
        return new FusionRecord(name, tier, "A", "B", "a.json", "b.json", concept, spell,
            firstPass, false, false, null, nSha, mSha, source, "test-model", "2026-01-01T00:00:00Z");
    }

    // A gated live record with an explicit kebab name, concept name, parent display names, and parent
    // paths — the paths drive the filename hash, the display names drive the rediscovery message.
    private static FusionRecord RecNamed(string kebab, string conceptName, string aName, string bName, string aPath, string bPath)
    {
        var spell = JsonNode.Parse($"{{\"id\":\"{kebab}\",\"tier\":2,\"delivery\":{{\"type\":\"self\"}},\"clauses\":[{{\"verb\":\"heal\",\"share\":1.0}}]}}");
        var concept = new Concept(conceptName, "", "shadow", new List<string> { "conceal" }, "flavor", "why");
        return new FusionRecord(kebab, 2, aName, bName, aPath, bPath, concept, spell,
            true, false, false, null, "N", "M", "live", "test-model", "2026-01-01T00:00:00Z");
    }

    private static string NewTempArena()
    {
        string dir = Path.Combine(Path.GetTempPath(), "arena-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
