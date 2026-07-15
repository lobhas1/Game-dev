using System.Linq;
using Xunit;
using Spellcraft;

namespace Spellcraft.Tests;

// Self-exclusion (spec docs/specs/2026-07-15-target-filter.md): an entity's own OFFENSIVE effects
// never touch itself; beneficial self-effects still land. All paths converge on Sim.RunClause, so
// these cover the groundAoE-covers-caster, zone-ticks-owner, offensive-status, offensive-displace,
// and self-shield-blast cases plus every beneficial self-effect that must survive unchanged.
public class TargetFilterTests
{
    // caster at the origin; enemy 3 units away — both inside a medium (radius 6) groundAoE centred
    // on the caster, so pre-fix the caster took its own blast.
    private static (Sim sim, Entity caster, Entity enemy) Arena()
    {
        var sim = new Sim(seed: 1);
        var caster = sim.State.AddEntity("caster", Faction.Player, 100, new Vec3(0, 0, 0));
        caster.Resources[ResourceKind.Mana] = 100;
        var enemy = sim.State.AddEntity("enemy", Faction.Enemy, 500, new Vec3(3, 0, 0));
        return (sim, caster, enemy);
    }

    private static IEnumerable<DamageDealt> Damage(Sim s) => s.Events.Events.OfType<DamageDealt>();

