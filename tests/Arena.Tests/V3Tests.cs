using Xunit;
using Arena;
using Spellcraft;

namespace Arena.Tests;

public class V3Tests
{
    // Regression: with no config, the fixture fight seed 1 must reproduce today's output exactly —
    // the committed golden projection plus the known summary. Guards against config drift.
    [Fact]
    public void Defaults_ReproduceTodaysFight_Seed1()
    {
        var frost = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/frost.json"));
        var ember = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/ember.json"));
        var r = FightEngine.Run(frost, ember, 1); // default (legacy) config

        var golden = File.ReadAllLines(PromptTemplate.LocateRepoFile("tests/Arena.Tests/goldens/frost-ember-seed1.projection.txt"));
        Assert.Equal(golden, r.Projection);

        Assert.Equal("ember", r.Winner);
        Assert.Equal("death", r.EndReason);
        Assert.Equal(37.6, r.DurationSeconds, 1);
        Assert.Equal(11, r.CastsA);
        Assert.Equal(14, r.CastsB);
        Assert.Equal(4, r.DistinctVerbs);
        Assert.Equal(15, r.StatusesApplied);
        Assert.Equal(4, r.LeadChanges);

        // legacy config recorded (flat 300 / mana 250)
        Assert.Equal(0f, r.HpScale);
        Assert.Equal(300f, r.HpA);
        Assert.Equal(300f, r.HpB);
        Assert.Equal(250f, r.Mana);
    }

    [Fact]
    public void HpScale_ScalesHpByTierBudget()
    {
        var frost = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/frost.json")); // tier 1, budget 100
        var ember = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/ember.json"));
        var r = FightEngine.Run(frost, ember, 1, new FightConfig { HpScale = 4f, Mana = 250f });
        Assert.Equal(400f, r.HpA); // 4 × TierBudget(1)=100
        Assert.Equal(400f, r.HpB);
        Assert.Equal(4f, r.HpScale);
    }

    [Fact]
    public void SameTier_SelectsOnlyEqualTierPairs()
    {
        var kits = new[] { KitAtTier("t1a", 1), KitAtTier("t1b", 1), KitAtTier("t2", 2) };

        var same = Rounds.RoundRobin(kits, new long[] { 1 }, FightConfig.Legacy, sameTierOnly: true);
        Assert.NotEmpty(same);
        Assert.All(same, r => Assert.True(r.SameTier));
        Assert.All(same, r => Assert.Equal(1, r.TierA)); // only the two tier-1 kits pair up

        var full = Rounds.RoundRobin(kits, new long[] { 1 }, FightConfig.Legacy, sameTierOnly: false);
        Assert.Contains(full, r => !r.SameTier); // cross-tier pairs present when unfiltered
    }

    [Fact]
    public void Sweep_ProducesARowPerScale()
    {
        var frost = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/frost.json"));
        var ember = Kit.Load(PromptTemplate.LocateRepoFile("fixtures/kits/ember.json"));
        var rows = Sweep.Compute(new[] { frost, ember }, new long[] { 1, 2 }, new[] { 3f, 6f }, 250f);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Fights > 0));
        Assert.All(rows, r => Assert.True(r.Median > 0));
    }

    [Fact]
    public void ConfigColumns_RoundTripThroughEvaluate()
    {
        string scaled = Header17 + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0,4.0,400.0,400.0,250.0";
        var r = Evaluator.Evaluate("", scaled, "P", new[] { "kA.json", "kB.json" });
        Assert.False(r.ConfigMixed);
        Assert.Single(r.ConfigKeys);
        Assert.Contains("hpScale 4", r.ConfigDisplay);

        string legacy = Header13 + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0";
        var r2 = Evaluator.Evaluate("", legacy, "P", new[] { "kA.json", "kB.json" });
        Assert.Single(r2.ConfigKeys);
        Assert.Contains("legacy", r2.ConfigDisplay);
    }

    [Fact]
    public void MixedConfig_MakesEvaluateFlagIt()
    {
        string mixed = Header17
            + "\nkA,kB,1,30.0,death,kA,0,0,4,0,1,0,0,4.0,400.0,400.0,250.0"
            + "\nkA,kB,2,30.0,death,kA,0,0,4,0,1,0,0,6.0,600.0,600.0,250.0"; // different hpScale
        var r = Evaluator.Evaluate("", mixed, "P", new[] { "kA.json" });
        Assert.True(r.ConfigMixed);
        Assert.Equal(2, r.ConfigKeys.Count);
    }

    private const string Header13 = "kitA,kitB,seed,duration,endReason,winner,castsA,castsB,distinctVerbs,statuses,leadChanges,dmgToA,dmgToB";
    private const string Header17 = Header13 + ",hpScale,hpA,hpB,mana";

    private static Kit KitAtTier(string name, int tier)
    {
        string json = $"{{\"id\":\"{name}\",\"tier\":{tier}," +
            "\"cast\":{\"mode\":\"instant\",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
            "\"delivery\":{\"type\":\"projectile\",\"speed\":\"fast\"}," +
            "\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0}]}";
        return new Kit(name, new[] { SpellCompiler.Compile(SpellJson.Parse(json)) });
    }
}
