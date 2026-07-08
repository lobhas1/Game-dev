using System.Text.Json.Nodes;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// Phase D milestone 1: replay export + verify. The gate is byte-for-byte agreement on the sim's
// projection lines; these tests pin producers 1 (live) and 2 (replay-verify) at the C# layer.
public class ReplayTests
{
    private static (Kit, Kit) Fixtures() =>
        (Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/frost.json")),
         Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/ember.json")));

    private static (FightResult, JsonObject) Export(long seed = 1)
    {
        var (frost, ember) = Fixtures();
        var rec = new ReplayRecorder();
        var r = FightEngine.Run(frost, ember, seed, FightConfig.Legacy, rec);
        return (r, Replay.Export(r, rec, "fixtures/kits/frost.json", "fixtures/kits/ember.json"));
    }

    [Fact]
    public void RoundTrip_ProjectionByteIdentical_OnFixtureFight()
    {
        var (r, json) = Export();
        var rendered = Replay.RenderFromJson(json);       // reconstruct events → Canonical()
        Assert.NotEmpty(rendered);
        Assert.Equal(r.Projection, rendered);             // line-for-line identical to the live sim
    }

    [Fact]
    public void Export_IsDeterministic_SameFightSameBytes()
    {
        Assert.Equal(Export().Item2.ToJsonString(), Export().Item2.ToJsonString());
    }

    [Fact]
    public void Export_CarriesSchemaVersion1()
    {
        var (_, json) = Export();
        Assert.Equal("1", json["schemaVersion"]!.GetValue<string>());
        Assert.NotNull(json["header"]);
        Assert.NotNull(json["events"]);
    }

    [Fact]
    public void Verify_FailsLoudly_OnTamperedCanonical()
    {
        var (_, json) = Export();
        ((JsonObject)json["events"]!.AsArray()[0]!)["canonical"] = "TAMPERED";

        var sw = new StringWriter();
        int code = Replay.Verify(json, diffAgainstLive: false, sw);
        Assert.NotEqual(0, code);
        Assert.Contains("ERROR", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_FailsLoudly_OnBadSchemaVersion()
    {
        var (_, json) = Export();
        json["schemaVersion"] = "999";
        var sw = new StringWriter();
        Assert.NotEqual(0, Replay.Verify(json, diffAgainstLive: false, sw));
        Assert.Contains("schemaVersion", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_FailsLoudly_OnMissingHeaderField()
    {
        var (_, json) = Export();
        ((JsonObject)json["header"]!).Remove("winner");   // the reviewer's repro: required key gone
        var sw = new StringWriter();
        Assert.NotEqual(0, Replay.Verify(json, diffAgainstLive: false, sw));
        Assert.Contains("header.winner", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_FailsLoudly_OnWrongWinner()
    {
        var (_, json) = Export();
        var names = json["header"]!["entities"]!.AsArray().Select(e => e!["name"]!.GetValue<string>()).ToList();
        string cur = json["header"]!["winner"]!.GetValue<string>();
        json["header"]!["winner"] = names.First(n => n != cur);   // name the loser (killed) as winner
        var sw = new StringWriter();
        Assert.NotEqual(0, Replay.Verify(json, diffAgainstLive: false, sw));
        Assert.Contains("winner", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_FailsLoudly_OnManifestIdMismatch()
    {
        var (_, json) = Export();
        ((JsonObject)json["header"]!["entities"]!.AsArray()[0]!)["id"] = 999; // events still reference the real id
        var sw = new StringWriter();
        Assert.NotEqual(0, Replay.Verify(json, diffAgainstLive: false, sw));
        Assert.Contains("absent from the manifest", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Clean_RendersEveryProjectionLine()
    {
        var (r, json) = Export();
        var sw = new StringWriter();
        int code = Replay.Verify(json, diffAgainstLive: false, sw);
        Assert.Equal(0, code);
        var printed = sw.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal(r.Projection, printed);
    }
}