    // ── self-burst damage excludes the caster; the enemy in the same blast is still hit ──
    [Fact]
    public void SelfCenteredBurst_DamageExcludesCaster_EnemyStillHit()
    {
        var (sim, caster, enemy) = Arena();
        var spell = SpellJson.Parse(
            "{\"id\":\"blast\",\"tier\":1,\"delivery\":{\"type\":\"groundAoE\",\"size\":\"medium\"}," +
            "\"clauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":1.0}]}");

        sim.Cast(caster.Ref, spell, point: new Vec3(0, 0, 0)); // centred on the caster

        Assert.DoesNotContain(Damage(sim), d => d.Target == caster.Ref); // no self-hit
        Assert.Equal(100.0, caster.Hp, 3);                               // caster untouched
        Assert.Single(Damage(sim), d => d.Target == enemy.Ref);          // opponent still hit
        Assert.True(enemy.Hp < 500.0);
    }

    // ── a zone never ticks its own owner, even when it affects "all" ──
    [Fact]
    public void Zone_NeverDamagesItsOwner()
    {
        var (sim, caster, enemy) = Arena();
        var spell = SpellJson.Parse(
            "{\"id\":\"field\",\"tier\":1,\"delivery\":{\"type\":\"groundAoE\"}," +
            "\"clauses\":[{\"verb\":\"spawnZone\",\"size\":\"medium\",\"duration\":\"medium\"," +
            "\"tickInterval\":\"fast\",\"affects\":\"all\"," +
            "\"tickClauses\":[{\"verb\":\"damage\",\"element\":\"fire\",\"share\":0.2}]}]}");

        sim.Cast(caster.Ref, spell, point: new Vec3(0, 0, 0)); // zone centred on the owner
        sim.Tick(1.0f);                                        // several ticks

        Assert.NotEmpty(sim.Events.Events.OfType<ZoneTicked>());
        Assert.DoesNotContain(Damage(sim), d => d.Target == caster.Ref); // owner never ticked
        Assert.Equal(100.0, caster.Hp, 3);
        Assert.Contains(Damage(sim), d => d.Target == enemy.Ref);        // enemy inside the field is
        Assert.True(enemy.Hp < 500.0);
    }

    // ── an offensive status (a debuff) in a self-centred burst excludes the caster ──
    [Fact]
    public void SelfCenteredBurst_DebuffStatus_ExcludesCaster_EnemyStillAfflicted()
    {
        var (sim, caster, enemy) = Arena();
        var spell = SpellJson.Parse(
            "{\"id\":\"snare\",\"tier\":1,\"delivery\":{\"type\":\"groundAoE\",\"size\":\"medium\"}," +
            "\"clauses\":[{\"verb\":\"applyStatus\",\"status\":\"root\",\"duration\":\"medium\",\"share\":0.5}]}");

        sim.Cast(caster.Ref, spell, point: new Vec3(0, 0, 0));

        Assert.False(sim.State.HasStatus(caster.Ref, StatusId.Root)); // caster not rooted by its own burst
        Assert.True(sim.State.HasStatus(enemy.Ref, StatusId.Root));   // enemy is
    }

    // ── beneficial self-effects (heal, shield, buff stat, buff status) all still land on the caster ──
    [Fact]
    public void SelfBuffs_StillApplyToCaster()
    {
        var (sim, caster, _) = Arena();
        caster.Hp = 50; // room to heal
        var spell = SpellJson.Parse(
            "{\"id\":\"bulwark\",\"tier\":1,\"delivery\":{\"type\":\"self\"},\"clauses\":[" +
            "{\"verb\":\"heal\",\"share\":0.3}," +
            "{\"verb\":\"shield\",\"share\":0.3,\"duration\":\"medium\"}," +
            "{\"verb\":\"modifyStat\",\"stat\":\"armor\",\"direction\":\"buff\",\"share\":0.2,\"duration\":\"medium\"}," +
            "{\"verb\":\"applyStatus\",\"status\":\"haste\",\"duration\":\"medium\",\"share\":0.2}]}");

        sim.Cast(caster.Ref, spell);

        Assert.Contains(sim.Events.Events.OfType<Healed>(), h => h.Target == caster.Ref && h.Effective > 0);
        Assert.Contains(sim.Events.Events.OfType<ShieldGranted>(), s => s.Target == caster.Ref);
        Assert.Contains(sim.Events.Events.OfType<StatModified>(), m => m.Target == caster.Ref && m.AmountPct > 0);
        Assert.True(sim.State.HasStatus(caster.Ref, StatusId.Haste));
        Assert.True(caster.Hp > 50.0);
        Assert.True(caster.Shields.Sum(s => s.Remaining) > 0);
    }

    // ── a self-dash (displace subject "caster") is beneficial and still moves the caster ──
    [Fact]
    public void SelfDash_StillApplies()
    {
        var (sim, caster, _) = Arena();
        var spell = SpellJson.Parse(
            "{\"id\":\"dash\",\"tier\":1,\"delivery\":{\"type\":\"groundAoE\"}," +
            "\"clauses\":[{\"verb\":\"displace\",\"subject\":\"caster\",\"mode\":\"dash\"," +
            "\"to\":\"point\",\"distance\":\"medium\"}]}");

        sim.Cast(caster.Ref, spell, point: new Vec3(10, 0, 0)); // dash toward the marked point

        Assert.Contains(sim.Events.Events.OfType<Displaced>(), d => d.Subject == caster.Ref && !d.Blocked);
        Assert.True(caster.Position.X > 0.0); // actually moved
    }

    // ── an offensive displace (subject "target") in a self-covering burst excludes the caster ──
    [Fact]
    public void OffensiveDisplace_ExcludesCaster_DisplacesEnemy()
    {
        var (sim, caster, enemy) = Arena();
        var spell = SpellJson.Parse(
            "{\"id\":\"shock\",\"tier\":1,\"delivery\":{\"type\":\"groundAoE\",\"size\":\"medium\"}," +
            "\"clauses\":[{\"verb\":\"displace\",\"subject\":\"target\",\"mode\":\"push\"," +
            "\"to\":\"caster\",\"distance\":\"small\"}]}");

        sim.Cast(caster.Ref, spell, point: new Vec3(0, 0, 0));

        Assert.DoesNotContain(sim.Events.Events.OfType<Displaced>(), d => d.Subject == caster.Ref);
        Assert.Contains(sim.Events.Events.OfType<Displaced>(), d => d.Subject == enemy.Ref);
        Assert.Equal(new Vec3(0, 0, 0), caster.Position); // caster did not push itself
    }

    // ── hearthstone: a self shield is NOT consumed by the caster's own blast ──
    [Fact]
    public void SelfShield_NotConsumedByOwnBlast()
    {
        var (sim, caster, _) = Arena();
        var spell = SpellJson.Parse(
            "{\"id\":\"hearth\",\"tier\":1,\"delivery\":{\"type\":\"groundAoE\",\"size\":\"medium\"},\"clauses\":[" +
            "{\"verb\":\"shield\",\"share\":0.5,\"duration\":\"medium\"}," +
            "{\"verb\":\"damage\",\"element\":\"fire\",\"share\":0.5}]}");

        sim.Cast(caster.Ref, spell, point: new Vec3(0, 0, 0));

        float shield = caster.Shields.Sum(s => s.Remaining);
        Assert.True(shield > 0);                                         // ward granted
        Assert.DoesNotContain(Damage(sim), d => d.Target == caster.Ref); // own blast never landed
        Assert.Equal(0.0, Damage(sim).Where(d => d.Target == caster.Ref).Sum(d => d.AbsorbedByShield), 3);
        Assert.Equal(100.0, caster.Hp, 3);
    }
}
