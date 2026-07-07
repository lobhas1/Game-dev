using Xunit;
using Arena;

namespace Arena.Tests;

// SpellJson.Parse (+ compile) is the sole gate, with exactly one repair attempt per spell.
// Driven by a deterministic StubOracle so the path runs fully offline.
public class ParserGateTests
{
    private const string Template = "brief={{BRIEF}} tier={{TIER}} size={{KIT_SIZE}}";

    private const string ValidSpell =
        "{\"id\":\"test-bolt\",\"tier\":1,\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}},\"delivery\":{\"type\":\"projectile\",\"speed\":\"fast\"},\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0}]}";

    // unknown verb → fails at parse
    private const string UnknownVerb =
        "{\"id\":\"x\",\"tier\":1,\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}},\"delivery\":{\"type\":\"projectile\",\"speed\":\"fast\"},\"clauses\":[{\"verb\":\"floop\",\"element\":\"fire\",\"share\":1.0}]}";
    // unknown band ("eternal") → fails at compile
    private const string BadBand =
        "{\"id\":\"x\",\"tier\":1,\"cast\":{\"mode\":\"instant\",\"cooldown\":\"eternal\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}},\"delivery\":{\"type\":\"projectile\",\"speed\":\"fast\"},\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0}]}";
    // unknown field ("bogus") → fails at parse (strict RequireNoExtra)
    private const string ExtraField =
        "{\"id\":\"x\",\"tier\":1,\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}},\"delivery\":{\"type\":\"projectile\",\"speed\":\"fast\"},\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0,\"bogus\":7}]}";

    [Fact]
    public async Task ValidProposal_Accepted_WithoutRepair()
    {
        var stub = new StubOracle($"[{ValidSpell}]");
        var o = await KitGenerator.GateKitAsync(stub, Template, "brief", 1, 1);
        Assert.Equal(1, o.Generated);
        Assert.Equal(1, o.FirstPassOk);
        Assert.Equal(1, o.AcceptedTotal);
        Assert.Empty(o.Rejections);
        Assert.Equal(1, stub.CallCount); // no repair call
    }

    [Theory]
    [InlineData(UnknownVerb)]
    [InlineData(BadBand)]
    [InlineData(ExtraField)]
    public async Task InvalidProposal_Rejected_AfterFailedRepair(string bad)
    {
        var stub = new StubOracle($"[{bad}]", bad); // kit → [bad]; repair → still bad
        var o = await KitGenerator.GateKitAsync(stub, Template, "brief", 1, 1);
        Assert.Equal(0, o.FirstPassOk);
        Assert.Equal(0, o.AcceptedTotal);
        Assert.Single(o.Rejections);
        Assert.StartsWith("no", o.Rejections[0].Repaired);
        Assert.Equal(2, stub.CallCount); // one kit call + one repair call
    }

    [Fact]
    public async Task RepairPath_FirstResponseBad_RepairedResponseGood_Accepted()
    {
        var stub = new StubOracle($"[{UnknownVerb}]", ValidSpell); // kit → [bad]; repair → good
        var o = await KitGenerator.GateKitAsync(stub, Template, "brief", 1, 1);
        Assert.Equal(0, o.FirstPassOk);   // did not pass first time
        Assert.Equal(1, o.AcceptedTotal); // accepted after the one repair
        Assert.Single(o.Rejections);
        Assert.Contains("repaired", o.Rejections[0].Repaired);
        Assert.Equal(2, stub.CallCount);
    }
}
