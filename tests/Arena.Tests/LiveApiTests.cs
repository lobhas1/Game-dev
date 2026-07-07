using Xunit;
using Arena;

namespace Arena.Tests;

// Conditional skip with no extra NuGet package: a FactAttribute that sets Skip when the key is
// unset — a true skip offline, a real run only when ANTHROPIC_API_KEY is present.
public sealed class LiveApiFactAttribute : FactAttribute
{
    public LiveApiFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            Skip = "ANTHROPIC_API_KEY not set — live API test skipped.";
    }
}

public class LiveApiTests
{
    // Runs only on the human's machine (key set). Exercises the real oracle end-to-end through the
    // same gate the offline tests use. Never touches the network offline.
    [LiveApiFact]
    public async Task LiveOracle_GeneratesAndGatesAKit()
    {
        string template = PromptTemplate.LoadPrompt();
        IOracle oracle = new LiveAnthropicOracle("claude-sonnet-4-6");
        var outcome = await KitGenerator.GateKitAsync(oracle, template, "a patient frost controller", 1, 3);
        Assert.True(outcome.Generated > 0, "the oracle returned no parseable spell array");
    }
}
