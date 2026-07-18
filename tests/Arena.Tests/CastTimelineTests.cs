using System.Text.Json.Nodes;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// Cast-time round (spec docs/specs/2026-07-17-cast-time.md), replay side: recorded timelines carry
// the real CastStarted→CastResolved gap, and CastResolved round-trips through export → reconstruct
// → verify like every other event.
public class CastTimelineTests
{
    private const string QuickCastSpell =
        "{\"id\":\"wind-bolt\",\"tier\":1," +
        "\"cast\":{\"mode\":\"cast\",\"castTime\":\"quick\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
        "\"delivery\":{\"type\":\"targetUnit\",\"range\":\"medium\"}," +
        "\"clauses\":[{\"verb\":\"damage\",\"element\":\"air\",\"share\":1.0}]}";

    private static string Temp(string json)
    {
        string p = Path.Combine(Path.GetTempPath(), "casttime-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, json);
        return p;
    }

    private static float T(JsonNode replay, string type) =>
        (float)replay["events"]!.AsArray()
            .First(e => e!["type"]!.GetValue<string>() == type)!["t"]!.GetValue<double>();

    // ── the exported showcase timeline shows the declared window: start at 0, resolve at 0.4 ──
    [Fact]
    public void ShowcaseReplay_CarriesTheDeclaredWindow()
    {
        string path = Temp(QuickCastSpell);
        var (r, rec) = Showcase.Run(path, 1, Showcase.DefaultMaxSeconds);
        var json = Showcase.BuildReplay(r, rec, path, Showcase.DefaultMaxSeconds);

        Assert.Equal(0.0, T(json, "CastStarted"), 3);
        Assert.Equal(0.4, T(json, "CastResolved"), 3);              // quick = 0.4s window
        Assert.Equal(T(json, "CastResolved"), T(json, "DamageDealt"), 3); // effect at the resolution instant
    }

    // ── CastResolved round-trips: reconstruct → Canonical, and strict verify accepts the file ──
    [Fact]
    public void CastResolved_RoundTripsThroughExportAndVerify()
    {
        string path = Temp(QuickCastSpell);
        var (r, rec) = Showcase.Run(path, 1, Showcase.DefaultMaxSeconds);
        var json = Showcase.BuildReplay(r, rec, path, Showcase.DefaultMaxSeconds);

        Assert.Contains("castResolved E1 wind-bolt", Replay.RenderFromJson(json)); // reconstructed line
        Assert.Equal(r.Projection, Replay.RenderFromJson(json));                   // full projection intact

        var sw = new StringWriter();
        Assert.Equal(0, Replay.Verify(json, diffAgainstLive: false, sw));
    }

    // ── fight replays: every successful cast resolves; failed casts never do ──
    [Fact]
    public void FightReplay_ResolvesExactlyTheSuccessfulCasts()
    {
        var frost = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/frost.json"));
        var ember = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/ember.json"));
        var rec = new ReplayRecorder();
        var r = FightEngine.Run(frost, ember, 1, FightConfig.Legacy, rec);

        int started = rec.Events.Count(e => e.Event is CastStarted);
        int resolved = rec.Events.Count(e => e.Event is CastResolved);
        int failed = rec.Events.Count(e => e.Event is CastFailed);
        Assert.Equal(r.CastsA + r.CastsB, resolved); // one per successful cast (TryCast counts successes)
        Assert.Equal(started, resolved + failed);    // every started cast either resolved or failed
    }
}
