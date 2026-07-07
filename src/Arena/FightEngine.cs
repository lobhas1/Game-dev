using Spellcraft;

namespace Arena;

public sealed record FightResult(
    string KitA, string KitB, long Seed,
    string EndReason, string Winner, float DurationSeconds,
    int CastsA, int CastsB, int DistinctVerbs, int StatusesApplied, int LeadChanges,
    float DamageToA, float DamageToB,
    IReadOnlyList<string> Projection);

/// <summary>One headless duel between two kits, seeded and deterministic. Fixed arena constants; a
/// dumb rotation policy identical for both fighters (see the spec §3.1).</summary>
public static class FightEngine
{
    public const float StartHp = 300f;
    public const float StartMana = 250f;
    public const float SpawnDistance = 8f;
    public const float DecisionQuantum = 0.5f;
    public const float TimeoutSeconds = 90f;
    private const int MaxIterations = 1000; // anti-infinite-loop backstop (>=180 real quanta to timeout)

    private sealed class Side
    {
        public required Kit Kit;
        public EntityRef Self;
        public EntityRef Enemy;
        public int Pointer;
    }

    public static FightResult Run(Kit a, Kit b, long seed)
    {
        var sim = new Sim(seed);
        var eA = sim.State.AddEntity(a.Name, Faction.Player, StartHp, new Vec3(0, 0, 0));
        eA.Resources[ResourceKind.Mana] = StartMana;
        var eB = sim.State.AddEntity(b.Name, Faction.Enemy, StartHp, new Vec3(SpawnDistance, 0, 0));
        eB.Resources[ResourceKind.Mana] = StartMana;

        var sideA = new Side { Kit = a, Self = eA.Ref, Enemy = eB.Ref };
        var sideB = new Side { Kit = b, Self = eB.Ref, Enemy = eA.Ref };

        int castsA = 0, castsB = 0, leadChanges = 0, lastSign = 0, lastSecond = -1;
        string endReason = "timeout";

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            if (sim.State.Now >= TimeoutSeconds) { endReason = "timeout"; break; }
            if (sim.State.IsDead(eA.Ref) || sim.State.IsDead(eB.Ref)) { endReason = "death"; break; }
            if (!CanAffordAny(sim, sideA) && !CanAffordAny(sim, sideB)) { endReason = "oom"; break; }

            if (TryCast(sim, sideA)) castsA++;
            if (sim.State.IsDead(eB.Ref)) { endReason = "death"; break; }
            if (TryCast(sim, sideB)) castsB++;
            if (sim.State.IsDead(eA.Ref)) { endReason = "death"; break; }

            sim.Tick(DecisionQuantum);

            int sec = (int)MathF.Floor(sim.State.Now);
            if (sec > lastSecond)
            {
                lastSecond = sec;
                float diff = sim.State.Get(eA.Ref).Hp - sim.State.Get(eB.Ref).Hp;
                int sign = diff > 0 ? 1 : diff < 0 ? -1 : 0;
                if (sign != 0)
                {
                    if (lastSign != 0 && sign != lastSign) leadChanges++;
                    lastSign = sign;
                }
            }

            if (sim.State.IsDead(eA.Ref) || sim.State.IsDead(eB.Ref)) { endReason = "death"; break; }
        }

        float hpA = sim.State.Get(eA.Ref).Hp, hpB = sim.State.Get(eB.Ref).Hp;
        string winner = hpA > hpB ? a.Name : hpB > hpA ? b.Name : "draw";

        var events = sim.Events.Events;
        int distinctVerbs = events.Select(VerbOf).Where(v => v is not null).Distinct().Count();
        int statuses = events.OfType<StatusApplied>().Count(s => !s.Resisted);
        float dmgToA = events.OfType<DamageDealt>().Where(d => d.Target == eA.Ref && !d.Evaded).Sum(d => d.Amount);
        float dmgToB = events.OfType<DamageDealt>().Where(d => d.Target == eB.Ref && !d.Evaded).Sum(d => d.Amount);

        return new FightResult(a.Name, b.Name, seed, endReason, winner, sim.State.Now,
            castsA, castsB, distinctVerbs, statuses, leadChanges, dmgToA, dmgToB,
            sim.Events.Projection());
    }

    private static bool CanAffordAny(Sim sim, Side side)
    {
        foreach (var sp in side.Kit.Spells)
            if (Affordable(sim, side.Self, sp)) return true;
        return false;
    }

    private static bool Affordable(Sim sim, EntityRef who, Spell sp)
    {
        var cost = sp.Cast.Cost;
        return cost.Resource == ResourceKind.Health
            ? sim.State.Get(who).Hp > cost.Amount
            : sim.State.GetResource(who, cost.Resource) >= cost.Amount;
    }

    // Rotate from the pointer to the first castable (affordable + off cooldown) spell; else wait.
    private static bool TryCast(Sim sim, Side side)
    {
        int n = side.Kit.Spells.Count;
        for (int off = 0; off < n; off++)
        {
            int idx = (side.Pointer + off) % n;
            var sp = side.Kit.Spells[idx];
            if (!Affordable(sim, side.Self, sp)) continue;
            if (sim.State.CooldownRemaining(side.Self, sp.Id) > 0f) continue;

            EntityRef target = default;
            Vec3? point = null;
            switch (sp.Delivery.Type)
            {
                case DeliveryType.Self: target = side.Self; break;
                case DeliveryType.GroundAoE: point = sim.State.Get(side.Enemy).Position; break;
                default: target = side.Enemy; break; // targetUnit / projectile → enemy
            }

            var report = sim.Cast(side.Self, sp, target, point);
            side.Pointer = (idx + 1) % n;
            return report.Success;
        }
        return false; // nothing castable — wait one quantum
    }

    // Which verb an event evidences firing (mirrors GoldenTests.Kind, keyed to verb names).
    private static string? VerbOf(GameEvent e) => e switch
    {
        DamageDealt => "damage",
        Healed => "heal",
        ShieldGranted => "shield",
        StatusApplied => "applyStatus",
        Displaced => "displace",
        ZoneSpawned => "spawnZone",
        StatModified => "modifyStat",
        Dispelled => "dispel",
        _ => null
    };
}
