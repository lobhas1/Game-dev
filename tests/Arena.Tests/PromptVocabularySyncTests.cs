using System.Reflection;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// Doctrine-style drift guard: the single-authority prompt must name every T0 verb, every slice
// delivery, and every band the C# balance tables define. If the vocabulary drifts, this goes red.
public class PromptVocabularySyncTests
{
    [Fact]
    public void Prompt_ContainsEveryVerb_Delivery_AndBand()
    {
        string prompt = File.ReadAllText(PromptTemplate.LocateRepoFile("prompts/proposal-oracle.md"));

        var verbs = T0VerbJsonNames().ToList();
        var bands = BandNamesFromTables().ToList();
        Assert.NotEmpty(verbs); // guard against a vacuous pass
        Assert.NotEmpty(bands);

        foreach (var verb in verbs)
            Assert.Contains(verb, prompt, StringComparison.Ordinal);

        foreach (var delivery in new[] { "self", "targetUnit", "projectile", "groundAoE" })
            Assert.Contains(delivery, prompt, StringComparison.Ordinal);

        foreach (var band in bands)
            Assert.Contains(band, prompt, StringComparison.Ordinal);
    }

    // The eight T0 verbs, in the JSON (camelCase) form the oracle emits.
    private static IEnumerable<string> T0VerbJsonNames() =>
        Enum.GetValues<VerbId>().Take(8).Select(v =>
        {
            string n = v.ToString();
            return char.ToLowerInvariant(n[0]) + n.Substring(1);
        });

    // Reflect BalanceTables' private band dictionaries so band drift is caught without modifying
    // src/Spellcraft (spec §7.2).
    private static IEnumerable<string> BandNamesFromTables() =>
        typeof(BalanceTables)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Dictionary<string, float>))
            .SelectMany(f => ((Dictionary<string, float>)f.GetValue(null)!).Keys)
            .Distinct();
}
