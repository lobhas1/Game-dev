using Xunit;
using Spellcraft;

namespace Spellcraft.Tests;

// The law (from the review's meta-finding): every status and modifier requires a with-versus-without
// assertion that changes a MEASURED outcome. Presence is not effect. HasStatus(Vulnerable) passing
// while Vulnerable did nothing is exactly how the base-0 GetStat bug shipped. `Vulnerable_...` below
// is the one-line differential that would have caught it.
public class DifferentialTests
{
    private static EffectCtx Ctx(Sim sim, EntityRef caster) => new()
    {
        Caster = caster,
        SpellId = new SpellId("t"),
        ClauseIndex = 0,
        Rng = SeededRng.FromSeed(1),
        State = sim.State,
        World = sim.World,
        Impact = new ImpactCtx(new Vec3(0, 0, 0), new Vec3(1, 0, 0), null, DeliveryType.Self),
        Events = sim.Events,
        Depth = 0,
        Tier = 1
    };

    private static (Sim, EntityRef caster, EntityRef enemy) TwoFighters(float enemyHp = 1000f)
    {
        var sim = new Sim();
        var c = sim.State.AddEntity("caster", Faction.Player, 100, new Vec3(0, 0, 0));
        var e = sim.State.AddEntity("enemy", Faction.Enemy, enemyHp, new Vec3(1, 0, 0));
        return (sim, c.Ref, e.Ref);
    }

    private static float Hit(Sim sim, EntityRef caster, EntityRef target, float amount) =>
        SpellVerbs.Damage(Ctx(sim, caster), target,
            new DamageParams { Element = Element.Arcane, Amount = amount, CanCrit = false }, 1f).Final;

    private static void Apply(Sim sim, EntityRef caster, EntityRef target, StatusId status, float potency) =>
        SpellVerbs.ApplyStatus(Ctx(sim, caster), target,
            new ApplyStatusParams { Status = status, Duration = float.PositiveInfinity, Potency = potency }, 1f);

    [Fact]
    public void Vulnerable_IncreasesDamageTaken()
    {
        var (c, cc, ce) = TwoFighters();
        float baseline = Hit(c, cc, ce, 100f);

        var (t, tc, te) = TwoFighters();
        Apply(t, tc, te, StatusId.Vulnerable, 50f); // +50% DamageIn
        float amplified = Hit(t, tc, te, 100f);

        Assert.Equal(100f, baseline, 3);
        Assert.Equal(150f, amplified, 3);
        Assert.True(amplified > baseline, "Vulnerable must increase damage taken, not just be present.");
    }

    [Fact]
    public void Weaken_ReducesDamageDealt()
    {
        var (c, cc, ce) = TwoFighters();
        float baseline = Hit(c, cc, ce, 100f);

        var (t, tc, te) = TwoFighters();
        Apply(t, tc, tc, StatusId.Weaken, 40f); // weaken the CASTER: -40% DamageOut
        float weakened = Hit(t, tc, te, 100f);

        Assert.Equal(60f, weakened, 3);
        Assert.True(weakened < baseline);
    }

    [Fact]
    public void HasteAndSlow_MoveTheMultiplierStat()
    {
        var (sim, c, e) = TwoFighters();
        Assert.Equal(1.0f, sim.State.GetStat(e, StatId.MoveSpeed), 3); // base

        Apply(sim, c, e, StatusId.Haste, 20f);
        Assert.Equal(1.2f, sim.State.GetStat(e, StatId.MoveSpeed), 3); // +20%

        Apply(sim, c, e, StatusId.Slow, 50f); // different source class ⇒ sums: +20 − 50 = −30
        Assert.Equal(0.7f, sim.State.GetStat(e, StatId.MoveSpeed), 3);
    }

    [Fact]
    public void CritAndEvasion_BranchesAreLive()
    {
        // These branches were dead code when crit/evasion base was always 0 — no RNG was ever drawn.
        var (s1, c1, e1) = TwoFighters();
        s1.State.Get(c1).BaseStats[StatId.CritChance] = 1.0f; // always crit
        var crit = SpellVerbs.Damage(Ctx(s1, c1), e1,
            new DamageParams { Element = Element.Arcane, Amount = 50f, CanCrit = true }, 1f);
        Assert.True(crit.Crit);
        Assert.Equal(100f, crit.Final, 3); // ×2 CritMult

        var (s2, c2, e2) = TwoFighters(100f);
        s2.State.Get(e2).BaseStats[StatId.Evasion] = 1.0f; // always dodge
        var dodged = SpellVerbs.Damage(Ctx(s2, c2), e2,
            new DamageParams { Element = Element.Arcane, Amount = 50f, CanCrit = false }, 1f);
        Assert.True(dodged.Evaded);
        Assert.Equal(100f, s2.State.Get(e2).Hp, 3); // took nothing
    }

    [Fact]
    public void Push_MovesFullDistance_NotCappedAtOne()
    {
        var (sim, c, e) = TwoFighters();
        var res = SpellVerbs.Displace(Ctx(sim, c), e,
            new DisplaceParams { Mode = DisplaceMode.Push, To = DisplaceAnchor.Impact, Distance = 10f });
        Assert.Equal(10f, res.Moved, 3);        // not 1.0
        Assert.Equal(11f, sim.State.Get(e).Position.X, 3); // (1,0,0) pushed away from origin by 10
    }

    [Fact]
    public void StatModPotency_IsBoundedPercent_NotTierBudget()
    {
        // From the fixture: vulnerable share 0.2 ⇒ 20% (tier-independent). Under budget×share it was 17.
        var frost = SpellCompiler.Compile(SpellJson.Parse(Fixtures.Load("frost-shatter.proposal.json")));
        var ifs = (IfStatusTemplate)frost.Clauses[0].Template!;
        var vuln = (ApplyStatusParams)ifs.Also![0].Params;
        Assert.Equal(20f, vuln.Potency, 3);

        // The exact scenario the review flagged: a T5 slow at share 0.2 must NOT resolve to −131%.
        var t5 = SpellCompiler.Compile(new Spell
        {
            Id = new SpellId("t5-slow"),
            Tier = 5,
            Clauses = new[] { new Clause(VerbId.ApplyStatus,
                new ApplyStatusParams { Status = StatusId.Slow, Share = 0.2f, DurationBand = "short" }) }
        });
        float p = ((ApplyStatusParams)t5.Clauses[0].Params).Potency;
        Assert.Equal(20f, p, 3);                       // tier-independent, not TierBudget(5)×0.2 = 131
        Assert.True(p <= BalanceTables.StatModPotencyCap);

        // And the cap actually bites for large shares.
        var huge = SpellCompiler.Compile(new Spell
        {
            Id = new SpellId("huge"),
            Tier = 1,
            Clauses = new[] { new Clause(VerbId.ApplyStatus,
                new ApplyStatusParams { Status = StatusId.Slow, Share = 1.5f, DurationBand = "short" }) }
        });
        Assert.Equal(BalanceTables.StatModPotencyCap, ((ApplyStatusParams)huge.Clauses[0].Params).Potency, 3);
    }
}
