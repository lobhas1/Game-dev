// CombatModel.cs — the NOUNS of the headless combat sim.
//
// This file is data only: value types, the closed-vocabulary enums, the rules-state
// model (WorldState), the shrunk engine seam (IWorldApi), the status catalog, the
// balance authority (BalanceTables), the serializable spell descriptor, and events.
//
// There are NO game verbs here. Behaviour (verbs, pipeline, resolver, clock) lives in
// CombatSim.cs. WorldState carries the small state-integrity primitives it owns (apply
// health loss, add a shield); orchestration of those primitives is a verb's job, not the
// model's. "Magnitude" and "Duration" in verbs.md are concrete numbers — here, float.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Spellcraft;

// ─────────────────────────────────────────────────────────────────────────────
// Value types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Gameplay logic runs on the XZ plane; Y exists for launch/terrain.</summary>
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, float k) => new(a.X * k, a.Y * k, a.Z * k);

    /// <summary>Planar distance; Y is ignored for gameplay ranges.</summary>
    public float DistanceXZ(Vec3 o)
    {
        float dx = X - o.X, dz = Z - o.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Planar unit direction (Y forced to 0). Zero-length returns +X.</summary>
    public Vec3 DirectionXZ(Vec3 to)
    {
        float dx = to.X - X, dz = to.Z - Z;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        return len < 1e-6f ? new Vec3(1, 0, 0) : new Vec3(dx / len, 0, dz / len);
    }

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##})", X, Y, Z);
}

/// <summary>Opaque, stable handle into world state. 0 = none.</summary>
public readonly record struct EntityRef(int Value)
{
    public static readonly EntityRef None = new(0);
    public bool IsValid => Value != 0;
    public override string ToString() => $"E{Value}";
}

public readonly record struct SpellId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ZoneId(int Value)
{
    public static readonly ZoneId None = new(0);
    public override string ToString() => $"Z{Value}";
}
public readonly record struct ShieldId(int Value);
public readonly record struct ModifierId(int Value);

// ─────────────────────────────────────────────────────────────────────────────
// Closed-list vocabulary (enums). The oracle may reference nothing outside these.
// ─────────────────────────────────────────────────────────────────────────────

public enum Element { Fire, Water, Earth, Air, Light, Shadow, Nature, Arcane }

public enum StatusId
{
    Burn, Poison, Bleed, Chill, Freeze, Shock, Stun, Root, Slow, Silence,
    Disarm, Blind, Fear, Weaken, Vulnerable, Haste, Regen, Stealth, Unstoppable, Invulnerable
}

public enum StatusCategory { Dot, StatMod, HardCc, SoftCc, Proc, Hot, Utility }

public enum StackRule { None, Refresh, Stacks, Strongest }

public enum StatKind { MoveSpeed, CastSpeed, DamageOut, DamageIn, Armor, Resist, CritChance, CritMult, Evasion, Lifesteal }

/// <summary>Stat identifier; Resist is parameterised by Element (the spec's <c>resist(element)</c>).</summary>
public readonly record struct StatId(StatKind Kind, Element? Element = null)
{
    public static StatId MoveSpeed => new(StatKind.MoveSpeed);
    public static StatId CastSpeed => new(StatKind.CastSpeed);
    public static StatId DamageOut => new(StatKind.DamageOut);
    public static StatId DamageIn => new(StatKind.DamageIn);
    public static StatId Armor => new(StatKind.Armor);
    public static StatId CritChance => new(StatKind.CritChance);
    public static StatId CritMult => new(StatKind.CritMult);
    public static StatId Evasion => new(StatKind.Evasion);
    public static StatId Lifesteal => new(StatKind.Lifesteal);
    public static StatId Resist(Element e) => new(StatKind.Resist, e);

    public override string ToString() => Kind == StatKind.Resist ? $"resist({Element})" : Kind.ToString();
}

public enum ResourceKind { Mana, Stamina, Health }

public enum CastMode { Instant, Cast, Channel, Charge, Passive, Reactive, Aura }

public enum DeliveryType { Self, Melee, TargetUnit, Projectile, GroundAoE, Nova, Beam, Chain, AtPoint }

public enum ZoneShape { Circle, Cone, Line, Ring }

public enum Affects { Enemies, Allies, All }

public enum DisplaceMode { Teleport, Dash, Push, Pull, Launch, Swap }

public enum DisplaceAnchor { Point, Impact, Caster, BehindTarget }

public enum DispelCategory { Buff, Debuff, Dot, All }

public enum DispelOrder { Newest, Oldest, Strongest }

public enum Faction { Player, Enemy, Neutral }

/// <summary>The full closed verb vocabulary (all 29). This slice IMPLEMENTS the first eight;
/// the rest parse as valid vocabulary but the dispatcher rejects them as not-yet-built.</summary>
public enum VerbId
{
    // Tier 0 — implemented in this slice
    Damage, Heal, Shield, ApplyStatus, Dispel, ModifyStat, Displace, SpawnZone,
    // Tier 1+ — vocabulary only (dispatch throws NotImplementedInSlice)
    Summon, Mark, Detonate, DrainResource, CreateBarrier, Reveal, Taunt, Reflect, ModifyAbility,
    Tether, Transform, LifeLink, Absorb, Charm, Banish, CommandMinions,
    Resurrect, Terraform, TimeDilate, Possess, CastSpell
}

