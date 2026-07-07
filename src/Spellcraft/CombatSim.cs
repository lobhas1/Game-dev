// CombatSim.cs — the VERBS and the interpreter that runs them.
//
// This is the reference implementation of verbs.md: the pieces that make a bag of verbs
// into a combat sim. Strict recursive JSON parsing (closed vocabulary as law), the budget
// resolver (proposal → compiled), spatial-lite geometry, the eight Tier-0 verbs over a
// shared mitigation pipeline, the §2 resolution pipeline, the §7 trigger phase with all
// guards at one choke point, and the fixed-timestep clock.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Spellcraft;

// ─────────────────────────────────────────────────────────────────────────────
// Engine seam implementation (headless). Spatial *queries* are analytic (Geometry);
// only the truly un-scaffoldable primitives are identity/stubs here.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HeadlessWorldApi : IWorldApi
{
    public Vec3 NavClamp(Vec3 desired) => desired; // documented identity: no navmesh in headless
    public EntityRef SpawnEntity(string archetype, Vec3 at, EntityRef owner) =>
        throw new NotImplementedInSliceException("SpawnEntity is widen-scope (summon needs AI + pathing).");
    public void SetFaction(EntityRef entity, Faction faction) =>
        throw new NotImplementedInSliceException("SetFaction is widen-scope (charm/AI).");
}

// ─────────────────────────────────────────────────────────────────────────────
// Spatial-lite geometry — deterministic analytic queries over XZ positions.
// ─────────────────────────────────────────────────────────────────────────────

public static class Geometry
{
    /// <summary>Nearest entity struck by a ray from <paramref name="from"/> along the XZ unit
    /// <paramref name="dir"/>, within <paramref name="hitRadius"/> of the ray and <= maxRange ahead.</summary>
    public static (EntityRef Hit, Vec3 Point) LineSweepNearest(
        Vec3 from, Vec3 dir, IEnumerable<Entity> entities, float maxRange, float hitRadius)
    {
        float best = float.PositiveInfinity;
        EntityRef hit = EntityRef.None;
        Vec3 point = from;
        foreach (var e in entities)
        {
            var rel = e.Position - from;
            float t = rel.X * dir.X + rel.Z * dir.Z; // projection along ray (XZ)
            if (t <= 0f || t > maxRange) continue;
            var onRay = from + dir * t;
            if (onRay.DistanceXZ(e.Position) <= hitRadius && t < best)
            {
                best = t;
                hit = e.Ref;
                point = e.Position;
            }
        }
        return (hit, hit.IsValid ? point : from + dir * maxRange);
    }

    public static List<EntityRef> CircleOverlap(Vec3 center, float radius, IEnumerable<Entity> entities) =>
        entities.Where(e => e.Position.DistanceXZ(center) <= radius).Select(e => e.Ref).ToList();
}

// ─────────────────────────────────────────────────────────────────────────────
// Budget resolver / compiler — proposal (bands + shares) → compiled (numbers).
// Magnitude = tierBudget(tier) × share × deliveryMult × castMult (§1). Durations, sizes,
// intervals, speeds, costs and amplify resolve directly from their bands.
// ─────────────────────────────────────────────────────────────────────────────

public static class SpellCompiler
{
    public static Spell Compile(Spell s, BalanceOverrides? overrides = null)
    {
        if (s.Compiled) return s;
        float dMult = BalanceTables.DeliveryMultiplier(s.Delivery.Type);
        float cMult = BalanceTables.CastMultiplier(s.Cast.Mode);

        var cast = s.Cast with
        {
            CastTime = s.Cast.Mode == CastMode.Cast && s.Cast.CastTimeBand is not null
                ? BalanceTables.CastTime(s.Cast.CastTimeBand) : 0f,
            Cooldown = BalanceTables.Cooldown(s.Cast.CooldownBand),
            Cost = s.Cast.Cost with { Amount = BalanceTables.Cost(s.Cast.Cost.AmountBand) }
        };

        var delivery = s.Delivery with
        {
            Size = s.Delivery.SizeBand is not null ? BalanceTables.Size(s.Delivery.SizeBand) : 0f,
            Range = s.Delivery.RangeBand is not null ? BalanceTables.Size(s.Delivery.RangeBand) : 0f,
            Speed = s.Delivery.SpeedBand is not null ? BalanceTables.Speed(s.Delivery.SpeedBand) : 0f
        };

        var clauses = s.Clauses.Select(c => CompileClause(c, s.Tier, dMult, cMult, overrides)).ToArray();
        return s with { Cast = cast, Delivery = delivery, Clauses = clauses, Compiled = true };
    }

    private static float Magnitude(int tier, float share, float dMult, float cMult) =>
        BalanceTables.TierBudget(tier) * share * dMult * cMult;

    /// <summary>Resolve the ifStatus amplify multiplier for a band. Only 'major' consults the
    /// per-run override; 'minor'/'extreme' and a null override fall through to BalanceTables, so
    /// null overrides reproduce the committed pricing exactly.</summary>
    private static float ResolveAmplify(string band, BalanceOverrides? overrides) =>
        band == "major" && overrides?.AmplifyMajor is float m ? m : BalanceTables.Multiplier(band);

    /// <summary>Status potency resolves per category. Stat-mod potency is a bounded percentage
    /// (tier-independent, clamped) — not the exponential damage budget. DoT/HoT potency IS
    /// budget-scaled, so a higher-tier burn ticks harder (§5).</summary>
    private static float ResolvePotency(StatusId status, int tier, float share, float dMult, float cMult) =>
        StatusCatalog.Get(status).Category == StatusCategory.StatMod
            ? MathF.Min(share * 100f, BalanceTables.StatModPotencyCap)
            : Magnitude(tier, share, dMult, cMult);

