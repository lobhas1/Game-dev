using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

// Phase A v4: the amplify 'major' multiplier as an explicit, threaded run override. All offline.
public class V4Tests
{
    // A single-clause strike that amplifies on a pre-existing burn (the v3 kill shape).
    private const string StrikeIfBurnMajor =
        "{\"id\":\"amp-strike\",\"tier\":1," +
        "\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
        "\"delivery\":{\"type\":\"targetUnit\",\"range\":\"medium\"}," +
        "\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0," +
        "\"template\":{\"kind\":\"ifStatus\",\"status\":\"burn\",\"amplify\":\"major\"}}]}";

    private const string BurnApplier =
        "{\"id\":\"burn-it\",\"tier\":1," +
        "\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
        "\"delivery\":{\"type\":\"targetUnit\",\"range\":\"medium\"}," +
        "\"clauses\":[{\"verb\":\"applyStatus\",\"status\":\"burn\",\"duration\":\"medium\",\"share\":0.5}]}";

    private const string Header13 = "kitA,kitB,seed,duration,endReason,winner,castsA,castsB,distinctVerbs,statuses,leadChanges,dmgToA,dmgToB";
    private const string Header18 = Header13 + ",hpScale,hpA,hpB,mana,amplifyMajor";

    // ── plumbing: the override bites only the 'major' band, only when supplied ──

    [Fact]
    public void Compile_OverridesMajorAmplify_NullIsDefault()
    {
        var def = SpellCompiler.Compile(SpellJson.Parse(StrikeIfBurnMajor)); // null overrides
        var low = SpellCompiler.Compile(SpellJson.Parse(StrikeIfBurnMajor), new BalanceOverrides { AmplifyMajor = 1.5f });
        Assert.Equal(2.5f, ((IfStatusTemplate)def.Clauses[0].Template!).Amplify); // committed default
        Assert.Equal(1.5f, ((IfStatusTemplate)low.Clauses[0].Template!).Amplify); // overridden
    }

    [Fact]
    public void Compile_LeavesMinorBandUntouched()
    {
        string minor = StrikeIfBurnMajor.Replace("\"major\"", "\"minor\"");
        var c = SpellCompiler.Compile(SpellJson.Parse(minor), new BalanceOverrides { AmplifyMajor = 3.0f });
        Assert.Equal(1.5f, ((IfStatusTemplate)c.Clauses[0].Template!).Amplify); // minor stays 1.5
    }

    [Fact]
    public void KitLoad_ThreadsOverrideIntoCompile()
    {
        string path = WriteTempKit("[" + StrikeIfBurnMajor + "]");
        var def = Kit.Load(path);
        var low = Kit.Load(path, new BalanceOverrides { AmplifyMajor = 1.5f });
        Assert.Equal(2.5f, ((IfStatusTemplate)def.Spells[0].Clauses[0].Template!).Amplify);
        Assert.Equal(1.5f, ((IfStatusTemplate)low.Spells[0].Clauses[0].Template!).Amplify);
    }

    // ── defaults-regression: the no-override fight is unchanged and records the effective 2.5 ──

    [Fact]
    public void DefaultFight_IsUnchanged_AndRecordsEffective2point5()
    {
        var frost = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/frost.json"));
        var ember = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/ember.json"));
        var r = FightEngine.Run(frost, ember, 1); // legacy default config
        Assert.Equal("ember", r.Winner);
        Assert.Equal("death", r.EndReason);
        Assert.Equal(37.6, r.DurationSeconds, 1);
        Assert.Equal(2.5f, r.AmplifyMajor); // effective default resolved from BalanceTables
    }

    // ── amplify sensitivity: same seed, lower multiplier ⇒ lower peak hit, different projection ──

    [Fact]
    public void AmplifySensitivity_LowerMajor_LowerPeak_DifferentProjection()
    {
        var hi = RunAmp(2.5f);
        var lo = RunAmp(1.5f);
        Assert.True(hi.Peak > lo.Peak, $"peak at 2.5 ({hi.Peak}) should exceed peak at 1.5 ({lo.Peak})");
        Assert.NotEqual(hi.Projection, lo.Projection);
    }