public enum TemplateKind { OnHit, OnKill, OnCrit, IfStatus, Delayed, Repeating, OnExpire, HpThreshold }

// ─────────────────────────────────────────────────────────────────────────────
// Exceptions — deserialization is where the closed vocabulary becomes law.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Unknown verb/status/field, malformed band, or a missing required field.</summary>
public sealed class SpellParseException : Exception
{
    public SpellParseException(string message) : base(message) { }
}

/// <summary>A recognised-but-unbuilt symbol (a widen-scope verb or template). Distinct from
/// SpellParseException: the vocabulary is valid, the engine just hasn't grown this yet.</summary>
public sealed class NotImplementedInSliceException : Exception
{
    public NotImplementedInSliceException(string message) : base(message) { }
}

// ─────────────────────────────────────────────────────────────────────────────
// Descriptor model (serializable). A spell is DATA: proposal-stage fields (bands +
// shares) plus resolved-stage numbers the compiler fills. See CombatSim.SpellCompiler.
//
// Convention: *Band / Share = proposal input; the plainly-named numeric = resolved output.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record CostSpec
{
    public ResourceKind Resource { get; init; } = ResourceKind.Mana;
    public string AmountBand { get; init; } = "trivial";
    public float Amount { get; init; } // resolved
}

public sealed record CastSpec
{
    public CastMode Mode { get; init; } = CastMode.Instant;
    public string? CastTimeBand { get; init; }
    public string CooldownBand { get; init; } = "short";
    public CostSpec Cost { get; init; } = new();
    public bool Interruptible { get; init; }
    public bool MoveWhileCasting { get; init; }
    public float CastTime { get; init; }  // resolved seconds
    public float Cooldown { get; init; }  // resolved seconds
}

public sealed record DeliverySpec
{
    public DeliveryType Type { get; init; } = DeliveryType.Self;
    public ZoneShape? Shape { get; init; }
    public string? SizeBand { get; init; }    // groundAoE radius / nova radius
    public string? RangeBand { get; init; }   // targetUnit / atPoint range
    public string? SpeedBand { get; init; }   // projectile speed
    public bool Telegraph { get; init; }
    public float Size { get; init; }   // resolved
    public float Range { get; init; }  // resolved
    public float Speed { get; init; }  // resolved
}

/// <summary>Base for all verb parameter records. One concrete record per implemented verb.</summary>
public abstract record VerbParams;

public sealed record DamageParams : VerbParams
{
    public Element Element { get; init; } = Element.Arcane;
    public float Share { get; init; }
    public bool CanCrit { get; init; } = true;
    public string[]? Tags { get; init; }
    public float LeechPct { get; init; }
    public float Amount { get; init; } // resolved
}

public sealed record HealParams : VerbParams
{
    public float Share { get; init; }
    public bool CanOverhealToShield { get; init; }
    public float Amount { get; init; } // resolved
}

public sealed record ShieldParams : VerbParams
{
    public float Share { get; init; }
    public string DurationBand { get; init; } = "short";
    public Element? Element { get; init; }
    public float Amount { get; init; }   // resolved
    public float Duration { get; init; } // resolved seconds
}

public sealed record ApplyStatusParams : VerbParams
{
    public StatusId Status { get; init; }
    public string DurationBand { get; init; } = "short";
    public int Stacks { get; init; } = 1;
    public float Share { get; init; }      // → potency
    public float Duration { get; init; }   // resolved seconds
    public float Potency { get; init; }    // resolved
}

public sealed record DispelParams : VerbParams
{
    public DispelCategory Category { get; init; } = DispelCategory.Debuff;
    public Element? Element { get; init; }
    public int Count { get; init; } = 1;
    public DispelOrder Order { get; init; } = DispelOrder.Newest;
}

public sealed record ModifyStatParams : VerbParams
{
    public StatId Stat { get; init; }
    public float Share { get; init; }             // magnitude of the percent change
    public string Direction { get; init; } = "buff"; // "buff" (+) or "debuff" (-)
    public string DurationBand { get; init; } = "short";
    public float AmountPct { get; init; }  // resolved, signed
    public float Duration { get; init; }   // resolved seconds
}

public sealed record DisplaceParams : VerbParams
{
    public string Subject { get; init; } = "target"; // "caster" | "target"
    public DisplaceMode Mode { get; init; } = DisplaceMode.Push;
    public DisplaceAnchor? To { get; init; }
    public string? DistanceBand { get; init; }
    public string? SpeedBand { get; init; }
    public float Distance { get; init; } // resolved
    public float Speed { get; init; }    // resolved
}

