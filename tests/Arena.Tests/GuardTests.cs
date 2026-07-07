using System.Globalization;
using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// Phase A v3 guard fixes: verdict-file uniqueness / refuse-overwrite, and the cross-tier scoring
// guard. All offline — pure functions and temp-dir file I/O; no network, no repo mutation.
public class GuardTests
{
    // ── Phase 1.1: verdict-file uniqueness + refuse-overwrite ──

    [Fact]
    public void VerdictFileName_CarriesTimestampAndProtocol()
    {
        var ts = new DateTime(2026, 7, 7, 13, 5, 9, DateTimeKind.Utc);
        Assert.Equal("2026-07-07-130509-phase-a-verdict-v4.md", Evaluator.VerdictFileName(ts));
    }

    [Fact]
    public void DoubleEvaluate_SameDay_ProducesTwoFiles_NoOverwrite()
    {
        string dir = NewTempDir();
        var t1 = new DateTime(2026, 7, 7, 13, 5, 9, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 7, 7, 13, 5, 10, DateTimeKind.Utc); // same day & minute, +1s
        string p1 = Evaluator.WriteVerdictDoc(dir, t1, "doc one");
        string p2 = Evaluator.WriteVerdictDoc(dir, t2, "doc two");

        Assert.NotEqual(p1, p2);
        Assert.True(File.Exists(p1));
        Assert.True(File.Exists(p2));
        Assert.Equal(2, Directory.GetFiles(dir, "*.md").Length);
    }

    [Fact]
    public void WriteVerdictDoc_RefusesToOverwriteExisting()
    {
        string dir = NewTempDir();
        var t = new DateTime(2026, 7, 7, 13, 5, 9, DateTimeKind.Utc);
        string p = Evaluator.WriteVerdictDoc(dir, t, "first");

        var ex = Assert.Throws<IOException>(() => Evaluator.WriteVerdictDoc(dir, t, "second"));
        Assert.Contains("refus", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("first", File.ReadAllText(p)); // original untouched
    }

    // ── Phase 1.2: cross-tier scoring guard ──

    [Fact]
    public void CrossTierRow_IsFlaggedByName()
    {
        var tiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["kA"] = 1, ["kB"] = 2 };
        string csv = Header + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0";

        var offending = Evaluator.FindCrossTierRows(tiers, csv);
        Assert.Single(offending);
        Assert.Contains("kA", offending[0]);
        Assert.Contains("kB", offending[0]);
    }

    [Fact]
    public void SameTierCsv_IsClean()
    {
        var tiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["kA"] = 2, ["kB"] = 2 };
        string csv = Header
            + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0"
            + "\nkB,kA,1,30.0,death,kB,0,0,4,0,1,0,0";

        Assert.Empty(Evaluator.FindCrossTierRows(tiers, csv));
    }

    [Fact]
    public void UnknownKitInRow_IsFlagged()
    {
        var tiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["kA"] = 1 };
        string csv = Header + "\nkA,kGHOST,1,30.0,death,kA,0,0,4,0,1,0,0";

        var offending = Evaluator.FindCrossTierRows(tiers, csv);
        Assert.Single(offending);
        Assert.Contains("kGHOST", offending[0]);
    }

    [Fact]
    public void KitMixesTiers_DetectsMultiTierKit()
    {
        var single = new Kit("s", new[] { SpellAt(2), SpellAt(2) });
        var mixed = new Kit("m", new[] { SpellAt(1), SpellAt(2) });

        Assert.False(Evaluator.KitMixesTiers(single));
        Assert.True(Evaluator.KitMixesTiers(mixed));
    }

    private const string Header = "kitA,kitB,seed,duration,endReason,winner,castsA,castsB,distinctVerbs,statuses,leadChanges,dmgToA,dmgToB";

    private static Spell SpellAt(int tier)
    {
        string json = $"{{\"id\":\"s{tier}\",\"tier\":{tier}," +
            "\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
            "\"delivery\":{\"type\":\"projectile\",\"speed\":\"fast\"}," +
            "\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0}]}";
        return SpellCompiler.Compile(SpellJson.Parse(json));
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "arena-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