    private static (float Peak, IReadOnlyList<string> Projection) RunAmp(float major)
    {
        var sim = new Sim(7, new BalanceOverrides { AmplifyMajor = major });
        var caster = sim.State.AddEntity("c", Faction.Player, 5000f, new Vec3(0, 0, 0));
        var target = sim.State.AddEntity("t", Faction.Enemy, 5000f, new Vec3(2, 0, 0));
        caster.Resources[ResourceKind.Mana] = 1000f;
        sim.Cast(caster.Ref, SpellJson.Parse(BurnApplier), target.Ref);   // land burn
        sim.Cast(caster.Ref, SpellJson.Parse(StrikeIfBurnMajor), target.Ref); // amplified strike
        float peak = sim.Events.Events.OfType<DamageDealt>().Select(d => d.Amount).DefaultIfEmpty(0f).Max();
        return (peak, sim.Events.Projection());
    }

    // ── evaluate config key = (hpScale, mana, amplifyMajor) ──

    [Fact]
    public void AmplifyColumn_RoundTripsThroughEvaluate()
    {
        string csv = Header18 + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0,8.0,800.0,800.0,250.0,1.75";
        var r = Evaluator.Evaluate("", csv, "P", new[] { "kA.json", "kB.json" });
        Assert.Single(r.ConfigKeys);
        Assert.Contains("amplifyMajor 1.75", r.ConfigDisplay);
        Assert.Contains("hpScale 8", r.ConfigDisplay);
    }

    [Fact]
    public void MixedAmplify_MakesEvaluateFlagIt()
    {
        string csv = Header18
            + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0,8.0,800.0,800.0,250.0,2.5"
            + "\nkA,kB,2,30.0,death,kA,0,0,4,0,1,0,0,8.0,800.0,800.0,250.0,1.5"; // differ only in amplify
        var r = Evaluator.Evaluate("", csv, "P", new[] { "kA.json" });
        Assert.True(r.ConfigMixed);
        Assert.Equal(2, r.ConfigKeys.Count);
    }

    [Fact]
    public void LegacyCsvWithoutAmplifyColumn_ReadsAs2point5()
    {
        string csv = Header13 + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0";
        var r = Evaluator.Evaluate("", csv, "P", new[] { "kA.json" });
        Assert.Contains("amplifyMajor 2.5", r.ConfigDisplay);
    }

    // ── per-run C1 disclosure table ──

    [Fact]
    public void PerRunTable_ListsEachMatchingRun()
    {
        string sha = Evaluator.Sha256Hex("P");
        string log = string.Join("\n",
            Row(sha, "2026-07-07T21:28:53Z"),
            Row(sha, "2026-07-08T10:00:00Z"));
        var r = Evaluator.Evaluate(log, "", "P", new[] { "k1.json" });
        Assert.Equal(2, r.MatchingRuns.Count);

        var doc = Evaluator.FormatVerdictDoc(r, "2026-07-08");
        Assert.Contains("by generation run", doc);
        Assert.Contains("2026-07-07T21:28:53Z", doc);
        Assert.Contains("2026-07-08T10:00:00Z", doc);

        static string Row(string sha, string ts) => GenerationLog.Format(new GenerationLogEntry(
            ts, sha, "m", "a brief", 1, 3, 6, 6, 6,
            new[] { "arena/kits/k1.json" }, new[] { new KeyValuePair<string, int>("damage", 6) }));
    }

    // ── amplify sweep smoke ──

    [Fact]
    public void AmplifySweep_ProducesARowPerValue()
    {
        var paths = new[]
        {
            PromptTemplate.LocateRepoFile("fixtures/kits/frost.json"),
            PromptTemplate.LocateRepoFile("fixtures/kits/ember.json")
        };
        var rows = Sweep.ComputeAmplify(paths, new long[] { 1, 2 }, hpScale: 8f, mana: 250f, new[] { 2.5f, 1.5f });
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Fights > 0));
        Assert.Equal(2.5f, rows[0].AmplifyMajor);
        Assert.Equal(1.5f, rows[1].AmplifyMajor);
    }

    private static string WriteTempKit(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), "arena-v4-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }
}