public sealed record SpawnZoneParams : VerbParams
{
    public ZoneShape Shape { get; init; } = ZoneShape.Circle;
    public string SizeBand { get; init; } = "medium";
    public string DurationBand { get; init; } = "medium";
    public string TickIntervalBand { get; init; } = "medium";
    public Affects Affects { get; init; } = Affects.Enemies;
    public Clause[] TickClauses { get; init; } = Array.Empty<Clause>();
    public Clause[]? OnEnterClauses { get; init; }
    public Clause[]? OnExitClauses { get; init; }
    public float Size { get; init; }         // resolved
    public float Duration { get; init; }     // resolved seconds
    public float TickInterval { get; init; } // resolved seconds
}

/// <summary>Base for §7 templates. All eight parse-validate; execution is implemented for
/// OnHit and IfStatus, stubbed for the rest (see CombatSim.TriggerPhase).</summary>
public abstract record TemplateSpec
{
    public abstract TemplateKind Kind { get; }
}

public sealed record OnHitTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.OnHit;
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
}

public sealed record OnKillTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.OnKill;
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
}

public sealed record OnCritTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.OnCrit;
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
}

public sealed record IfStatusTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.IfStatus;
    public StatusId Status { get; init; }
    public bool Consume { get; init; }
    public string AmplifyBand { get; init; } = "minor";
    public float Amplify { get; init; } // resolved multiplier
    public Clause[]? Also { get; init; }
}

public sealed record DelayedTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.Delayed;
    public string DelayBand { get; init; } = "short";
    public float Delay { get; init; } // resolved seconds
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
}

public sealed record RepeatingTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.Repeating;
    public int Times { get; init; } = 1;
    public string IntervalBand { get; init; } = "medium";
    public float Interval { get; init; } // resolved seconds
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
}

public sealed record OnExpireTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.OnExpire;
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
}

public sealed record HpThresholdTemplate : TemplateSpec
{
    public override TemplateKind Kind => TemplateKind.HpThreshold;
    public float Pct { get; init; }
    public string Side { get; init; } = "below"; // "above" | "below"
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
}

/// <summary>One sentence of a spell: a verb, its typed params, and an optional trigger template.</summary>
public sealed record Clause(VerbId Verb, VerbParams Params, TemplateSpec? Template = null);

