using System.Globalization;
using Xunit;
using Arena;

namespace Arena.Tests;

// Drives the pure Evaluator.Evaluate with synthetic generation.log + results.csv fixtures.
public class EvaluateTests
{
    private const string PromptV1 = "PROMPT VOCABULARY v1";
    private static string CurrentSha => Evaluator.Sha256Hex(PromptV1);
    private const string StaleSha = "0000000000000000000000000000000000000000000000000000000000000000";

    private static string GenRow(string sha, string brief, int gen, int fp, int acc, string[] kits) =>
        GenerationLog.Format(new GenerationLogEntry(
            "2026-07-08T00:00:00Z", sha, "claude-sonnet-4-6", brief, 1, 3, gen, fp, acc,
            kits, new[] { new KeyValuePair<string, int>("damage", gen) }));

    private static string Csv(params string[] rows) =>
        "kitA,kitB,seed,duration,endReason,winner,castsA,castsB,distinctVerbs,statuses,leadChanges,dmgToA,dmgToB\n"
        + string.Join("\n", rows);

    private static string Fight(double dur, int distinctVerbs, int leadChanges, int seed = 1) =>
        $"kA,kB,{seed},{dur.ToString(CultureInfo.InvariantCulture)},death,kA,0,0,{distinctVerbs},0,{leadChanges},0,0";

    [Fact]
    public void Pass_AllCriteriaMet()
    {
        string log = GenRow(CurrentSha, "brief-a", 6, 6, 6, new[] { "arena/kits/k1.json" });
        string csv = Csv(Fight(30, 4, 1, 1), Fight(40, 4, 2, 2));
        var r = Evaluator.Evaluate(log, csv, PromptV1, new[] { "k1.json" });

        Assert.True(r.C1Pass);
        Assert.True(r.C2Pass);
        Assert.True(r.C3Pass);
        Assert.True(r.C4Pass);
        Assert.Empty(r.UnrecordedKits);
        Assert.Equal(Verdict.Pass, r.Overall);
    }

    [Fact]
    public void Fail_MedianOutOfWindow()
    {
        string log = GenRow(CurrentSha, "brief-a", 6, 6, 6, new[] { "arena/kits/k1.json" });
        string csv = Csv(Fight(5, 4, 1, 1), Fight(6, 4, 1, 2)); // median 5.5s < 15s
        var r = Evaluator.Evaluate(log, csv, PromptV1, new[] { "k1.json" });

        Assert.True(r.C1Pass);
        Assert.False(r.C2Pass);
        Assert.Equal(Verdict.Fail, r.Overall);
    }

    [Fact]
    public void Incomplete_NoMatchingHashRows()
    {
        string log = GenRow(StaleSha, "brief-a", 6, 6, 6, new[] { "arena/kits/k1.json" }); // wrong hash
        string csv = Csv(Fight(30, 4, 1, 1));
        var r = Evaluator.Evaluate(log, csv, PromptV1, new[] { "k1.json" });

        Assert.False(r.C1HasData);
        Assert.Equal(Verdict.Incomplete, r.Overall);
    }

    [Fact]
    public void StaleHashRows_DoNotCountTowardC1()
    {
        string log = string.Join("\n",
            GenRow(StaleSha, "old", 10, 10, 10, new[] { "arena/kits/k1.json" }), // 100% but stale — must be ignored
            GenRow(CurrentSha, "brief-a", 6, 3, 3, new[] { "arena/kits/k1.json" })); // 50%, current
        string csv = Csv(Fight(30, 4, 1, 1), Fight(40, 4, 2, 2));
        var r = Evaluator.Evaluate(log, csv, PromptV1, new[] { "k1.json" });

        Assert.Equal(6, r.C1Generated);   // not 16
        Assert.Equal(3, r.C1FirstPass);   // not 13
        Assert.False(r.C1Pass);           // 50% < 70%
        Assert.Equal(Verdict.Fail, r.Overall);
    }

    [Fact]
    public void Incomplete_ForeignKitNotRecorded()
    {
        string log = GenRow(CurrentSha, "brief-a", 6, 6, 6, new[] { "arena/kits/k1.json" });
        string csv = Csv(Fight(30, 4, 1, 1));
        var r = Evaluator.Evaluate(log, csv, PromptV1, new[] { "k1.json", "foreign.json" });

        Assert.Contains("foreign.json", r.UnrecordedKits);
        Assert.Equal(Verdict.Incomplete, r.Overall);
    }

    [Fact]
    public void PromptSha_IsLineEndingInvariant()
    {
        const string lf = "vocab line one\nline two\nline three\n";
        string crlf = lf.Replace("\n", "\r\n");
        string cr = lf.Replace("\n", "\r");
        Assert.Equal(Evaluator.Sha256Hex(lf), Evaluator.Sha256Hex(crlf));
        Assert.Equal(Evaluator.Sha256Hex(lf), Evaluator.Sha256Hex(cr));
    }

    [Fact]
    public void GenerationLogLine_RoundTrips()
    {
        var e = new GenerationLogEntry(
            "2026-07-08T12:34:56Z", CurrentSha, "claude-sonnet-4-6", "a patient frost controller",
            2, 3, 6, 5, 5,
            new[] { "arena/kits/frost-1.json", "arena/kits/frost-2.json" },
            new[] { new KeyValuePair<string, int>("applyStatus", 4), new KeyValuePair<string, int>("damage", 4) });

        var p = GenerationLog.Parse(GenerationLog.Format(e));

        Assert.NotNull(p);
        Assert.Equal(e.Timestamp, p!.Timestamp);
        Assert.Equal(e.PromptSha, p.PromptSha);
        Assert.Equal(e.Model, p.Model);
        Assert.Equal(e.Brief, p.Brief);
        Assert.Equal(e.Tier, p.Tier);
        Assert.Equal(e.KitSize, p.KitSize);
        Assert.Equal(e.Generated, p.Generated);
        Assert.Equal(e.FirstPass, p.FirstPass);
        Assert.Equal(e.Accepted, p.Accepted);
        Assert.Equal(e.Kits, p.Kits);
        Assert.Equal(e.Marginals, p.Marginals);
    }
}
