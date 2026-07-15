using System.Text.Json.Nodes;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// M2 phase 1: showcase replays. One spell, controlled scene, normal schema-1 replay.
public class ShowcaseTests
{
    private const string BareSpell =
        "{\"id\":\"bare-bolt\",\"tier\":1," +
        "\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
        "\"delivery\":{\"type\":\"projectile\",\"speed\":\"fast\"}," +
        "\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0}]}";

    private const string LongStatusSpell =
        "{\"id\":\"long-burn\",\"tier\":1," +
        "\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
        "\"delivery\":{\"type\":\"targetUnit\",\"range\":\"medium\"}," +
        "\"clauses\":[{\"verb\":\"applyStatus\",\"status\":\"burn\",\"duration\":\"long\",\"share\":1.0}]}";

    [Fact]
    public void Determinism_SameSpellSameSeed_IdenticalFile()
    {
        string path = Temp(BareSpell);
        string a = Export(path, seed: 7);
        string b = Export(path, seed: 7);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("bare")]     // bare proposal spell
    [InlineData("seed")]     // seed card: {..., "spell": {...}}
    [InlineData("record")]   // fusion record: {..., "spell": {...}}
    public void AllThreeInputShapes_Run(string shape)
    {
        string path = shape switch
        {
            "bare" => Temp(BareSpell),
            "seed" => PromptTemplate.LocateRepoFile("fixtures/seeds/ember.seed.json"),
            // the archived v3 record: stable and committed — the live corpus is re-fused per prompt
            // generation and its filenames change, so it must not anchor a test.
            _ => PromptTemplate.LocateRepoFile("arena/fusions-v3-archive/steam.record.json"),
        };
        var (r, rec) = Showcase.Run(path, 1, Showcase.DefaultMaxSeconds);
        Assert.NotEmpty(rec.Events);
        Assert.Equal("dummy", r.KitB);
        Assert.Contains(r.EndReason, new[] { "quiescent", "cap" });
    }

    [Fact]
    public void InertTarget_EmitsZeroCasts()
    {
        var (_, rec) = Showcase.Run(PromptTemplate.LocateRepoFile("fixtures/seeds/ember.seed.json"), 1, 30f);
        int dummyId = rec.Entities.Single(e => e.Name == "dummy").Id;
        Assert.DoesNotContain(rec.Events, e => e.Event is CastStarted c && c.Caster.Value == dummyId);
        Assert.Single(rec.Events.Where(e => e.Event is CastStarted)); // exactly the one showcase cast
    }

    [Fact]
    public void Quiescence_EndsEarly_WithTail()
    {
        // ember: burn (medium duration) — the horizon ends well under the 30s cap.
        var (r, _) = Showcase.Run(PromptTemplate.LocateRepoFile("fixtures/seeds/ember.seed.json"), 1, 30f);
        Assert.Equal("quiescent", r.EndReason);
        Assert.True(r.DurationSeconds < 30f, $"expected early quiescence, got {r.DurationSeconds}s");
    }

    [Fact]
    public void Cap_FiresWhenEffectsOutliveIt()
    {
        // a long (10s) status with a 2s cap: the horizon outlives the cap → endReason "cap".
        var (r, _) = Showcase.Run(Temp(LongStatusSpell), 1, maxSeconds: 2f);
        Assert.Equal("cap", r.EndReason);
        Assert.True(r.DurationSeconds >= 2f && r.DurationSeconds < 4f);
    }

    [Fact]
    public void SchemaRoundTrip_ThroughExistingReplayReader()
    {
        string path = PromptTemplate.LocateRepoFile("fixtures/seeds/ember.seed.json");
        var (r, rec) = Showcase.Run(path, 1, 30f);
        var json = Showcase.BuildReplay(r, rec, path, 30f);

        Assert.Equal("1", json["schemaVersion"]!.GetValue<string>());          // schema stays 1
        Assert.Equal(r.Projection, Replay.RenderFromJson(json));               // reconstruct → Canonical()

        var sw = new StringWriter();
        Assert.Equal(0, Replay.Verify(json, diffAgainstLive: false, sw));      // strict header accepts showcase
    }

    [Fact]
    public void Batch_ProducesOnePerCorpusInput_AndManifest()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "showcases-" + Guid.NewGuid().ToString("N"));
        string doc = Path.Combine(outDir, "coverage.md");
        Showcase.RunBatch(outDir, doc, 1, TextWriter.Null);

        // one showcase per corpus input — mirrors RunBatch's own globs, so it tracks corpus growth
        // (the v4 re-fusion + anchor children) instead of pinning a stale count.
        int expected =
            Directory.GetFiles(Path.Combine(PromptTemplate.RepoRoot(), "fixtures", "seeds"), "*.seed.json").Length +
            Directory.GetFiles(Path.Combine(PromptTemplate.ArenaDir(), "fusions"), "*.record.json").Length;
        Assert.Equal(expected, Directory.GetFiles(outDir, "*.replay.json").Length);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(outDir, "manifest.json")))!;
        Assert.Equal(expected, manifest["spells"]!.AsArray().Count);
        Assert.Contains("Missing tokens", File.ReadAllText(doc), StringComparison.Ordinal);
    }

    private static string Export(string path, long seed)
    {
        var (r, rec) = Showcase.Run(path, seed, Showcase.DefaultMaxSeconds);
        return Showcase.BuildReplay(r, rec, path, Showcase.DefaultMaxSeconds).ToJsonString();
    }

    private static string Temp(string json)
    {
        string p = Path.Combine(Path.GetTempPath(), "showcase-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, json);
        return p;
    }
}
