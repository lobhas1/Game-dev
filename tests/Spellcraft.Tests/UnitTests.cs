using Xunit;
using Spellcraft;

namespace Spellcraft.Tests;

public class UnitTests
{
    private static EffectCtx Ctx(Sim sim, EntityRef caster) => new()
    {
        Caster = caster,
        SpellId = new SpellId("test"),
        ClauseIndex = 0,
        Rng = SeededRng.FromSeed(1),
        State = sim.State,
        World = sim.World,
        Impact = new ImpactCtx(new Vec3(0, 0, 0), new Vec3(1, 0, 0), null, DeliveryType.Self),
        Events = sim.Events,
        Depth = 0,
        Tier = 1
    };

    // §2: hard CC does not stack — it refreshes with diminishing returns 100% → 50% → 25% → immune.
    [Fact]
    public void HardCc_DiminishingReturns_FollowTheCurve()
    {
        var sim = new Sim();
        var c = sim.State.AddEntity("c", Faction.Player, 100, new Vec3(0, 0, 0));
        var e = sim.State.AddEntity("e", Faction.Enemy, 100, new Vec3(1, 0, 0));
        var ctx = Ctx(sim, c.Ref);
        var stun = new ApplyStatusParams { Status = StatusId.Stun, Duration = 4f, Potency = 0f, Stacks = 1 };

        var r1 = SpellVerbs.ApplyStatus(ctx, e.Ref, stun, 1f);
        Assert.True(r1.Applied);
        Assert.Equal(4.0, sim.State.FindStatus(e.Ref, StatusId.Stun)!.ExpireTime, 3); // 100%

        SpellVerbs.ApplyStatus(ctx, e.Ref, stun, 1f);
        Assert.Equal(2.0, sim.State.FindStatus(e.Ref, StatusId.Stun)!.ExpireTime, 3);  // 50%

        SpellVerbs.ApplyStatus(ctx, e.Ref, stun, 1f);
        Assert.Equal(1.0, sim.State.FindStatus(e.Ref, StatusId.Stun)!.ExpireTime, 3);  // 25%

        var r4 = SpellVerbs.ApplyStatus(ctx, e.Ref, stun, 1f);
        Assert.False(r4.Applied);
        Assert.True(r4.Resisted);                                                      // immune

        var r5 = SpellVerbs.ApplyStatus(ctx, e.Ref, stun, 1f);
        Assert.True(r5.Resisted);                                                      // still immune (5 s window)
    }

    // §2: shields absorb before health.
    [Fact]
    public void Shields_AbsorbBeforeHealth()
    {
        var sim = new Sim();
        var c = sim.State.AddEntity("c", Faction.Player, 100, new Vec3(0, 0, 0));
        var e = sim.State.AddEntity("e", Faction.Enemy, 100, new Vec3(1, 0, 0));
        sim.State.AddShield(e.Ref, 30f, float.PositiveInfinity, null);

        var res = SpellVerbs.Damage(Ctx(sim, c.Ref), e.Ref,
            new DamageParams { Element = Element.Arcane, Amount = 50f, CanCrit = false }, 1f);

        Assert.Equal(30.0, res.AbsorbedByShield, 3);
        Assert.Equal(20.0, res.Final, 3);
        Assert.Equal(80.0, e.Hp, 3);
        Assert.Empty(sim.State.Get(e.Ref).Shields); // fully consumed
    }

    // DoT tick counts are cadence-independent under the fixed-timestep clock.
    [Fact]
    public void Dot_TicksDeterministically_UnderFixedClock()
    {
        var sim = new Sim();
        var c = sim.State.AddEntity("c", Faction.Player, 100, new Vec3(0, 0, 0));
        var e = sim.State.AddEntity("e", Faction.Enemy, 100, new Vec3(1, 0, 0));

        // burn: 0.5 s interval (catalog), 2 s duration, 10/tick
        SpellVerbs.ApplyStatus(Ctx(sim, c.Ref), e.Ref,
            new ApplyStatusParams { Status = StatusId.Burn, Duration = 2f, Potency = 10f, Stacks = 1 }, 1f);

        sim.Tick(2.0f); // one call...
        int ticksOneCall = sim.Events.Events.OfType<DamageDealt>().Count(d => d.Element == Element.Fire);

        // ...must equal many small calls (cadence independence).
        var sim2 = new Sim();
        var c2 = sim2.State.AddEntity("c", Faction.Player, 100, new Vec3(0, 0, 0));
        var e2 = sim2.State.AddEntity("e", Faction.Enemy, 100, new Vec3(1, 0, 0));
        SpellVerbs.ApplyStatus(Ctx(sim2, c2.Ref), e2.Ref,
            new ApplyStatusParams { Status = StatusId.Burn, Duration = 2f, Potency = 10f, Stacks = 1 }, 1f);
        for (int i = 0; i < 200; i++) sim2.Tick(0.01f); // 200 × 10 ms = 2 s
        int ticksManyCalls = sim2.Events.Events.OfType<DamageDealt>().Count(d => d.Element == Element.Fire);

        Assert.Equal(4, ticksOneCall);            // 2 s / 0.5 s
        Assert.Equal(4, ticksManyCalls);          // same regardless of dt cadence
        Assert.Equal(60.0, e.Hp, 1);              // 4 × 10 = 40 damage
        Assert.False(sim.State.HasStatus(e.Ref, StatusId.Burn)); // expired at t = 2
    }

    // Deserialization is where the closed vocabulary becomes law.
    [Fact]
    public void StrictParse_RejectsUnknownVocabularyAndFields()
    {
        Assert.Throws<SpellParseException>(() => SpellJson.Parse(
            """{ "id": "x", "tier": 1, "clauses": [ { "verb": "floop" } ] }"""));            // unknown verb

        Assert.Throws<SpellParseException>(() => SpellJson.Parse(
            """{ "id": "x", "tier": 1, "clauses": [ { "verb": "damage", "element": "shadow", "share": 0.5, "bogus": 1 } ] }""")); // unknown field

        Assert.Throws<SpellParseException>(() => SpellJson.Parse(
            """{ "id": "x", "tier": 1, "clauses": [ { "verb": "applyStatus", "status": "kryptonite", "share": 0.2 } ] }""")); // unknown status

        Assert.Throws<NotImplementedInSliceException>(() => SpellJson.Parse(
            """{ "id": "x", "tier": 1, "clauses": [ { "verb": "summon" } ] }"""));            // valid vocab, unbuilt
    }

    // Round-trip sanity: every §8 fixture parses and compiles without error.
    [Theory]
    [InlineData("blink-strike.proposal.json")]
    [InlineData("gravity-well.proposal.json")]
    [InlineData("frost-shatter.proposal.json")]
    public void Fixtures_ParseAndCompile(string name)
    {
        var spell = SpellJson.Parse(Fixtures.Load(name));
        var compiled = SpellCompiler.Compile(spell);
        Assert.True(compiled.Compiled);
        Assert.NotEmpty(compiled.Clauses);
    }
}
