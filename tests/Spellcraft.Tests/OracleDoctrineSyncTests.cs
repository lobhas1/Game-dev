using Xunit;

namespace Spellcraft.Tests;

// Drift guard for the oracle naming doctrine (docs/specs/2026-07-07-oracle-naming-doctrine.md).
// The prompt text has ONE home — the doctrine — and every file that embeds it must carry the six
// load-bearing sentences verbatim. If any embedding and the doctrine diverge on a sentence, this
// goes red. Ordinal (byte-exact) comparison: em-dashes, straight quotes, and casing all count.
public class OracleDoctrineSyncTests
{
    // The six load-bearing sentences, byte-identical to prototype/spellcraft-oracle.html
    // (straight quotes U+0022, em-dashes U+2014 in #3 and #5).
    public static readonly string[] LoadBearing =
    {
        "Name the new THING that exists when the two meet: an emergent concept, never a blend of the ingredient names.",
        "The result name must not reuse or lightly modify any word from either ingredient name.",
        "Prefer one plain common noun when one exists, and if the fusion resolves to a concept that plausibly already exists, return that plain name — converging onto known things is desired.",
        "Fire + Rain gives Steam, never \"Misty Flame\" or \"Elemental Duality\". Ember + Shadow gives Smoke, never \"Smoldering Dark\".",
        "Ingredient tags are mechanical bookkeeping — never translate a tag into a name word.",
        "Forbidden name styles: Cosmic, Infinite, Ultimate, Eternal, Supreme, Absolute, Omni-, God-, Primordial, or vague words like Essence/Energy/Power as the head noun.",
    };

    // Every file that embeds the doctrine. Both must contain every load-bearing sentence.
    public static readonly string[] Embeddings =
    {
        "prototype/spellcraft-oracle.html",
        "docs/specs/2026-07-07-oracle-naming-doctrine.md",
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
        string content = ReadRepoFile(relativePath);
        Assert.Contains(sentence, content, StringComparison.Ordinal);
    }

    // Walk up from the test bin/ to the repo root and read a file by its repo-relative path
    // (same pattern as Fixtures.cs).
    private static string ReadRepoFile(string relativePath)
    {
        string relative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException($"'{relativePath}' not found walking up from {AppContext.BaseDirectory}.");
    }
}
