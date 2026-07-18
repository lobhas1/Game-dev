using System.Linq;
using Xunit;
using Spellcraft;

namespace Spellcraft.Tests;

// Cast-time round (spec docs/specs/2026-07-17-cast-time.md): events are stamped with the sim clock
// at EMISSION, and every successful cast emits CastResolved after its windup — so the recorded
// timeline carries the real CastStarted→CastResolved gap. Instants stay 0-width; failed casts
// never resolve.
public class CastTimeTests
{
    private const float Tol = 1e-3f; // clock accumulates in 0.05f quanta

    private static string CastSpell(string mode, string? castTime) =>
        "{\"id\":\"bolt\",\"tier\":1," +
        $"\"cast\":{{\"mode\":\"{mode}\"" + (castTime is null ? "" : $",\"castTime\":\"{castTime}\"") +
        ",\"cooldown\":\"short\",\"cost\":{\"resource\":\"mana\",\"amount\":\"low\"}}," +
        "\"delivery\":{\"type\":\"targetUnit\",\"range\":\"medium\"}," +
        "\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0}]}";

    private static (Sim sim, Entity caster, Entity enemy) Arena()
    {
        var sim = new Sim(seed: 1);
        var caster = sim.State.AddEntity("caster", Faction.Player, 100, new Vec3(0, 0, 0));
        caster.Resources[ResourceKind.Mana] = 200;
        var enemy = sim.State.AddEntity("enemy", Faction.Enemy, 500, new Vec3(3, 0, 0));
        return (sim, caster, enemy);
    }

    private static float TimeOfFirst<T>(Sim sim) where T : GameEvent
    {
        for (int i = 0; i < sim.Events.Events.Count; i++)
            if (sim.Events.Events[i] is T) return sim.Events.TimeOf(i);
        throw new Xunit.Sdk.XunitException($"no {typeof(T).Name} emitted");
    }

    // ── the windup separates start from resolution by exactly the band's seconds ──
    [Theory]
    [InlineData("quick", 0.4f)]
    [InlineData("normal", 0.8f)]
    [InlineData("long", 1.5f)]
    public void CastMode_GapIsExactlyTheBand(string band, float seconds)
    {
        var (sim, caster, enemy) = Arena();
        var report = sim.Cast(caster.Ref, SpellJson.Parse(CastSpell("cast", band)), target: enemy.Ref);

        Assert.True(report.Success);
        float started = TimeOfFirst<CastStarted>(sim);
        float resolved = TimeOfFirst<CastResolved>(sim);
        float effect = TimeOfFirst<DamageDealt>(sim);
        Assert.Equal(0f, started, Tol);
        Assert.Equal(seconds, resolved - started, Tol);  // the gap IS the band
        Assert.Equal(resolved, effect, Tol);             // effects land at the resolution instant
    }

    // ── instants: resolution shares the start instant (0-width bar), still anchored ──
    [Fact]
    public void Instant_ResolvesAtTheStartInstant()
    {
        var (sim, caster, enemy) = Arena();
        sim.Cast(caster.Ref, SpellJson.Parse(CastSpell("instant", null)), target: enemy.Ref);

        Assert.Single(sim.Events.Events.OfType<CastResolved>());
        Assert.Equal(TimeOfFirst<CastStarted>(sim), TimeOfFirst<CastResolved>(sim), Tol);
    }

    // ── event ORDER: started → resolved → first effect ──
    [Fact]
    public void Order_ResolvedSitsBetweenStartAndEffects()
    {
        var (sim, caster, enemy) = Arena();
        sim.Cast(caster.Ref, SpellJson.Parse(CastSpell("cast", "normal")), target: enemy.Ref);

        var kinds = sim.Events.Events.Select(e => e.GetType().Name).ToArray();
        Assert.Equal(new[] { "CastStarted", "CastResolved", "DamageDealt" }, kinds);
    }

    // ── a failed cast never resolves ──
    [Fact]
    public void FailedCast_EmitsCastFailed_NeverCastResolved()
    {
        var (sim, caster, enemy) = Arena();
        var spell = SpellJson.Parse(CastSpell("cast", "quick"));
        Assert.True(sim.Cast(caster.Ref, spell, target: enemy.Ref).Success);
        Assert.False(sim.Cast(caster.Ref, spell, target: enemy.Ref).Success); // on cooldown

        Assert.Equal(2, sim.Events.Events.OfType<CastStarted>().Count());
        Assert.Single(sim.Events.Events.OfType<CastResolved>());   // only the successful cast
        Assert.Single(sim.Events.Events.OfType<CastFailed>());
    }

    // ── emission-time stamping: DoT ticks carry their true tick times, not a batch stamp ──
    [Fact]
    public void EmissionTimes_DotTicksCarryTrueTickTimes()
    {
        var (sim, caster, enemy) = Arena();
        var ctx = new EffectCtx
        {
            Caster = caster.Ref, SpellId = new SpellId("test"), ClauseIndex = 0,
            Rng = SeededRng.FromSeed(1), State = sim.State, World = sim.World,
            Impact = new ImpactCtx(new Vec3(0, 0, 0), new Vec3(1, 0, 0), null, DeliveryType.Self),
            Events = sim.Events, Depth = 0, Tier = 1
        };
        SpellVerbs.ApplyStatus(ctx, enemy.Ref,
            new ApplyStatusParams { Status = StatusId.Burn, Duration = 2f, Potency = 10f, Stacks = 1 }, 1f);

        sim.Tick(2.0f); // burn ticks every 0.5s

        var tickTimes = Enumerable.Range(0, sim.Events.Events.Count)
            .Where(i => sim.Events.Events[i] is DamageDealt)
            .Select(i => sim.Events.TimeOf(i)).ToList();
        Assert.True(tickTimes.Count >= 3, $"expected >=3 burn ticks, got {tickTimes.Count}");
        Assert.True(tickTimes.Distinct().Count() == tickTimes.Count, "each tick carries its own time");
        Assert.Equal(0.5f, tickTimes[0], Tol); // first tick at its true instant, not the harvest instant
    }
}
