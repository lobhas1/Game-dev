using System.Reflection;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// Doctrine-style drift guard: BOTH single-authority prompts (the proposal oracle and the fusion-
// mechanics oracle, which duplicates the same closed vocabulary) must name every T0 verb, every
// slice delivery, every band the C# balance tables define, and every StatKind name. The band guard
// is bidirectional — a prompt must not declare a band the tables don't define. Declared bands are
// read from the comma-separated lists under each prompt's "## Qualitative bands" section.
public class PromptVocabularySyncTests
{
    public static IEnumerable<object[]> Prompts()
    {
        yield return new object[] { "prompts/proposal-oracle.md" };
        yield return new object[] { "prompts/fusion-mechanics-oracle.md" };
    }

    [Theory]
    [MemberData(nameof(Prompts))]
    public void Prompt_ContainsEveryVerb_Delivery_AndBand(string promptPath)
    {
        string prompt = File.ReadAllText(PromptTemplate.LocateRepoFile(promptPath));

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

    // every legal stat name must appear in the prompt, or modifyStat is unusable (the oracle guesses
    // names like "speed"/"attackSpeed" that hard-fail the parser and bias the marginals).
    [Theory]
    [MemberData(nameof(Prompts))]
    public void Prompt_NamesEveryStatKind(string promptPath)
    {
        string prompt = File.ReadAllText(PromptTemplate.LocateRepoFile(promptPath));
        var stats = Enum.GetValues<StatKind>()
            .Select(s => { string n = s.ToString(); return char.ToLowerInvariant(n[0]) + n.Substring(1); })
            .ToList();
        Assert.NotEmpty(stats);
        foreach (var stat in stats)
            Assert.Contains(stat, prompt, StringComparison.Ordinal);
    }

    // the reverse direction — a prompt must not declare a band the tables don't define.
    [Theory]
    [MemberData(nameof(Prompts))]
    public void Prompt_DeclaresNoBandOutsideTheTables(string promptPath)
    {
        string prompt = File.ReadAllText(PromptTemplate.LocateRepoFile(promptPath));
        var tableBands = BandNamesFromTables().ToHashSet(StringComparer.Ordinal);
        var declared = DeclaredBandsInPrompt(prompt).ToList();
        Assert.NotEmpty(declared); // guard against a vacuous pass if the section moves/renames
        foreach (var b in declared)
            Assert.True(tableBands.Contains(b), $"{promptPath} lists band '{b}' that BalanceTables does not define");
    }

    // Bands are the comma-separated tokens after the last colon of each bullet under the prompt's
    // "## Qualitative bands" section (field names sit before the colon, in backticks).
    private static IEnumerable<string> DeclaredBandsInPrompt(string prompt)
    {
        bool inSection = false;
        foreach (var raw in prompt.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = line.Contains("Qualitative bands", StringComparison.Ordinal);
                continue;
            }
            if (!inSection || !line.TrimStart().StartsWith("- ", StringComparison.Ordinal)) continue;
            int colon = line.LastIndexOf(':');
            if (colon < 0) continue;
            foreach (var tok in line.Substring(colon + 1).Trim().TrimEnd('.').Split(','))
            {
                string t = tok.Trim();
                if (t.Length > 0) yield return t;
            }
        }
    }
}