    private static Clause CompileClause(Clause c, int tier, float dMult, float cMult, BalanceOverrides? overrides)
    {
        VerbParams p = c.Params switch
        {
            DamageParams d => d with { Amount = Magnitude(tier, d.Share, dMult, cMult) },
            HealParams h => h with { Amount = Magnitude(tier, h.Share, dMult, cMult) },
            ShieldParams sh => sh with
            {
                Amount = Magnitude(tier, sh.Share, dMult, cMult),
                Duration = BalanceTables.Duration(sh.DurationBand)
            },
            ApplyStatusParams a => a with
            {
                Potency = ResolvePotency(a.Status, tier, a.Share, dMult, cMult),
                Duration = BalanceTables.Duration(a.DurationBand)
            },
            ModifyStatParams m => m with
            {
                AmountPct = (m.Direction == "debuff" ? -1f : 1f) * m.Share * 100f,
                Duration = BalanceTables.Duration(m.DurationBand)
            },
            DisplaceParams dp => dp with
            {
                Distance = dp.DistanceBand is not null ? BalanceTables.Size(dp.DistanceBand) : 0f,
                Speed = dp.SpeedBand is not null ? BalanceTables.Speed(dp.SpeedBand) : 0f
            },
            SpawnZoneParams z => z with
            {
                Size = BalanceTables.Size(z.SizeBand),
                Duration = BalanceTables.Duration(z.DurationBand),
                TickInterval = BalanceTables.Interval(z.TickIntervalBand),
                TickClauses = z.TickClauses.Select(tc => CompileClause(tc, tier, dMult, cMult, overrides)).ToArray(),
                OnEnterClauses = z.OnEnterClauses?.Select(tc => CompileClause(tc, tier, dMult, cMult, overrides)).ToArray(),
                OnExitClauses = z.OnExitClauses?.Select(tc => CompileClause(tc, tier, dMult, cMult, overrides)).ToArray()
            },
            _ => c.Params // DispelParams and any param with no resolvable fields
        };

        TemplateSpec? t = c.Template switch
        {
            IfStatusTemplate ifs => ifs with
            {
                Amplify = ResolveAmplify(ifs.AmplifyBand, overrides),
                Also = ifs.Also?.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray()
            },
            OnHitTemplate oh => oh with { Clauses = oh.Clauses.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray() },
            OnKillTemplate ok => ok with { Clauses = ok.Clauses.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray() },
            OnCritTemplate oc => oc with { Clauses = oc.Clauses.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray() },
            DelayedTemplate dl => dl with
            {
                Delay = BalanceTables.Duration(dl.DelayBand),
                Clauses = dl.Clauses.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray()
            },
            RepeatingTemplate rp => rp with
            {
                Interval = BalanceTables.Interval(rp.IntervalBand),
                Clauses = rp.Clauses.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray()
            },
            OnExpireTemplate oe => oe with { Clauses = oe.Clauses.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray() },
            HpThresholdTemplate hp => hp with { Clauses = hp.Clauses.Select(a => CompileClause(a, tier, dMult, cMult, overrides)).ToArray() },
            _ => c.Template
        };

        return c with { Params = p, Template = t };
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Strict recursive JSON parser. Proposal-stage descriptor → Spell. An unknown verb,
// status, enum value, or object field is a hard failure — the closed vocabulary is law.
// ─────────────────────────────────────────────────────────────────────────────

public static class SpellJson
{
    public static Spell Parse(string json)
    {
        JsonNode? root = JsonNode.Parse(json) ?? throw new SpellParseException("Empty JSON.");
        var r = new Reader(AsObject(root, "spell"));
        var spell = new Spell
        {
            Id = new SpellId(r.String("id")),
            Tier = r.Int("tier"),
            Cast = ParseCast(r.Object("cast", required: false)),
            Delivery = ParseDelivery(r.Object("delivery", required: false)),
            Clauses = ParseClauseArray(r.Array("clauses", required: true)!)
        };
        r.RequireNoExtra("spell");
        return spell;
    }

    private static CastSpec ParseCast(Reader? r)
    {
        if (r is null) return new CastSpec();
        var cost = r.Object("cost", required: false);
        var spec = new CastSpec
        {
            Mode = r.Enum<CastMode>("mode"),
            CastTimeBand = r.StringOrNull("castTime"),
            CooldownBand = r.StringOrNull("cooldown") ?? "short",
            Interruptible = r.BoolOrNull("interruptible") ?? false,
            MoveWhileCasting = r.BoolOrNull("moveWhileCasting") ?? false,
            Cost = ParseCost(cost)
        };
        r.RequireNoExtra("cast");
        return spec;
    }

    private static CostSpec ParseCost(Reader? r)
    {
        if (r is null) return new CostSpec();
        var c = new CostSpec
        {
            Resource = r.Enum<ResourceKind>("resource"),
            AmountBand = r.StringOrNull("amount") ?? "trivial"
        };
        r.RequireNoExtra("cost");
        return c;
    }

    private static DeliverySpec ParseDelivery(Reader? r)
    {
        if (r is null) return new DeliverySpec();
        var d = new DeliverySpec
        {
            Type = r.Enum<DeliveryType>("type"),
            Shape = r.EnumOrNull<ZoneShape>("shape"),
            SizeBand = r.StringOrNull("size"),
            RangeBand = r.StringOrNull("range"),
            SpeedBand = r.StringOrNull("speed"),
            Telegraph = r.BoolOrNull("telegraph") ?? false
        };
        r.RequireNoExtra("delivery");
        return d;
    }

    private static Clause[] ParseClauseArray(JsonArray arr) =>
        arr.Select((n, i) => ParseClause(AsObject(n!, $"clause[{i}]"))).ToArray();

    private static Clause ParseClause(JsonObject obj)
    {
        var r = new Reader(obj);
        var verb = r.Enum<VerbId>("verb");
        var template = ParseTemplate(r.Object("template", required: false));
        var pars = ParseParams(r, verb);
        r.RequireNoExtra($"clause '{verb}'");
        return new Clause(verb, pars, template);
    }

    private static VerbParams ParseParams(Reader r, VerbId verb) => verb switch
    {
        VerbId.Damage => new DamageParams
        {
            Element = r.Enum<Element>("element"),
            Share = (float)r.Double("share"),
            CanCrit = r.BoolOrNull("canCrit") ?? true,
            Tags = r.StringArrayOrNull("tags"),
            LeechPct = (float)(r.DoubleOrNull("leechPct") ?? 0)
        },
        VerbId.Heal => new HealParams
        {
            Share = (float)r.Double("share"),
            CanOverhealToShield = r.BoolOrNull("canOverhealToShield") ?? false
        },
        VerbId.Shield => new ShieldParams
        {
            Share = (float)r.Double("share"),
            DurationBand = r.StringOrNull("duration") ?? "short",
            Element = r.EnumOrNull<Element>("element")
        },
        VerbId.ApplyStatus => new ApplyStatusParams
        {
            Status = r.Enum<StatusId>("status"),
            DurationBand = r.StringOrNull("duration") ?? "short",
            Stacks = r.IntOrNull("stacks") ?? 1,
            Share = (float)(r.DoubleOrNull("share") ?? 0)
        },
        VerbId.Dispel => new DispelParams
        {
            Category = r.EnumOrNull<DispelCategory>("category") ?? DispelCategory.Debuff,
            Element = r.EnumOrNull<Element>("element"),
            Count = r.IntOrNull("count") ?? 1,
            Order = r.EnumOrNull<DispelOrder>("order") ?? DispelOrder.Newest
        },
        VerbId.ModifyStat => new ModifyStatParams
        {
            Stat = ParseStat(r),
            Share = (float)r.Double("share"),
            Direction = r.StringOrNull("direction") ?? "buff",
            DurationBand = r.StringOrNull("duration") ?? "short"
        },
        VerbId.Displace => new DisplaceParams
        {
            Subject = r.StringOrNull("subject") ?? "target",
            Mode = r.Enum<DisplaceMode>("mode"),
            To = r.EnumOrNull<DisplaceAnchor>("to"),
            DistanceBand = r.StringOrNull("distance"),
            SpeedBand = r.StringOrNull("speed")
        },
        VerbId.SpawnZone => new SpawnZoneParams
        {
            Shape = r.EnumOrNull<ZoneShape>("shape") ?? ZoneShape.Circle,
            SizeBand = r.StringOrNull("size") ?? "medium",
            DurationBand = r.StringOrNull("duration") ?? "medium",
            TickIntervalBand = r.StringOrNull("tickInterval") ?? "medium",
            Affects = r.EnumOrNull<Affects>("affects") ?? Affects.Enemies,
            TickClauses = ParseNestedClauses(r, "tickClauses") ?? Array.Empty<Clause>(),
            OnEnterClauses = ParseNestedClauses(r, "onEnterClauses"),
            OnExitClauses = ParseNestedClauses(r, "onExitClauses")
        },
        _ => throw new NotImplementedInSliceException(
            $"Verb '{verb}' is valid vocabulary but not implemented in this slice.")
    };

    private static StatId ParseStat(Reader r)
    {
        string kind = r.String("stat");
        if (kind.Equals("resist", StringComparison.OrdinalIgnoreCase))
            return StatId.Resist(r.Enum<Element>("element"));
        if (!Enum.TryParse<StatKind>(kind, ignoreCase: true, out var k))
            throw new SpellParseException($"Unknown stat '{kind}'.");
        return new StatId(k);
    }

    private static Clause[]? ParseNestedClauses(Reader r, string key)
    {
        var arr = r.Array(key, required: false);
        return arr is null ? null : ParseClauseArray(arr);
    }

    private static TemplateSpec? ParseTemplate(Reader? r)
    {
        if (r is null) return null;
        var kind = r.Enum<TemplateKind>("kind");
        TemplateSpec t = kind switch
        {
            TemplateKind.OnHit => new OnHitTemplate { Clauses = ParseNestedClauses(r, "clauses") ?? Array.Empty<Clause>() },
            TemplateKind.OnKill => new OnKillTemplate { Clauses = ParseNestedClauses(r, "clauses") ?? Array.Empty<Clause>() },
            TemplateKind.OnCrit => new OnCritTemplate { Clauses = ParseNestedClauses(r, "clauses") ?? Array.Empty<Clause>() },
            TemplateKind.IfStatus => new IfStatusTemplate
            {
                Status = r.Enum<StatusId>("status"),
                Consume = r.BoolOrNull("consume") ?? false,
                AmplifyBand = r.StringOrNull("amplify") ?? "minor",
                Also = ParseNestedClauses(r, "also")
            },
            TemplateKind.Delayed => new DelayedTemplate
            {
                DelayBand = r.StringOrNull("delay") ?? "short",
                Clauses = ParseNestedClauses(r, "clauses") ?? Array.Empty<Clause>()
            },
            TemplateKind.Repeating => new RepeatingTemplate
            {
                Times = r.IntOrNull("times") ?? 1,
                IntervalBand = r.StringOrNull("interval") ?? "medium",
                Clauses = ParseNestedClauses(r, "clauses") ?? Array.Empty<Clause>()
            },
            TemplateKind.OnExpire => new OnExpireTemplate { Clauses = ParseNestedClauses(r, "clauses") ?? Array.Empty<Clause>() },
            TemplateKind.HpThreshold => new HpThresholdTemplate
            {
                Pct = (float)(r.DoubleOrNull("pct") ?? 0),
                Side = r.StringOrNull("side") ?? "below",
                Clauses = ParseNestedClauses(r, "clauses") ?? Array.Empty<Clause>()
            },
            _ => throw new SpellParseException($"Unknown template kind '{kind}'.")
        };
        r.RequireNoExtra($"template '{kind}'");
        return t;
    }

    private static JsonObject AsObject(JsonNode n, string ctx) =>
        n as JsonObject ?? throw new SpellParseException($"Expected object at {ctx}.");

    /// <summary>Wraps a JsonObject and tracks consumed keys so unknown fields become errors.</summary>
    private sealed class Reader
    {
        private readonly JsonObject _obj;
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        public Reader(JsonObject obj) => _obj = obj;

        private JsonNode? Node(string key) { _used.Add(key); return _obj.TryGetPropertyValue(key, out var n) ? n : null; }

        public string String(string key) => StringOrNull(key) ?? throw new SpellParseException($"Missing required '{key}'.");
        public string? StringOrNull(string key) => Node(key)?.GetValue<string>();
        public bool? BoolOrNull(string key) => Node(key) is { } n ? n.GetValue<bool>() : null;
        public int Int(string key) => IntOrNull(key) ?? throw new SpellParseException($"Missing required '{key}'.");
        public int? IntOrNull(string key) => Node(key) is { } n ? n.GetValue<int>() : null;
        public double Double(string key) => DoubleOrNull(key) ?? throw new SpellParseException($"Missing required '{key}'.");
        public double? DoubleOrNull(string key) => Node(key) is { } n ? n.GetValue<double>() : null;

        public string[]? StringArrayOrNull(string key) =>
            Node(key) is JsonArray a ? a.Select(x => x!.GetValue<string>()).ToArray() : null;

        public JsonArray? Array(string key, bool required)
        {
            var n = Node(key);
            if (n is null) return required ? throw new SpellParseException($"Missing required array '{key}'.") : null;
            return n as JsonArray ?? throw new SpellParseException($"'{key}' must be an array.");
        }

        public Reader? Object(string key, bool required)
        {
            var n = Node(key);
            if (n is null) return required ? throw new SpellParseException($"Missing required object '{key}'.") : null;
            return new Reader(n as JsonObject ?? throw new SpellParseException($"'{key}' must be an object."));
        }

        public T Enum<T>(string key) where T : struct, Enum
        {
            string s = String(key);
            if (!System.Enum.TryParse<T>(s, ignoreCase: true, out var v))
                throw new SpellParseException($"Unknown {typeof(T).Name} '{s}'. Allowed: {string.Join(", ", System.Enum.GetNames<T>())}.");
            return v;
        }

        public T? EnumOrNull<T>(string key) where T : struct, Enum
        {
            string? s = StringOrNull(key);
            if (s is null) return null;
            if (!System.Enum.TryParse<T>(s, ignoreCase: true, out var v))
                throw new SpellParseException($"Unknown {typeof(T).Name} '{s}'.");
            return v;
        }

        public void RequireNoExtra(string ctx)
        {
            var extra = _obj.Select(kv => kv.Key).Where(k => !_used.Contains(k)).ToList();
            if (extra.Count > 0)
                throw new SpellParseException($"Unknown field(s) in {ctx}: {string.Join(", ", extra)}.");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-cast guard state (§2 recursion guards), enforced at ONE point (TriggerPhase).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CastGuards
{
    public const int MaxDepth = 3;
    public const int MaxClauses = 12;
    public int Enqueued;
}

// ─────────────────────────────────────────────────────────────────────────────
// The eight Tier-0 verbs + shared helpers. All read/write WorldState (rules); the
// engine seam is touched only for NavClamp. Every verb emits and returns a result.
// ─────────────────────────────────────────────────────────────────────────────

public static class SpellVerbs
{
    // ── damage: the spine. evasion → crit → resist → armor → shields → health ──
    public static DamageResult Damage(EffectCtx ctx, EntityRef target, DamageParams p, float amplify)
    {
        var st = ctx.State;
        if (st.IsDead(target)) return new DamageResult(0, 0, false, false, 0, false);
        if (st.IsInvulnerable(target))
        {
            ctx.Events.Emit(new DamageDealt(target, 0, p.Element, false, false, 0, false));
            return new DamageResult(0, 0, false, false, 0, false);
        }

        // evasion (own substream — adding a roll never shifts others). Points → chance via the
        // one declared conversion, so a small evasion buff shifts odds instead of guaranteeing.
        float evasion = st.GetStatFraction(target, StatId.Evasion);
        if (evasion > 0f && ctx.Rng.Fork("evasion").Chance(evasion))
        {
            ctx.Events.Emit(new DamageDealt(target, 0, p.Element, false, false, 0, true));
            return DamageResult.EvadedResult;
        }

        float amount = p.Amount * amplify;
        amount *= 1f + st.GetStat(ctx.Caster, StatId.DamageOut) / 100f;
        amount *= 1f + st.GetStat(target, StatId.DamageIn) / 100f;

        bool crit = false;
        if (p.CanCrit)
        {
            float cc = st.GetStatFraction(ctx.Caster, StatId.CritChance);
            if (cc > 0f && ctx.Rng.Fork("crit").Chance(cc))
            {
                crit = true;
                amount *= st.GetStat(ctx.Caster, StatId.CritMult);
            }
        }

        var (toHealth, absorbed, killed, overkill) = ResolveMitigationAndApply(st, target, amount, p.Element);

        if (p.LeechPct > 0f)
            st.ApplyHeal(ctx.Caster, toHealth * p.LeechPct / 100f);

        ctx.Events.Emit(new DamageDealt(target, toHealth, p.Element, crit, killed, absorbed, false));
        return new DamageResult(toHealth, absorbed, crit, killed, overkill, false);
    }

    /// <summary>Mitigation shared by damage and DoT ticks: resist → armor → shields → health.</summary>
    internal static (float ToHealth, float Absorbed, bool Killed, float Overkill) ResolveMitigationAndApply(
        WorldState st, EntityRef target, float postCrit, Element? element)
    {
        float amount = MathF.Max(0f, postCrit);
        if (element.HasValue)
        {
            float resist = st.GetStatFraction(target, StatId.Resist(element.Value));
            amount *= 1f - resist;
        }
        float armor = st.GetStat(target, StatId.Armor);
        if (armor > 0f) amount *= 1f - armor / (armor + 100f);

        float absorbed = AbsorbShields(st, target, ref amount, element);
        float hpBefore = st.Get(target).Hp;
        bool killed = st.ApplyHealthLoss(target, amount);
        float overkill = MathF.Max(0f, amount - hpBefore);
        return (amount, absorbed, killed, overkill);
    }

    /// <summary>Shields absorb before health. Elemental shields absorb only their element.
    /// Reduces <paramref name="amount"/> to the portion reaching health; returns absorbed.</summary>
    internal static float AbsorbShields(WorldState st, EntityRef target, ref float amount, Element? element)
    {
        float absorbed = 0f;
        var shields = st.Get(target).Shields.OrderBy(s => s.Seq).ToList();
        foreach (var s in shields)
        {
            if (amount <= 0f) break;
            if (s.Element.HasValue && s.Element != element) continue;
            float take = MathF.Min(s.Remaining, amount);
            s.Remaining -= take;
            amount -= take;
            absorbed += take;
        }
        st.Get(target).Shields.RemoveAll(s => s.Remaining <= 0f);
        return absorbed;
    }

    // ── heal: respects death state (no healing corpses) ──
    public static HealResult Heal(EffectCtx ctx, EntityRef target, HealParams p, float amplify)
    {
        var st = ctx.State;
        if (st.IsDead(target)) return new HealResult(0, 0); // that's resurrect (T3), not heal
        float amount = p.Amount * amplify;
        float eff = st.ApplyHeal(target, amount);
        float overheal = amount - eff;
        if (overheal > 0f && p.CanOverhealToShield)
            st.AddShield(target, overheal, BalanceTables.Duration("short"), null);
        ctx.Events.Emit(new Healed(target, eff, overheal));
        return new HealResult(eff, overheal);
    }

    // ── shield: temporary absorb pool ──
    public static ShieldResult Shield(EffectCtx ctx, EntityRef target, ShieldParams p, float amplify)
    {
        var st = ctx.State;
        float amount = p.Amount * amplify;
        var s = st.AddShield(target, amount, p.Duration, p.Element);
        ctx.Events.Emit(new ShieldGranted(target, amount, p.Duration));
        return new ShieldResult(s.Id, amount);
    }

    // ── applyStatus: immunity → CC diminishing returns → catalog stacking ──
    public static ApplyStatusResult ApplyStatus(EffectCtx ctx, EntityRef target, ApplyStatusParams p, float amplify)
    {
        var st = ctx.State;
        if (st.IsDead(target)) return new ApplyStatusResult(false, false, 0, false);
        var def = StatusCatalog.Get(p.Status);
        float potency = p.Potency * amplify;
        float duration = p.Duration;

        if (def.AppliesDr)
        {
            if (st.IsUnstoppable(target))
            {
                ctx.Events.Emit(new StatusApplied(target, p.Status, 0, 0, true, false));
                return new ApplyStatusResult(false, true, 0, false);
            }
            var dr = st.Get(target).CcDr;
            dr.Applications.RemoveAll(t => t < st.Now - 10f);
            if (st.Now < dr.ImmuneUntil)
            {
                ctx.Events.Emit(new StatusApplied(target, p.Status, 0, 0, true, false));
                return new ApplyStatusResult(false, true, 0, false);
            }
            int n = dr.Applications.Count;
            float factor = n switch { 0 => 1f, 1 => 0.5f, 2 => 0.25f, _ => 0f };
            if (factor == 0f)
            {
                dr.ImmuneUntil = st.Now + 5f;
                ctx.Events.Emit(new StatusApplied(target, p.Status, 0, 0, true, false));
                return new ApplyStatusResult(false, true, 0, false);
            }
            duration *= factor;
            dr.Applications.Add(st.Now);
        }

        var existing = st.FindStatus(target, p.Status);
        bool refreshed = false;
        int stacksNow;
        if (existing is not null && def.Stacking != StackRule.None)
        {
            refreshed = true;
            switch (def.Stacking)
            {
                case StackRule.Stacks:
                    existing.Stacks = Math.Min(existing.Stacks + p.Stacks, def.MaxStacks);
                    break;
                case StackRule.Strongest:
                    if (potency < existing.Potency) { /* keep stronger */ }
                    break;
            }
            if (def.Stacking != StackRule.Strongest || potency >= existing.Potency)
                existing.Potency = potency;
            existing.ExpireTime = st.Now + duration;
            stacksNow = existing.Stacks;
            UpdateLinkedModifier(st, target, p.Status, existing.Potency);
        }
        else
        {
            var inst = new StatusInstance
            {
                Id = p.Status,
                Category = def.Category,
                ExpireTime = duration <= 0 ? float.PositiveInfinity : st.Now + duration,
                Stacks = Math.Max(1, p.Stacks),
                Potency = potency,
                NextTick = def.TickInterval > 0 ? st.Now + def.TickInterval : float.PositiveInfinity,
                Seq = st.NextSeq()
            };
            st.Get(target).Statuses.Add(inst);
            stacksNow = inst.Stacks;
            if (def.Category == StatusCategory.StatMod)
                AddLinkedModifiers(st, target, p.Status, potency);
        }

        ctx.Events.Emit(new StatusApplied(target, p.Status, duration, stacksNow, false, refreshed));
        return new ApplyStatusResult(true, false, stacksNow, refreshed);
    }

    // ── dispel: counterplay primitive; returns the removed set ──
    public static DispelResult Dispel(EffectCtx ctx, EntityRef target, DispelParams p)
    {
        var st = ctx.State;
        IEnumerable<StatusInstance> pool = st.Get(target).Statuses;
        pool = p.Category switch
        {
            DispelCategory.Buff => pool.Where(s => StatusCatalog.IsBuff(s.Id)),
            DispelCategory.Debuff => pool.Where(s => StatusCatalog.IsDebuff(s.Id)),
            DispelCategory.Dot => pool.Where(s => s.Category == StatusCategory.Dot),
            _ => pool
        };
        if (p.Element.HasValue)
            pool = pool.Where(s => StatusCatalog.Get(s.Id).DotElement == p.Element);

        pool = p.Order switch
        {
            DispelOrder.Newest => pool.OrderByDescending(s => s.Seq),
            DispelOrder.Oldest => pool.OrderBy(s => s.Seq),
            _ => pool.OrderByDescending(s => s.Potency)
        };

        var toRemove = pool.Take(p.Count).ToList();
        foreach (var s in toRemove)
            RemoveStatusInstance(st, ctx.Events, target, s);
        ctx.Events.Emit(new Dispelled(target, toRemove.Count));
        return new DispelResult(toRemove.Select(s => s.Id).ToList());
    }

    // ── modifyStat: buffs/debuffs as first-class handles ──
    public static ModifyStatResult ModifyStat(EffectCtx ctx, EntityRef target, ModifyStatParams p, float amplify)
    {
        var st = ctx.State;
        var id = st.AddStatModifier(target, p.Stat, p.AmountPct * amplify, p.Duration, "modifyStat");
        ctx.Events.Emit(new StatModified(target, p.Stat, p.AmountPct * amplify, p.Duration));
        return new ModifyStatResult(id);
    }

    // ── displace: six modes, one verb. unstoppable immunes all of it ──
    public static DisplaceResult Displace(EffectCtx ctx, EntityRef subject, DisplaceParams p)
    {
        var st = ctx.State;
        var from = st.Get(subject).Position;
        if (st.IsUnstoppable(subject))
        {
            ctx.Events.Emit(new Displaced(subject, p.Mode, from, from, true));
            return new DisplaceResult(from, 0, true, EntityRef.None);
        }

        Vec3 anchor = (p.To ?? DisplaceAnchor.Point) switch
        {
            DisplaceAnchor.Caster => st.Get(ctx.Caster).Position,
            DisplaceAnchor.Impact => ctx.Impact.Point,
            _ => ctx.Impact.Point
        };

        Vec3 dest = p.Mode switch
        {
            DisplaceMode.Teleport => ctx.World.NavClamp(anchor),
            // Pull/Dash move TOWARD the anchor and must not overshoot it (clamp is correct).
            DisplaceMode.Pull => MoveToward(from, anchor, p.Distance),
            DisplaceMode.Dash => MoveToward(from, anchor, p.Distance),
            // Push moves AWAY from the anchor by the full distance (no overshoot clamp).
            DisplaceMode.Push => from + anchor.DirectionXZ(from) * p.Distance,
            _ => throw new NotImplementedInSliceException($"Displace mode '{p.Mode}' is widen-scope.")
        };
        dest = ctx.World.NavClamp(dest);

        st.Get(subject).Position = dest;
        float moved = from.DistanceXZ(dest);
        ctx.Events.Emit(new Displaced(subject, p.Mode, from, dest, false));
        return new DisplaceResult(dest, moved, false, EntityRef.None);
    }

    private static Vec3 MoveToward(Vec3 from, Vec3 anchor, float distance)
    {
        float d = from.DistanceXZ(anchor);
        if (d <= 1e-6f) return from;
        float step = MathF.Min(distance, d); // don't overshoot the anchor
        var dir = from.DirectionXZ(anchor);
        return from + dir * step;
    }

    // ── spawnZone: registers a persistent ground effect; the clock ticks it ──
    public static SpawnZoneResult SpawnZone(EffectCtx ctx, SpawnZoneParams p)
    {
        var st = ctx.State;
        // Guard: zones may not spawn zones beyond depth 1 (§8).
        if (ctx.Depth >= 1)
            return new SpawnZoneResult(ZoneId.None);

        var zone = new ZoneInstance
        {
            Id = st.NewZoneId(),
            Owner = ctx.Caster,
            Spell = ctx.SpellId,
            Tier = ctx.Tier,
            Center = ctx.Impact.Point,
            Shape = p.Shape,
            Size = p.Size,
            Affects = p.Affects,
            TickInterval = p.TickInterval,
            NextTick = st.Now + p.TickInterval,
            ExpireTime = st.Now + p.Duration,
            TickClauses = p.TickClauses,
            Depth = ctx.Depth
        };
        st.Zones.Add(zone);
        ctx.Events.Emit(new ZoneSpawned(zone.Id, zone.Center, zone.Shape, zone.Size, p.Duration));
        return new SpawnZoneResult(zone.Id);
    }

    // ── status/modifier bookkeeping shared by apply/dispel/expiry ──

    internal static void RemoveStatusInstance(WorldState st, EventBus ev, EntityRef target, StatusInstance s)
    {
        st.Get(target).Statuses.Remove(s);
        st.Get(target).Modifiers.RemoveAll(m => m.SourceClass == SourceClass(s.Id));
        ev.Emit(new StatusRemoved(target, s.Id));
    }

    private static string SourceClass(StatusId id) => $"status:{id}";

    private static void AddLinkedModifiers(WorldState st, EntityRef target, StatusId status, float potency)
    {
        foreach (var (stat, pct) in StatModMapping(status, potency))
            st.AddStatModifier(target, stat, pct, float.PositiveInfinity, SourceClass(status));
    }

    private static void UpdateLinkedModifier(WorldState st, EntityRef target, StatusId status, float potency)
    {
        var mods = st.Get(target).Modifiers.Where(m => m.SourceClass == SourceClass(status)).ToList();
        var mapping = StatModMapping(status, potency).ToList();
        for (int i = 0; i < mods.Count && i < mapping.Count; i++)
            mods[i].AmountPct = mapping[i].Pct;
    }

    /// <summary>Which stat(s) a stat-mod status drives, and by how much (percent from potency).</summary>
    private static IEnumerable<(StatId Stat, float Pct)> StatModMapping(StatusId status, float potency) => status switch
    {
        StatusId.Vulnerable => new[] { (StatId.DamageIn, +potency) },
        StatusId.Weaken => new[] { (StatId.DamageOut, -potency) },
        StatusId.Slow => new[] { (StatId.MoveSpeed, -potency) },
        StatusId.Chill => new[] { (StatId.MoveSpeed, -potency), (StatId.CastSpeed, -potency) },
        StatusId.Haste => new[] { (StatId.MoveSpeed, +potency), (StatId.CastSpeed, +potency) },
        // Blind ("large miss chance") is a documented gap, not a silent no-op: it needs an
        // attacker-accuracy term the damage pipeline doesn't model yet. It applies as a *present*
        // status (Blink Strike relies on that) but has no mechanical effect in this slice — widen.
        StatusId.Blind => Enumerable.Empty<(StatId, float)>(),
        _ => Enumerable.Empty<(StatId, float)>()
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Dispatcher — Clause (data) → verb (function). Delegates never leave this file.
// ─────────────────────────────────────────────────────────────────────────────

public static class VerbDispatcher
{
    public static EffectResult Dispatch(EffectCtx ctx, Clause clause, EntityRef target, float amplify) => clause.Verb switch
    {
        VerbId.Damage => SpellVerbs.Damage(ctx, target, (DamageParams)clause.Params, amplify),
        VerbId.Heal => SpellVerbs.Heal(ctx, target, (HealParams)clause.Params, amplify),
        VerbId.Shield => SpellVerbs.Shield(ctx, target, (ShieldParams)clause.Params, amplify),
        VerbId.ApplyStatus => SpellVerbs.ApplyStatus(ctx, target, (ApplyStatusParams)clause.Params, amplify),
        VerbId.Dispel => SpellVerbs.Dispel(ctx, target, (DispelParams)clause.Params),
        VerbId.ModifyStat => SpellVerbs.ModifyStat(ctx, target, (ModifyStatParams)clause.Params, amplify),
        VerbId.Displace => SpellVerbs.Displace(ctx, target, (DisplaceParams)clause.Params),
        VerbId.SpawnZone => SpellVerbs.SpawnZone(ctx, (SpawnZoneParams)clause.Params),
        _ => throw new NotImplementedInSliceException($"Verb '{clause.Verb}' not built in this slice.")
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// The sim — WorldState + events + engine seam + the pipeline and clock.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record CastReport(bool Success, string? FailReason);

public sealed class Sim
{
    private const float Step = 0.05f;    // fixed 50 ms quantum
    private const float Eps = 1e-4f;

    public WorldState State { get; } = new();
    public EventBus Events { get; } = new();
    public IWorldApi World { get; } = new HeadlessWorldApi();

    private readonly long _baseSeed;
    private readonly BalanceOverrides? _overrides;
    private float _accumulator;
    private int _castSeq;

    public Sim(long seed = 0x5EED, BalanceOverrides? overrides = null)
    {
        _baseSeed = seed;
        _overrides = overrides;
    }

    // ── §2 resolution pipeline ──
    public CastReport Cast(EntityRef caster, Spell spell, EntityRef target = default, Vec3? point = null)
    {
        spell = SpellCompiler.Compile(spell, _overrides);
        var castRng = SeededRng.FromSeed(_baseSeed).Fork($"cast:{caster.Value}:{spell.Id}:{_castSeq++}");
        Events.Emit(new CastStarted(caster, spell.Id));

        // 1. cost & gate
        if (State.IsStunned(caster)) return Fail(caster, spell.Id, "stunned");
        if (spell.Cast.Mode != CastMode.Instant && State.IsSilenced(caster)) return Fail(caster, spell.Id, "silenced");
        if (State.CooldownRemaining(caster, spell.Id) > 0f) return Fail(caster, spell.Id, "cooldown");
        if (!State.SpendResource(caster, spell.Cast.Cost.Resource, spell.Cast.Cost.Amount))
            return Fail(caster, spell.Id, "noResource");
        State.PutOnCooldown(caster, spell.Id, spell.Cast.Cooldown);

        // 2. cast windup (advances the clock; DoTs/zones progress during the cast)
        if (spell.Cast.Mode == CastMode.Cast && spell.Cast.CastTime > 0f)
            Tick(spell.Cast.CastTime);

        // 3. delivery
        var (targets, impact) = ResolveDelivery(caster, spell.Delivery, target, point);

        // 4 + 5. clause loop and trigger phase
        var guards = new CastGuards();
        for (int i = 0; i < spell.Clauses.Length; i++)
            RunClauseGroup(caster, spell, i, spell.Clauses[i], targets, impact, castRng, guards);

        // 6. event flush is implicit: consumers read Events after this returns.
        return new CastReport(true, null);
    }

    private CastReport Fail(EntityRef caster, SpellId id, string reason)
    {
        Events.Emit(new CastFailed(caster, id, reason));
        return new CastReport(false, reason);
    }

    private void RunClauseGroup(EntityRef caster, Spell spell, int ordinal, Clause clause,
        TargetSet targets, ImpactCtx impact, SeededRng castRng, CastGuards guards)
    {
        EffectCtx MakeCtx(EntityRef t) => new()
        {
            Caster = caster, SpellId = spell.Id, ClauseIndex = ordinal,
            Rng = castRng.Fork($"clause:{ordinal}:{t.Value}"),
            State = State, World = World, Impact = impact, Events = Events, Depth = 0, Tier = spell.Tier
        };

        switch (clause.Verb)
        {
            case VerbId.SpawnZone:
                RunClause(MakeCtx(EntityRef.None), clause, EntityRef.None, guards);
                break;
            case VerbId.Displace:
                var subjects = ((DisplaceParams)clause.Params).Subject == "caster"
                    ? new[] { caster }
                    : targets.Units;
                foreach (var s in subjects) RunClause(MakeCtx(s), clause, s, guards);
                break;
            default:
                foreach (var u in targets.Units) RunClause(MakeCtx(u), clause, u, guards);
                break;
        }
    }

    // Executes a single clause: ifStatus pre-amplify → dispatch → post triggers.
    internal EffectResult RunClause(EffectCtx ctx, Clause clause, EntityRef target, CastGuards guards)
    {
        float amplify = 1f;
        bool ifStatusMatched = false;
        if (clause.Template is IfStatusTemplate ifs && target.IsValid && ctx.State.HasStatus(target, ifs.Status))
        {
            amplify = ifs.Amplify;
            ifStatusMatched = true;
        }

        var result = VerbDispatcher.Dispatch(ctx, clause, target, amplify);
        RunTriggers(ctx, clause, target, result, ifStatusMatched, guards);
        return result;
    }

    // §7 trigger phase — executes ifStatus + onHit; the other six parse-validate but no-op.
    private void RunTriggers(EffectCtx ctx, Clause clause, EntityRef target, EffectResult result,
        bool ifStatusMatched, CastGuards guards)
    {
        switch (clause.Template)
        {
            case IfStatusTemplate ifs when ifStatusMatched:
                if (ifs.Consume)
                {
                    var inst = ctx.State.FindStatus(target, ifs.Status);
                    if (inst is not null) SpellVerbs.RemoveStatusInstance(ctx.State, ctx.Events, target, inst);
                }
                if (ifs.Also is { Length: > 0 }) Enqueue(ctx, ifs.Also, target, guards);
                break;

            case OnHitTemplate oh when result is DamageResult { Evaded: false, Final: > 0 }:
                Enqueue(ctx, oh.Clauses, target, guards);
                break;

            // onKill, onCrit, delayed, repeating, onExpire, hpThreshold: parsed & validated,
            // execution is widen-scope. No-op (never throw — the spell parsed legitimately).
            default:
                break;
        }
    }

    // The single enforcement point for the §2 recursion guards.
    private void Enqueue(EffectCtx ctx, Clause[] clauses, EntityRef target, CastGuards guards)
    {
        if (ctx.Depth + 1 > CastGuards.MaxDepth) return;
        foreach (var c in clauses)
        {
            if (guards.Enqueued >= CastGuards.MaxClauses) break;
            guards.Enqueued++;
            var child = ctx with { Depth = ctx.Depth + 1, Rng = ctx.Rng.Fork($"trig:{c.Verb}") };
            var subject = c.Verb == VerbId.Displace && ((DisplaceParams)c.Params).Subject == "caster"
                ? ctx.Caster : target;
            RunClause(child, c, subject, guards);
        }
    }

    // ── delivery layer (slice: self / targetUnit / projectile / groundAoE) ──
    private (TargetSet, ImpactCtx) ResolveDelivery(EntityRef caster, DeliverySpec d, EntityRef target, Vec3? point)
    {
        var casterPos = State.Get(caster).Position;
        switch (d.Type)
        {
            case DeliveryType.Self:
                return (new TargetSet { Units = new[] { caster }, Point = casterPos },
                        new ImpactCtx(casterPos, new Vec3(1, 0, 0), null, d.Type));

            case DeliveryType.TargetUnit:
            {
                var pt = target.IsValid ? State.Get(target).Position : casterPos;
                var units = target.IsValid ? new[] { target } : Array.Empty<EntityRef>();
                return (new TargetSet { Units = units, Point = pt, Direction = casterPos.DirectionXZ(pt) },
                        new ImpactCtx(pt, casterPos.DirectionXZ(pt), null, d.Type));
            }

            case DeliveryType.Projectile:
            {
                Vec3 dir = target.IsValid ? casterPos.DirectionXZ(State.Get(target).Position)
                    : point.HasValue ? casterPos.DirectionXZ(point.Value) : new Vec3(1, 0, 0);
                var (hit, hitPt) = Geometry.LineSweepNearest(
                    casterPos, dir, State.AliveEntities().Where(e => e.Ref != caster), maxRange: 1000f, hitRadius: 1.0f);
                var units = hit.IsValid ? new[] { hit } : Array.Empty<EntityRef>();
                return (new TargetSet { Units = units, Point = hitPt, Direction = dir },
                        new ImpactCtx(hitPt, dir, null, d.Type));
            }

            case DeliveryType.GroundAoE:
            {
                var center = point ?? (target.IsValid ? State.Get(target).Position : casterPos);
                float radius = d.Size > 0f ? d.Size : BalanceTables.Size("medium");
                var units = Geometry.CircleOverlap(center, radius, State.AliveEntities());
                return (new TargetSet { Units = units.ToArray(), Point = center },
                        new ImpactCtx(center, new Vec3(1, 0, 0), null, d.Type));
            }

            default:
                throw new NotImplementedInSliceException($"Delivery '{d.Type}' is widen-scope (melee dropped from this slice).");
        }
    }

    // ── fixed-timestep clock ──
    public void Tick(float dt)
    {
        _accumulator += dt;
        while (_accumulator + Eps >= Step)
        {
            _accumulator -= Step;
            State.Now += Step;
            StepZones();
            StepDots();
            Expire();
        }
    }

    private void StepZones()
    {
        foreach (var zone in State.Zones.ToList())
        {
            while (zone.NextTick <= State.Now + Eps && zone.NextTick <= zone.ExpireTime + Eps)
            {
                TickZone(zone);
                zone.NextTick += zone.TickInterval;
            }
        }
    }

    private void TickZone(ZoneInstance zone)
    {
        var ownerFaction = State.Get(zone.Owner).Faction;
        var affected = Geometry.CircleOverlap(zone.Center, zone.Size, State.AliveEntities())
            .Where(u => ZoneAffects(zone.Affects, ownerFaction, State.Get(u).Faction))
            .ToList();
        Events.Emit(new ZoneTicked(zone.Id, affected.Count));

        var impact = new ImpactCtx(zone.Center, new Vec3(1, 0, 0), null, DeliveryType.GroundAoE);
        // Key the substream on an integer tick ordinal, never a formatted float — "0.25" vs "0,25"
        // across cultures would fork divergent streams and break §2.7 determinism.
        zone.TickIndex++;
        var rng = SeededRng.FromSeed(_baseSeed).Fork($"zone:{zone.Id.Value}:{zone.TickIndex}");
        var guards = new CastGuards();

        foreach (var unit in affected)
        {
            for (int i = 0; i < zone.TickClauses.Length; i++)
            {
                var clause = zone.TickClauses[i];
                var subject = clause.Verb == VerbId.Displace && ((DisplaceParams)clause.Params).Subject == "caster"
                    ? zone.Owner : unit;
                var ctx = new EffectCtx
                {
                    Caster = zone.Owner, SpellId = zone.Spell, ClauseIndex = i,
                    Rng = rng.Fork($"u{unit.Value}:c{i}"),
                    State = State, World = World, Impact = impact, Events = Events,
                    Depth = 1, Tier = zone.Tier
                };
                RunClause(ctx, clause, subject, guards);
            }
        }
    }

    private static bool ZoneAffects(Affects a, Faction owner, Faction unit) => a switch
    {
        Affects.Enemies => unit != owner,
        Affects.Allies => unit == owner,
        _ => true
    };

    private void StepDots()
    {
        foreach (var e in State.Entities.Values)
        {
            if (e.Dead) continue;
            foreach (var s in e.Statuses.ToList())
            {
                var def = StatusCatalog.Get(s.Id);
                if (def.TickInterval <= 0f) continue;
                while (s.NextTick <= State.Now + Eps && s.NextTick <= s.ExpireTime + Eps)
                {
                    ApplyTick(e.Ref, s, def);
                    s.NextTick += def.TickInterval;
                    if (e.Dead) break;
                }
            }
        }
    }

    private void ApplyTick(EntityRef target, StatusInstance s, StatusDef def)
    {
        float magnitude = s.Potency * s.Stacks;
        if (def.Category == StatusCategory.Hot)
        {
            float eff = State.ApplyHeal(target, magnitude);
            Events.Emit(new Healed(target, eff, magnitude - eff));
        }
        else // Dot
        {
            var (toHealth, absorbed, killed, _) = SpellVerbs.ResolveMitigationAndApply(State, target, magnitude, def.DotElement);
            Events.Emit(new DamageDealt(target, toHealth, def.DotElement ?? Element.Arcane, false, killed, absorbed, false));
        }
    }

    private void Expire()
    {
        // Same epsilon the tick loop uses, so a status whose final tick fired also expires
        // in the same quantum (Now can land a hair under an integer boundary in float).
        float now = State.Now + Eps;
        foreach (var e in State.Entities.Values)
        {
            foreach (var s in e.Statuses.Where(s => now >= s.ExpireTime).ToList())
                SpellVerbs.RemoveStatusInstance(State, Events, e.Ref, s);
            e.Shields.RemoveAll(sh => now >= sh.ExpireTime);
            e.Modifiers.RemoveAll(m => now >= m.ExpireTime);
        }
        foreach (var z in State.Zones.Where(z => now >= z.ExpireTime).ToList())
        {
            State.Zones.Remove(z);
            Events.Emit(new ZoneExpired(z.Id));
        }
    }
}