/// <summary>The full serializable spell descriptor. Root carries id + tier so shares are priced.</summary>
public sealed record Spell
{
    public SpellId Id { get; init; }
    public int Tier { get; init; } = 1;
    public CastSpec Cast { get; init; } = new();
    public DeliverySpec Delivery { get; init; } = new();
    public Clause[] Clauses { get; init; } = Array.Empty<Clause>();
    /// <summary>Set true by SpellCompiler once every band/share has a resolved number.</summary>
    public bool Compiled { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Status catalog — statuses are DATA referencing shared machinery (DoT ticker, stat
// modifier, CC controller). Adding one is cheap; a new category is not.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record StatusDef(
    StatusId Id,
    StatusCategory Category,
    StackRule Stacking,
    int MaxStacks,
    bool AppliesDr,             // hard/soft CC that triggers diminishing returns
    Element? DotElement,        // for Dot; null = untyped (no elemental resist)
    float TickInterval);        // for Dot/Hot, seconds; 0 otherwise

public static class StatusCatalog
{
    private static readonly Dictionary<StatusId, StatusDef> Defs = new()
    {
        [StatusId.Burn]        = new(StatusId.Burn, StatusCategory.Dot, StackRule.Stacks, 5, false, Element.Fire, 0.5f),
        [StatusId.Poison]      = new(StatusId.Poison, StatusCategory.Dot, StackRule.Stacks, 10, false, Element.Nature, 0.5f),
        [StatusId.Bleed]       = new(StatusId.Bleed, StatusCategory.Dot, StackRule.Stacks, 5, false, null, 0.5f),
        [StatusId.Chill]       = new(StatusId.Chill, StatusCategory.StatMod, StackRule.Strongest, 1, false, null, 0f),
        [StatusId.Freeze]      = new(StatusId.Freeze, StatusCategory.HardCc, StackRule.Refresh, 1, true, null, 0f),
        [StatusId.Shock]       = new(StatusId.Shock, StatusCategory.Proc, StackRule.None, 1, false, null, 0f),
        [StatusId.Stun]        = new(StatusId.Stun, StatusCategory.HardCc, StackRule.Refresh, 1, true, null, 0f),
        [StatusId.Root]        = new(StatusId.Root, StatusCategory.SoftCc, StackRule.Refresh, 1, true, null, 0f),
        [StatusId.Slow]        = new(StatusId.Slow, StatusCategory.StatMod, StackRule.Strongest, 1, false, null, 0f),
        [StatusId.Silence]     = new(StatusId.Silence, StatusCategory.SoftCc, StackRule.Refresh, 1, true, null, 0f),
        [StatusId.Disarm]      = new(StatusId.Disarm, StatusCategory.SoftCc, StackRule.Refresh, 1, true, null, 0f),
        [StatusId.Blind]       = new(StatusId.Blind, StatusCategory.StatMod, StackRule.Refresh, 1, false, null, 0f),
        [StatusId.Fear]        = new(StatusId.Fear, StatusCategory.HardCc, StackRule.Refresh, 1, true, null, 0f),
        [StatusId.Weaken]      = new(StatusId.Weaken, StatusCategory.StatMod, StackRule.Strongest, 1, false, null, 0f),
        [StatusId.Vulnerable]  = new(StatusId.Vulnerable, StatusCategory.StatMod, StackRule.Strongest, 1, false, null, 0f),
        [StatusId.Haste]       = new(StatusId.Haste, StatusCategory.StatMod, StackRule.Strongest, 1, false, null, 0f),
        [StatusId.Regen]       = new(StatusId.Regen, StatusCategory.Hot, StackRule.Stacks, 3, false, null, 0.5f),
        [StatusId.Stealth]     = new(StatusId.Stealth, StatusCategory.Utility, StackRule.None, 1, false, null, 0f),
        [StatusId.Unstoppable] = new(StatusId.Unstoppable, StatusCategory.Utility, StackRule.None, 1, false, null, 0f),
        [StatusId.Invulnerable]= new(StatusId.Invulnerable, StatusCategory.Utility, StackRule.None, 1, false, null, 0f),
    };

    public static StatusDef Get(StatusId id) => Defs[id];

    /// <summary>Buffs: the beneficial statuses a "dispel buff" would strip.</summary>
    public static bool IsBuff(StatusId id) => id is
        StatusId.Haste or StatusId.Regen or StatusId.Unstoppable or StatusId.Invulnerable or StatusId.Stealth;

    /// <summary>Debuffs: everything that isn't a buff (DoTs, CC, and harmful stat mods).</summary>
    public static bool IsDebuff(StatusId id) => !IsBuff(id);
}

// ─────────────────────────────────────────────────────────────────────────────
// Stat-units authority — the ONE named place the currency of every StatKind is declared, so a
// number never crosses the GetStat → consumer boundary with an undeclared unit. Additive stats
// are all in percentage POINTS; multiplier stats scale a non-zero baseline. `WorldState.GetStat`
// and `GetStatFraction` both cite this table; no consumer re-derives a conversion.
// ─────────────────────────────────────────────────────────────────────────────

public enum StatUnit
{
    Multiplier,     // GetStat = base × (1 + Σpts/100); base ≈ 1 (speeds) or 2 (crit mult)
    Points,         // additive percentage points; consumed as 1 + pts/100, or a DR rating (armor)
    Probability,    // additive points, consumed as a chance:  clamp(pts/100, 0, 1)
    ResistFraction  // additive points, consumed as a resist:  clamp(pts/100, 0, 0.95)
}

public static class StatUnits
{
    private static readonly Dictionary<StatKind, StatUnit> Table = new()
    {
        [StatKind.MoveSpeed]  = StatUnit.Multiplier,
        [StatKind.CastSpeed]  = StatUnit.Multiplier,
        [StatKind.CritMult]   = StatUnit.Multiplier,
        [StatKind.DamageIn]   = StatUnit.Points,
        [StatKind.DamageOut]  = StatUnit.Points,
        [StatKind.Armor]      = StatUnit.Points,
        [StatKind.Lifesteal]  = StatUnit.Points,
        [StatKind.CritChance] = StatUnit.Probability,
        [StatKind.Evasion]    = StatUnit.Probability,
        [StatKind.Resist]     = StatUnit.ResistFraction,
    };

    public static StatUnit Of(StatKind k) => Table[k];
    public static bool IsMultiplier(StatKind k) => Table[k] == StatUnit.Multiplier;
}

// ─────────────────────────────────────────────────────────────────────────────
// Balance authority — the ONE named place every band and multiplier lives.
// Magnitude = tierBudget(tier) × share × deliveryMult × castMult.
// Durations / sizes / rates resolve from their own bands (not budget-scaled).
// ─────────────────────────────────────────────────────────────────────────────

public static class BalanceTables
{
    // tierBudget(t) = 100 × 1.6^(t-1)
    public static float TierBudget(int tier) => 100f * MathF.Pow(1.6f, Math.Max(0, tier - 1));

    /// <summary>Cap on stat-modifier potency (percentage points). Stat-mod potency does NOT ride the
    /// exponential tier budget (that currency is for damage/heal magnitudes) — a T5 slow at share 0.2
    /// would otherwise resolve to −131% move speed. Percentages get their own bounded curve.</summary>
    public const float StatModPotencyCap = 90f;

    private static readonly Dictionary<string, float> CastTimeBands = new()
        { ["instant"] = 0f, ["quick"] = 0.4f, ["normal"] = 0.8f, ["long"] = 1.5f };

    private static readonly Dictionary<string, float> DurationBands = new()
        { ["instant"] = 0f, ["short"] = 2f, ["medium"] = 5f, ["long"] = 10f };

    private static readonly Dictionary<string, float> CooldownBands = new()
        { ["short"] = 5f, ["medium"] = 9f, ["long"] = 15f };

    private static readonly Dictionary<string, float> SizeBands = new()
        { ["tiny"] = 1f, ["small"] = 3f, ["medium"] = 6f, ["large"] = 10f };

    private static readonly Dictionary<string, float> SpeedBands = new()
        { ["slow"] = 10f, ["fast"] = 25f };

    private static readonly Dictionary<string, float> IntervalBands = new()
        { ["fast"] = 0.25f, ["medium"] = 0.5f, ["slow"] = 1f };

    private static readonly Dictionary<string, float> CostBands = new()
        { ["trivial"] = 5f, ["low"] = 15f, ["moderate"] = 30f, ["high"] = 50f };

    private static readonly Dictionary<string, float> MultiplierBands = new()
        { ["minor"] = 1.5f, ["major"] = 2.5f, ["extreme"] = 4f };

    public static float CastTime(string band) => Lookup(CastTimeBands, band, "castTime");
    public static float Duration(string band) => Lookup(DurationBands, band, "duration");
    public static float Cooldown(string band) => Lookup(CooldownBands, band, "cooldown");
    public static float Size(string band) => Lookup(SizeBands, band, "size");
    public static float Speed(string band) => Lookup(SpeedBands, band, "speed");
    public static float Interval(string band) => Lookup(IntervalBands, band, "interval");
    public static float Cost(string band) => Lookup(CostBands, band, "cost");
    public static float Multiplier(string band) => Lookup(MultiplierBands, band, "multiplier");

    // §4 delivery multipliers (pierce/homing add-ons are widen-scope)
    public static float DeliveryMultiplier(DeliveryType d) => d switch
    {
        DeliveryType.Self => 0.8f,
        DeliveryType.Melee => 0.9f,
        DeliveryType.TargetUnit => 1.0f,
        DeliveryType.Projectile => 1.0f,
        DeliveryType.GroundAoE => 1.6f,
        DeliveryType.Nova => 1.5f,
        DeliveryType.Beam => 1.4f,
        DeliveryType.Chain => 1.4f,
        DeliveryType.AtPoint => 1.0f,
        _ => 1.0f
    };

    // §3 cast multipliers (slice supports instant/cast; others listed for completeness)
    public static float CastMultiplier(CastMode m) => m switch
    {
        CastMode.Instant => 0.85f,
        CastMode.Cast => 1.0f,
        CastMode.Channel => 1.3f,
        CastMode.Charge => 1.2f,
        CastMode.Reactive => 0.9f,
        _ => 1.0f
    };

    private static float Lookup(Dictionary<string, float> table, string band, string kind)
    {
        if (band is null || !table.TryGetValue(band, out var v))
            throw new SpellParseException($"Unknown {kind} band '{band}'. Allowed: {string.Join(", ", table.Keys)}.");
        return v;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Events — every verb emits. VFX/audio/log/AI (and the golden tests) subscribe.
// Canonical() is the stable projection the goldens assert on.
// ─────────────────────────────────────────────────────────────────────────────

public abstract record GameEvent
{
    public abstract string Canonical();
    protected static string N(float f) => f.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed record DamageDealt(EntityRef Target, float Amount, Element Element, bool Crit, bool Killed, float AbsorbedByShield, bool Evaded) : GameEvent
{
    public override string Canonical() => Evaded
        ? $"damage {Target} evaded"
        : $"damage {Target} amt={N(Amount)} {Element} crit={Crit} killed={Killed} absorbed={N(AbsorbedByShield)}";
}

public sealed record Healed(EntityRef Target, float Effective, float Overheal) : GameEvent
{
    public override string Canonical() => $"heal {Target} eff={N(Effective)} over={N(Overheal)}";
}

public sealed record ShieldGranted(EntityRef Target, float Amount, float Duration) : GameEvent
{
    public override string Canonical() => $"shield {Target} amt={N(Amount)} dur={N(Duration)}";
}

public sealed record StatusApplied(EntityRef Target, StatusId Status, float Duration, int StacksNow, bool Resisted, bool Refreshed) : GameEvent
{
    public override string Canonical() => Resisted
        ? $"status {Target} {Status} resisted"
        : $"status {Target} {Status} dur={N(Duration)} stacks={StacksNow} refreshed={Refreshed}";
}

public sealed record StatusRemoved(EntityRef Target, StatusId Status) : GameEvent
{
    public override string Canonical() => $"statusRemoved {Target} {Status}";
}

public sealed record StatModified(EntityRef Target, StatId Stat, float AmountPct, float Duration) : GameEvent
{
    public override string Canonical() => $"modifyStat {Target} {Stat} pct={N(AmountPct)} dur={N(Duration)}";
}

public sealed record Dispelled(EntityRef Target, int Count) : GameEvent
{
    public override string Canonical() => $"dispel {Target} count={Count}";
}

public sealed record Displaced(EntityRef Subject, DisplaceMode Mode, Vec3 From, Vec3 To, bool Blocked) : GameEvent
{
    public override string Canonical() => $"displace {Subject} {Mode} to={To} blocked={Blocked}";
}

public sealed record ZoneSpawned(ZoneId Id, Vec3 Center, ZoneShape Shape, float Size, float Duration) : GameEvent
{
    public override string Canonical() => $"zoneSpawned {Id} {Shape} r={N(Size)} dur={N(Duration)}";
}

public sealed record ZoneTicked(ZoneId Id, int AffectedCount) : GameEvent
{
    public override string Canonical() => $"zoneTick {Id} affected={AffectedCount}";
}

public sealed record ZoneExpired(ZoneId Id) : GameEvent
{
    public override string Canonical() => $"zoneExpired {Id}";
}

public sealed record CastStarted(EntityRef Caster, SpellId Spell) : GameEvent
{
    public override string Canonical() => $"cast {Caster} {Spell}";
}

public sealed record CastFailed(EntityRef Caster, SpellId Spell, string Reason) : GameEvent
{
    public override string Canonical() => $"castFailed {Caster} {Spell} {Reason}";
}

/// <summary>Collects emitted events in order. "Event flush" (§2 step 6) is modelled as
/// consumers reading the log after a consistent state is reached.</summary>
public sealed class EventBus
{
    private readonly List<GameEvent> _events = new();
    public IReadOnlyList<GameEvent> Events => _events;
    public void Emit(GameEvent e) => _events.Add(e);
    public IReadOnlyList<string> Projection() => _events.Select(e => e.Canonical()).ToList();
}

// ─────────────────────────────────────────────────────────────────────────────
// Deterministic RNG with forked substreams (§ amendment 7). Seeded per cast; a stable
// hash — NEVER string.GetHashCode, which is per-process randomised. Forking does not
// advance the parent stream, so adding a new roll type never shifts existing draws.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SeededRng
{
    private ulong _state;

    private SeededRng(ulong state) => _state = state == 0 ? 0x9E3779B97F4A7C15UL : state;

    public static SeededRng FromSeed(long seed) => new(Mix((ulong)seed));

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private ulong NextU64()
    {
        // xorshift64* — deterministic, adequate for gameplay (not cryptographic).
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);
    public float NextFloat() => (float)NextDouble();
    public bool Chance(float p) => NextDouble() < p;
    public int NextInt(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)(NextDouble() * maxExclusive);

    /// <summary>Derive an independent, reproducible substream. Same (parent-state, name) ⇒ same
    /// stream; different names never collide. Does not mutate this stream.</summary>
    public SeededRng Fork(string name) => new(Mix(_state ^ Fnv1a(name)));

    private static ulong Fnv1a(string s)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in s)
        {
            h = (h ^ (byte)(c & 0xFF)) * 1099511628211UL;
            h = (h ^ (byte)((c >> 8) & 0xFF)) * 1099511628211UL;
        }
        return h;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// WorldState — the OWNED rules model. Health, death, shields, statuses, modifiers,
// resources, cooldowns, positions, factions. Verbs read and mutate this directly;
// this is the cut that makes the damage pipeline testable without an engine.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StatusInstance
{
    public required StatusId Id { get; init; }
    public required StatusCategory Category { get; init; }
    public float ExpireTime { get; set; }
    public int Stacks { get; set; }
    public float Potency { get; set; }
    public float NextTick { get; set; }
    public long Seq { get; init; }
    public ModifierId LinkedModifier { get; set; } // for StatMod statuses
}

public sealed class ShieldInstance
{
    public required ShieldId Id { get; init; }
    public float Remaining { get; set; }
    public float ExpireTime { get; set; }
    public Element? Element { get; init; }
    public long Seq { get; init; }
}

public sealed class StatModifier
{
    public required ModifierId Id { get; init; }
    public required StatId Stat { get; init; }
    public float AmountPct { get; set; }
    public float ExpireTime { get; set; }
    public string SourceClass { get; init; } = "generic";
    public long Seq { get; init; }
}

/// <summary>Diminishing-returns state for CC (§2): 100% → 50% → 25% → immune 5 s, 10 s window.</summary>
public sealed class CcDrState
{
    public List<float> Applications { get; } = new();
    public float ImmuneUntil { get; set; }
}

public sealed class Entity
{
    public required EntityRef Ref { get; init; }
    public string Name { get; init; } = "";
    public Faction Faction { get; set; } = Faction.Neutral;
    public Vec3 Position { get; set; } = Vec3.Zero;
    public float Hp { get; set; }
    public float MaxHp { get; set; }
    public bool Dead { get; set; }

    public Dictionary<StatId, float> BaseStats { get; } = new();
    public Dictionary<ResourceKind, float> Resources { get; } = new();
    public Dictionary<ResourceKind, float> MaxResources { get; } = new();
    public List<StatusInstance> Statuses { get; } = new();
    public List<ShieldInstance> Shields { get; } = new();
    public List<StatModifier> Modifiers { get; } = new();
    public Dictionary<string, float> CooldownUntil { get; } = new();
    public CcDrState CcDr { get; } = new();
}

public sealed class ZoneInstance
{
    public required ZoneId Id { get; init; }
    public required EntityRef Owner { get; init; }
    public required SpellId Spell { get; init; }
    public int Tier { get; init; }
    public Vec3 Center { get; set; }
    public ZoneShape Shape { get; init; }
    public float Size { get; init; }
    public Affects Affects { get; init; }
    public float TickInterval { get; init; }
    public float NextTick { get; set; }
    public float ExpireTime { get; init; }
    public Clause[] TickClauses { get; init; } = Array.Empty<Clause>();
    public int Depth { get; init; }
    /// <summary>Monotonic tick counter — the culture-independent key for per-tick RNG substreams.</summary>
    public int TickIndex { get; set; }
}

public sealed class WorldState
{
    private int _nextEntityId = 1;
    private int _nextZoneId = 1;
    private int _nextShieldId = 1;
    private int _nextModifierId = 1;
    private long _seq = 1;

    public float Now { get; set; }
    public Dictionary<EntityRef, Entity> Entities { get; } = new();
    public List<ZoneInstance> Zones { get; } = new();

    public long NextSeq() => _seq++;
    public ZoneId NewZoneId() => new(_nextZoneId++);
    public ShieldId NewShieldId() => new(_nextShieldId++);
    public ModifierId NewModifierId() => new(_nextModifierId++);

    public Entity Get(EntityRef r) => Entities[r];
    public bool IsDead(EntityRef r) => Entities[r].Dead;

    public Entity AddEntity(string name, Faction faction, float maxHp, Vec3 pos)
    {
        var e = new Entity { Ref = new EntityRef(_nextEntityId++), Name = name, Faction = faction, MaxHp = maxHp, Hp = maxHp, Position = pos };
        Entities[e.Ref] = e;
        return e;
    }

    public IEnumerable<Entity> AliveEntities() => Entities.Values.Where(e => !e.Dead);

    // ── Health primitives (the model's own invariant; verbs orchestrate around these) ──

    /// <summary>Subtract health, clamp at 0, flip Dead. Returns true if this call killed it.</summary>
    public bool ApplyHealthLoss(EntityRef r, float amount)
    {
        var e = Entities[r];
        if (e.Dead) return false;
        e.Hp = MathF.Max(0f, e.Hp - MathF.Max(0f, amount));
        if (e.Hp <= 0f) { e.Dead = true; return true; }
        return false;
    }

    /// <summary>Restore health (never revives). Returns effective healing applied.</summary>
    public float ApplyHeal(EntityRef r, float amount)
    {
        var e = Entities[r];
        if (e.Dead) return 0f;
        float before = e.Hp;
        e.Hp = MathF.Min(e.MaxHp, e.Hp + MathF.Max(0f, amount));
        return e.Hp - before;
    }

    // ── Stats: base × (1 + Σ strongest-per-source-class percent / 100) ──

    public float GetBaseStat(EntityRef r, StatId s) =>
        Entities[r].BaseStats.TryGetValue(s, out var v) ? v : DefaultStat(s);

    public static float DefaultStat(StatId s) => s.Kind switch
    {
        StatKind.CritMult => 2.0f,
        StatKind.MoveSpeed => 1.0f,
        StatKind.CastSpeed => 1.0f,
        _ => 0f
    };

    /// <summary>Aggregate a stat in its declared currency (StatUnits). Additive stats — base and
    /// modifiers both in POINTS — sum; multiplier stats scale their baseline. (Applying
    /// base×(1+pts/100) to a base-0 stat was the critical bug: 0 × anything = 0.) Callers that need
    /// a probability/resist FRACTION must use GetStatFraction, never divide here.</summary>
    public float GetStat(EntityRef r, StatId s)
    {
        float baseVal = GetBaseStat(r, s);
        // strongest per source-class, summed across classes
        var byClass = Entities[r].Modifiers.Where(m => m.Stat.Equals(s))
            .GroupBy(m => m.SourceClass)
            .Select(g => g.OrderByDescending(m => MathF.Abs(m.AmountPct)).First().AmountPct);
        float pts = byClass.Sum();
        return StatUnits.IsMultiplier(s.Kind) ? baseVal * (1f + pts / 100f) : baseVal + pts;
    }

    /// <summary>Points → the fraction a probability/resist stat is consumed as. The single place the
    /// points→fraction conversion lives, so no consumer re-derives it and saturates: a +5-point
    /// evasion buff is 5%, not a guaranteed dodge.</summary>
    public float GetStatFraction(EntityRef r, StatId s) => StatUnits.Of(s.Kind) switch
    {
        StatUnit.Probability => Math.Clamp(GetStat(r, s) / 100f, 0f, 1f),
        StatUnit.ResistFraction => Math.Clamp(GetStat(r, s) / 100f, 0f, 0.95f),
        _ => throw new InvalidOperationException($"{s.Kind} is not a fraction-consumed stat.")
    };

    public ModifierId AddStatModifier(EntityRef r, StatId stat, float amountPct, float duration, string sourceClass)
    {
        var id = NewModifierId();
        Entities[r].Modifiers.Add(new StatModifier
        {
            Id = id, Stat = stat, AmountPct = amountPct,
            ExpireTime = duration <= 0 ? float.PositiveInfinity : Now + duration,
            SourceClass = sourceClass, Seq = NextSeq()
        });
        return id;
    }

    // ── Shields ──

    public ShieldInstance AddShield(EntityRef r, float amount, float duration, Element? element)
    {
        var s = new ShieldInstance
        {
            Id = NewShieldId(), Remaining = amount,
            ExpireTime = duration <= 0 ? float.PositiveInfinity : Now + duration,
            Element = element, Seq = NextSeq()
        };
        Entities[r].Shields.Add(s);
        return s;
    }

    // ── Resources ──

    public float GetResource(EntityRef r, ResourceKind k) => Entities[r].Resources.TryGetValue(k, out var v) ? v : 0f;

    public bool SpendResource(EntityRef r, ResourceKind k, float amount)
    {
        if (k == ResourceKind.Health) { ApplyHealthLoss(r, amount); return true; }
        float have = GetResource(r, k);
        if (have < amount) return false;
        Entities[r].Resources[k] = have - amount;
        return true;
    }

    // ── Cooldowns ──

    public float CooldownRemaining(EntityRef r, SpellId spell) =>
        Entities[r].CooldownUntil.TryGetValue(spell.Value, out var until) ? MathF.Max(0f, until - Now) : 0f;

    public void PutOnCooldown(EntityRef r, SpellId spell, float seconds) =>
        Entities[r].CooldownUntil[spell.Value] = Now + seconds;

    // ── Status queries (gate state derives from statuses) ──

    public bool HasStatus(EntityRef r, StatusId id) => Entities[r].Statuses.Any(s => s.Id == id);
    public StatusInstance? FindStatus(EntityRef r, StatusId id) => Entities[r].Statuses.FirstOrDefault(s => s.Id == id);
    public bool IsStunned(EntityRef r) => HasStatus(r, StatusId.Stun) || HasStatus(r, StatusId.Freeze);
    public bool IsSilenced(EntityRef r) => HasStatus(r, StatusId.Silence);
    public bool IsUnstoppable(EntityRef r) => HasStatus(r, StatusId.Unstoppable);
    public bool IsInvulnerable(EntityRef r) => HasStatus(r, StatusId.Invulnerable);
}

// ─────────────────────────────────────────────────────────────────────────────
// Engine seam — SHRUNK to genuinely un-scaffoldable, spatial/AI primitives. Nothing
// rules-shaped lives here. Analytic spatial *queries* (line-sweep, circle-overlap) are
// deterministic and live in CombatSim, not behind this seam.
// ─────────────────────────────────────────────────────────────────────────────

public interface IWorldApi
{
    /// <summary>Clamp a destination to reachable navmesh. Headless build: identity.</summary>
    Vec3 NavClamp(Vec3 desired);

    /// <summary>Spawn a new engine-backed entity (AI controller, pathing). Widen-scope.</summary>
    EntityRef SpawnEntity(string archetype, Vec3 at, EntityRef owner);

    /// <summary>Issue an AI/faction change. Widen-scope.</summary>
    void SetFaction(EntityRef entity, Faction faction);
}

// ─────────────────────────────────────────────────────────────────────────────
// Effect context — carries everything a verb needs. State (rules) and World (engine)
// are distinct handles. Immutable record: nested execution clones via `with`.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ImpactCtx(Vec3 Point, Vec3 Direction, Vec3? Normal, DeliveryType DeliveredBy);

public sealed class TargetSet
{
    public EntityRef[] Units { get; init; } = Array.Empty<EntityRef>();
    public Vec3? Point { get; init; }
    public Vec3? Direction { get; init; }
}

public sealed record EffectCtx
{
    public required EntityRef Caster { get; init; }
    public required SpellId SpellId { get; init; }
    public int ClauseIndex { get; init; }
    public required SeededRng Rng { get; init; }
    public required WorldState State { get; init; }
    public required IWorldApi World { get; init; }
    public required ImpactCtx Impact { get; init; }
    public required EventBus Events { get; init; }
    public int Depth { get; init; }
    public int Tier { get; init; } = 1;
}

// ─────────────────────────────────────────────────────────────────────────────
// Verb results — every verb returns a structured result (feeds conditional clauses)
// as well as emitting events (feeds everything else).
// ─────────────────────────────────────────────────────────────────────────────

public abstract record EffectResult;

public sealed record DamageResult(float Final, float AbsorbedByShield, bool Crit, bool Killed, float Overkill, bool Evaded) : EffectResult
{
    public static DamageResult EvadedResult => new(0, 0, false, false, 0, true);
}
public sealed record HealResult(float Effective, float Overheal) : EffectResult;
public sealed record ShieldResult(ShieldId Id, float Amount) : EffectResult;
public sealed record ApplyStatusResult(bool Applied, bool Resisted, int StacksNow, bool Refreshed) : EffectResult;
public sealed record DispelResult(IReadOnlyList<StatusId> Removed) : EffectResult;
public sealed record ModifyStatResult(ModifierId Id) : EffectResult;
public sealed record DisplaceResult(Vec3 FinalPos, float Moved, bool Blocked, EntityRef CollidedWith) : EffectResult;
public sealed record SpawnZoneResult(ZoneId Id) : EffectResult;
public sealed record NoResult : EffectResult;
