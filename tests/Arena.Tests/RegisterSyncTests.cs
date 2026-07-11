using Xunit;
using Arena;

namespace Arena.Tests;

// Drift guard for the prompt-v4 Register (docs/specs/2026-07-11-prompt-v4.md): the register prose
// has one home — prompts/naming-oracle.md — and the prototype's embedded prompt must carry the
// load-bearing phrases byte-identically. V1 (the stranger kill test) runs on the PROTOTYPE, so an
// unported register means the test tests the wrong prompt; the doctrine round's lesson was that
// untested embeddings drift. Ordinal comparison: em-dashes and punctuation count.
public class RegisterSyncTests
{
    public static readonly string[] LoadBearing =
    {
        "concrete and physical: what the thing does or looks like in-world, never how mysterious it is.",
        "Never use: threshold, border, veil, whisper(s), essence, ancient, forgotten, eternal, realm, shroud, twilight, betwixt, unseen, beyond.",
        "Vary word choice — do not lean on the same words across many spells.",
    };

    public static readonly string[] Embeddings =
    {
        "prompts/naming-oracle.md",
        "prototype/spellcraft-oracle.html",
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var file in Embeddings)
            foreach (var phrase in LoadBearing)
                yield return new object[] { file, phrase };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmbeddingContainsRegisterPhrase(string relativePath, string phrase)
    {
        string content = File.ReadAllText(PromptTemplate.LocateRepoFile(relativePath));
        Assert.Contains(phrase, content, StringComparison.Ordinal);
    }

    // The pinned never-use phrase must stay in lockstep with the frozen FlavorEval banned list —
    // otherwise the prompt bans one lexicon and the evaluator scores another.
    [Fact]
    public void NeverUsePhrase_MatchesFlavorEvalBannedList()
    {
        string phrase = LoadBearing[1];
        foreach (var token in FlavorEval.BannedTokens)
            Assert.Contains(token, phrase, StringComparison.Ordinal);
    }
}
