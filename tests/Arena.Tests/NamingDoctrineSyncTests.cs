using Xunit;
using Arena;

namespace Arena.Tests;

// Extends the naming-doctrine drift guard (docs/specs/2026-07-07-oracle-naming-doctrine.md §4) to the
// THIRD embedding: prompts/naming-oracle.md. The six load-bearing sentences have one home — the
// doctrine — and every embedding must carry them byte-identically (em-dashes U+2014, straight quotes
// U+0022, casing all count). The Spellcraft-side test pins the prototype + doctrine; this pins all
// three, including the C# fusion bridge's naming prompt.
public class NamingDoctrineSyncTests
{
    // Byte-identical to tests/Spellcraft.Tests/OracleDoctrineSyncTests.LoadBearing.
    public static readonly string[] LoadBearing =
    {
        "Name the new THING that exists when the two meet: an emergent concept, never a blend of the ingredient names.",
        "The result name must not reuse or lightly modify any word from either ingredient name.",
        "Prefer one plain common noun when one exists, and if the fusion resolves to a concept that plausibly already exists, return that plain name — converging onto known things is desired.",
        "Fire + Rain gives Steam, never \"Misty Flame\" or \"Elemental Duality\". Ember + Shadow gives Smoke, never \"Smoldering Dark\".",
        "Ingredient tags are mechanical bookkeeping — never translate a tag into a name word.",
        "Forbidden name styles: Cosmic, Infinite, Ultimate, Eternal, Supreme, Absolute, Omni-, God-, Primordial, or vague words like Essence/Energy/Power as the head noun.",
    };

    public static readonly string[] Embeddings =
    {
        "prototype/spellcraft-oracle.html",
        "docs/specs/2026-07-07-oracle-naming-doctrine.md",
        "prompts/naming-oracle.md",
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var file in Embeddings)
            foreach (var sentence in LoadBearing)
                yield return new object[] { file, sentence };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmbeddingContainsLoadBearingSentence(string relativePath, string sentence)
    {
        string content = File.ReadAllText(PromptTemplate.LocateRepoFile(relativePath));
        Assert.Contains(sentence, content, StringComparison.Ordinal);
    }

    [Fact]
    public void NamingPrompt_KeepsItsFourPlaceholders()
    {
        string p = File.ReadAllText(PromptTemplate.LocateRepoFile("prompts/naming-oracle.md"));
        foreach (var ph in new[] { "{{SPELL_A_LINE}}", "{{SPELL_B_LINE}}", "{{SELF_FUSION_NOTE}}", "{{TIER}}" })
            Assert.Contains(ph, p, StringComparison.Ordinal);
    }
}
