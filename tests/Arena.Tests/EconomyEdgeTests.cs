using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

public class EconomyEdgeTests
{
    // A self-only kit: both fighters spend mana on self-shields (no damage), so nobody dies and
    // mana is exhausted → the fight must end 'oom' with no crash and no infinite loop.
    [Fact]
    public void ResourceExhaustion_EndsOom_NoCrashNoInfiniteLoop()
    {
        const string selfShield =
            "{\"id\":\"ward\",\"tier\":1,\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\"," +
            "\"cost\":{\"resource\":\"mana\",\"amount\":\"high\"}},\"delivery\":{\"type\":\"self\"}," +
            "\"clauses\":[{\"verb\":\"shield\",\"share\":1.0,\"duration\":\"short\"}]}";
        var spell = SpellCompiler.Compile(SpellJson.Parse(selfShield));
        var kit = new Kit("ward-only", new[] { spell });

        var r = FightEngine.Run(kit, kit, seed: 1);

        Assert.Equal("oom", r.EndReason);
        Assert.True(r.DurationSeconds < FightEngine.TimeoutSeconds); // ended early via oom detection
        Assert.Equal("draw", r.Winner);                             // no damage was ever dealt
        Assert.Equal(0f, r.DamageToA);
        Assert.Equal(0f, r.DamageToB);
    }
}
