using Xunit;
using Spellcraft;

namespace Spellcraft.Tests;

// The acceptance criterion: the three §8 composition proofs, parsed from their JSON
// descriptors and executed end to end. Assertions are a canonical KIND projection of the
// event stream (robust shape) plus final-state invariants (the precise numbers) — never a
// raw event-string sequence, which rots into regenerate-on-red.
public class GoldenTests
{
    private static string Kind(GameEvent e) => e switch
    {
        CastStarted => "cast",
        CastResolved => "castResolved",
        CastFailed => "castFailed",
        DamageDealt d => d.Evaded ? "evaded" : "damage",
        Healed => "heal",
        ShieldGranted => "shield",
        StatusApplied s => s.Resisted ? "resisted" : "status",
        StatusRemoved => "statusRemoved",
        StatModified => "modifyStat",
        Dispelled => "dispel",
        Displaced => "displace",
        ZoneSpawned => "zoneSpawned",
        ZoneTicked => "zoneTick",
        ZoneExpired => "zoneExpired",
        _ => e.GetType().Name
    };

    private static string[] Kinds(Sim s) => s.Events.Events.Select(Kind).ToArray();
    private static int Count<T>(Sim s) => s.Events.Events.OfType<T>().Count();

    // ── Blink Strike — teleport to my projectile's impact, damage and blind what it hit ──
    [Fact]
    public void BlinkStrike_TeleportsToImpact_DamagesAndBlinds()
    {
        var sim = new Sim(seed: 1);
        var caster = sim.State.AddEntity("caster", Faction.Player, 100, new Vec3(0, 0, 0));
        caster.Resources[ResourceKind.Mana] = 100;
        var enemy = sim.State.AddEntity("enemy", Faction.Enemy, 100, new Vec3(10, 0, 0));

        var spell = SpellJson.Parse(Fixtures.Load("blink-strike.proposal.json"));
        var report = sim.Cast(caster.Ref, spell, target: enemy.Ref);

        Assert.True(report.Success);
        Assert.Equal(new[] { "cast", "castResolved", "displace", "damage", "status" }, Kinds(sim));

        // final-state invariants
        Assert.Equal(enemy.Position, caster.Position);            // teleported onto the impact point
        Assert.Equal(4.0, enemy.Hp, 2);                           // tier2 160 × 0.6 = 96 shadow damage
        Assert.True(sim.State.HasStatus(enemy.Ref, StatusId.Blind));
    }

    // ── Gravity Well — a zone that ticks a pull + arcane damage ──
    [Fact]
    public void GravityWell_ZoneTicksPullAndDamage()
    {
        var sim = new Sim(seed: 2);
        var caster = sim.State.AddEntity("caster", Faction.Player, 100, new Vec3(0, 0, 0));
        caster.Resources[ResourceKind.Mana] = 100;
        var enemy = sim.State.AddEntity("enemy", Faction.Enemy, 300, new Vec3(5, 0, 0));

        var spell = SpellJson.Parse(Fixtures.Load("gravity-well.proposal.json"));
        var report = sim.Cast(caster.Ref, spell, point: new Vec3(0, 0, 0)); // ground-target the origin
        Assert.True(report.Success);

        sim.Tick(1.0f); // 4 quanta-aligned ticks at interval 0.25 (after the 0.8s windup)

        Assert.Equal(1, Count<CastResolved>(sim)); // windup closed once
        Assert.Equal(1, Count<ZoneSpawned>(sim));
        Assert.Equal(4, Count<ZoneTicked>(sim));
        Assert.Equal(4, Count<Displaced>(sim));
        Assert.Equal(4, sim.Events.Events.OfType<DamageDealt>().Count());

        // pulled 1 unit/tick from x=5 toward the origin ⇒ x=1
        Assert.Equal(1.0, enemy.Position.X, 3);
        // 4 ticks × (160 × 0.1 × 1.6 groundAoE) = 4 × 25.6 = 102.4 arcane
        Assert.Equal(197.6, enemy.Hp, 1);
    }

    // ── Frost Shatter — if frozen, amplify and consume; then apply vulnerable ──
    [Fact]
    public void FrostShatter_WhenFrozen_AmplifiesConsumesAndAppliesVulnerable()
    {
        var sim = new Sim(seed: 3);
        var caster = sim.State.AddEntity("caster", Faction.Player, 100, new Vec3(0, 0, 0));
        caster.Resources[ResourceKind.Mana] = 100;
        var enemy = sim.State.AddEntity("enemy", Faction.Enemy, 200, new Vec3(3, 0, 0));
        Freeze(sim, enemy.Ref);

        var spell = SpellJson.Parse(Fixtures.Load("frost-shatter.proposal.json"));
        var report = sim.Cast(caster.Ref, spell, target: enemy.Ref);

        Assert.True(report.Success);
        Assert.Equal(new[] { "cast", "castResolved", "damage", "statusRemoved", "status" }, Kinds(sim));

        // tier1 100 × 0.5 × instant 0.85 = 42.5, amplified ×2.5 (major) = 106.25
        Assert.Equal(200.0 - 106.25, enemy.Hp, 2);
        Assert.False(sim.State.HasStatus(enemy.Ref, StatusId.Freeze));    // consumed
        Assert.True(sim.State.HasStatus(enemy.Ref, StatusId.Vulnerable)); // applied by the `also` clause
    }

    [Fact]
    public void FrostShatter_WhenNotFrozen_DealsBaseDamageOnly()
    {
        var sim = new Sim(seed: 4);
        var caster = sim.State.AddEntity("caster", Faction.Player, 100, new Vec3(0, 0, 0));
        caster.Resources[ResourceKind.Mana] = 100;
        var enemy = sim.State.AddEntity("enemy", Faction.Enemy, 200, new Vec3(3, 0, 0));

        var spell = SpellJson.Parse(Fixtures.Load("frost-shatter.proposal.json"));
        sim.Cast(caster.Ref, spell, target: enemy.Ref);

        Assert.Equal(new[] { "cast", "castResolved", "damage" }, Kinds(sim)); // ifStatus branch not taken
        Assert.Equal(200.0 - 42.5, enemy.Hp, 2);
        Assert.False(sim.State.HasStatus(enemy.Ref, StatusId.Vulnerable));
    }

    private static void Freeze(Sim sim, EntityRef target)
    {
        sim.State.Get(target).Statuses.Add(new StatusInstance
        {
            Id = StatusId.Freeze,
            Category = StatusCategory.HardCc,
            ExpireTime = float.PositiveInfinity,
            Stacks = 1,
            Potency = 0,
            NextTick = float.PositiveInfinity,
            Seq = sim.State.NextSeq()
        });
    }
}
