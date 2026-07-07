using Xunit;
using Arena;

namespace Arena.Tests;

public class DeterminismTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalProjection()
    {
        var frost = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/frost.json"));
        var ember = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/ember.json"));

        var r1 = FightEngine.Run(frost, ember, seed: 7);
        var r2 = FightEngine.Run(frost, ember, seed: 7);

        Assert.Equal(r1.Projection, r2.Projection);        // event stream, line for line
        Assert.Equal(r1.EndReason, r2.EndReason);
        Assert.Equal(r1.DurationSeconds, r2.DurationSeconds);
        Assert.Equal(r1.Winner, r2.Winner);
    }
}
