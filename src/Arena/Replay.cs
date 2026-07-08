using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spellcraft;

namespace Arena;

/// <summary>One fighter in a replay's entity manifest (spawn state the renderer needs).</summary>
public sealed record ReplayEntity(int Id, string Name, float MaxHp, Vec3 Spawn);

/// <summary>Captures the ordered event stream + per-event sim-time + entity manifest from a live
/// FightEngine.Run, WITHOUT changing the fight. Passed as an optional argument to Run; when absent
/// the fight is byte-for-byte identical (pinned by the existing golden). Timestamps are observed at
/// quantum boundaries — they are not part of any projection line, so they never affect the gate.</summary>
public sealed class ReplayRecorder
{
    private readonly List<(float Time, GameEvent Event)> _events = new();
    public IReadOnlyList<(float Time, GameEvent Event)> Events => _events;
    public IReadOnlyList<ReplayEntity> Entities { get; private set; } = Array.Empty<ReplayEntity>();

    public void Init(params ReplayEntity[] entities) => Entities = entities;

    /// <summary>Stamp any events emitted since the last mark with the current sim time.</summary>
    public void Mark(Sim sim)
    {
        var evs = sim.Events.Events;
        float now = sim.State.Now;
        for (int i = _events.Count; i < evs.Count; i++) _events.Add((now, evs[i]));
    }
}

/// <summary>Replay export + verify. The projection line for each event has ONE home —
/// GameEvent.Canonical() in src/Spellcraft. Verify reconstructs the real GameEvent objects from the
/// file's payload and calls that same Canonical(), so byte-identity is by construction, not
/// duplication. The stored per-event `canonical` string exists only so a non-C# renderer (Unreal)
/// can echo the sim's own text verbatim; Verify cross-checks it and fails loudly on any mismatch.</summary>
public static class Replay
{
    public const string SchemaVersion = "1";

    // ── export ──
    public static JsonObject Export(FightResult r, ReplayRecorder rec, string kitAPath, string kitBPath)
    {
        var entities = new JsonArray();
        foreach (var e in rec.Entities)
            entities.Add(new JsonObject { ["id"] = e.Id, ["name"] = e.Name, ["maxHp"] = (double)e.MaxHp, ["spawn"] = Vec(e.Spawn) });

        var events = new JsonArray();
        foreach (var (t, ev) in rec.Events)
            events.Add(new JsonObject
            {
                ["t"] = (double)t,
                ["type"] = ev.GetType().Name,
                ["canonical"] = ev.Canonical(),
                ["payload"] = Payload(ev)
            });

        return new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["header"] = new JsonObject
            {
                ["kitA"] = r.KitA, ["kitB"] = r.KitB,
                ["kitAPath"] = kitAPath, ["kitBPath"] = kitBPath,
                ["seed"] = r.Seed,
                ["config"] = new JsonObject { ["hpScale"] = (double)r.HpScale, ["mana"] = (double)r.Mana, ["amplifyMajor"] = (double)r.AmplifyMajor },
                ["durationSeconds"] = (double)r.DurationSeconds,
                ["winner"] = r.Winner, ["endReason"] = r.EndReason,
                ["entities"] = entities
            },
            ["events"] = events
        };
    }

    private static JsonObject Vec(Vec3 v) => new() { ["x"] = (double)v.X, ["y"] = (double)v.Y, ["z"] = (double)v.Z };

    private static JsonObject Payload(GameEvent e) => e switch
    {
        DamageDealt d => new() { ["target"] = d.Target.Value, ["amount"] = (double)d.Amount, ["element"] = d.Element.ToString(), ["crit"] = d.Crit, ["killed"] = d.Killed, ["absorbed"] = (double)d.AbsorbedByShield, ["evaded"] = d.Evaded },
        Healed h => new() { ["target"] = h.Target.Value, ["effective"] = (double)h.Effective, ["overheal"] = (double)h.Overheal },
        ShieldGranted s => new() { ["target"] = s.Target.Value, ["amount"] = (double)s.Amount, ["duration"] = (double)s.Duration },
        StatusApplied s => new() { ["target"] = s.Target.Value, ["status"] = s.Status.ToString(), ["duration"] = (double)s.Duration, ["stacks"] = s.StacksNow, ["resisted"] = s.Resisted, ["refreshed"] = s.Refreshed },
        StatusRemoved s => new() { ["target"] = s.Target.Value, ["status"] = s.Status.ToString() },
        StatModified m => new() { ["target"] = m.Target.Value, ["statKind"] = m.Stat.Kind.ToString(), ["statElement"] = m.Stat.Element?.ToString(), ["amountPct"] = (double)m.AmountPct, ["duration"] = (double)m.Duration },
        Dispelled d => new() { ["target"] = d.Target.Value, ["count"] = d.Count },
        Displaced d => new() { ["subject"] = d.Subject.Value, ["mode"] = d.Mode.ToString(), ["from"] = Vec(d.From), ["to"] = Vec(d.To), ["blocked"] = d.Blocked },
        ZoneSpawned z => new() { ["id"] = z.Id.Value, ["center"] = Vec(z.Center), ["shape"] = z.Shape.ToString(), ["size"] = (double)z.Size, ["duration"] = (double)z.Duration },
        ZoneTicked z => new() { ["id"] = z.Id.Value, ["affected"] = z.AffectedCount },
        ZoneExpired z => new() { ["id"] = z.Id.Value },
        CastStarted c => new() { ["caster"] = c.Caster.Value, ["spell"] = c.Spell.Value },
        CastFailed c => new() { ["caster"] = c.Caster.Value, ["spell"] = c.Spell.Value, ["reason"] = c.Reason },
        _ => throw new InvalidOperationException($"replay export: unknown event type {e.GetType().Name}")
    };

    // ── reconstruction: rebuild the real GameEvent, then let it render itself ──
    public static GameEvent ReconstructEvent(string type, JsonNode p) => type switch
    {
        "DamageDealt" => new DamageDealt(Ent(p, "target"), F(p, "amount"), En<Element>(p, "element"), B(p, "crit"), B(p, "killed"), F(p, "absorbed"), B(p, "evaded")),
        "Healed" => new Healed(Ent(p, "target"), F(p, "effective"), F(p, "overheal")),
        "ShieldGranted" => new ShieldGranted(Ent(p, "target"), F(p, "amount"), F(p, "duration")),
        "StatusApplied" => new StatusApplied(Ent(p, "target"), En<StatusId>(p, "status"), F(p, "duration"), I(p, "stacks"), B(p, "resisted"), B(p, "refreshed")),
        "StatusRemoved" => new StatusRemoved(Ent(p, "target"), En<StatusId>(p, "status")),
        "StatModified" => new StatModified(Ent(p, "target"), ReadStat(p), F(p, "amountPct"), F(p, "duration")),
        "Dispelled" => new Dispelled(Ent(p, "target"), I(p, "count")),
        "Displaced" => new Displaced(Ent(p, "subject"), En<DisplaceMode>(p, "mode"), V(p, "from"), V(p, "to"), B(p, "blocked")),
        "ZoneSpawned" => new ZoneSpawned(new ZoneId(I(p, "id")), V(p, "center"), En<ZoneShape>(p, "shape"), F(p, "size"), F(p, "duration")),
        "ZoneTicked" => new ZoneTicked(new ZoneId(I(p, "id")), I(p, "affected")),
        "ZoneExpired" => new ZoneExpired(new ZoneId(I(p, "id"))),
        "CastStarted" => new CastStarted(Ent(p, "caster"), new SpellId(S(p, "spell"))),
        "CastFailed" => new CastFailed(Ent(p, "caster"), new SpellId(S(p, "spell")), S(p, "reason")),
        _ => throw new InvalidOperationException($"replay verify: unknown event type '{type}'")
    };

    /// <summary>Reconstruct every event from the file and render it via the sim's own Canonical().</summary>
    public static List<string> RenderFromJson(JsonNode root)
    {
        var lines = new List<string>();
        foreach (var ev in Events(root))
        {
            if (ev is null) continue;
            lines.Add(ReconstructEvent(ev["type"]!.GetValue<string>(), ev["payload"]!).Canonical());
        }
        return lines;
    }

    // ── verify ──
    public static int RunVerify(string file, bool diffAgainstLive, TextWriter outw)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(file)); }
        catch (Exception ex) { outw.WriteLine($"ERROR: cannot parse replay file: {ex.Message}"); return 2; }
        if (root is null) { outw.WriteLine("ERROR: replay file is empty/null."); return 2; }
        return Verify(root, diffAgainstLive, outw);
    }

    public static int Verify(JsonNode root, bool diffAgainstLive, TextWriter outw)
    {
        string? ver = root["schemaVersion"]?.GetValue<string>();
        if (ver != SchemaVersion) { outw.WriteLine($"ERROR: unsupported schemaVersion '{ver}' (expected '{SchemaVersion}')."); return 2; }

        var lines = new List<string>();
        foreach (var ev in Events(root))
        {
            if (ev is null) continue;
            string type = ev["type"]?.GetValue<string>() ?? "";
            GameEvent reconstructed;
            try { reconstructed = ReconstructEvent(type, ev["payload"] ?? new JsonObject()); }
            catch (Exception ex) { outw.WriteLine($"ERROR: cannot reconstruct event '{type}': {ex.Message}"); return 3; }

            string line = reconstructed.Canonical();
            string? stored = ev["canonical"]?.GetValue<string>();
            if (stored is not null && !string.Equals(stored, line, StringComparison.Ordinal))
            {
                outw.WriteLine("ERROR: reconstructed line does not match stored canonical (corrupt/tampered file):");
                outw.WriteLine($"  reconstructed: {line}");
                outw.WriteLine($"  stored:        {stored}");
                return 3;
            }
            lines.Add(line);
        }

        if (!diffAgainstLive)
        {
            foreach (var l in lines) outw.WriteLine(l);
            return 0;
        }

        var live = ReRunLive(root);
        var diff = Diff(live, lines);
        if (diff.Count == 0)
        {
            outw.WriteLine($"OK: {lines.Count} projection lines — live == replay (byte-identical).");
            return 0;
        }
        outw.WriteLine($"DIFF: {diff.Count} differing line(s) (live | replay):");
        foreach (var d in diff) outw.WriteLine(d);
        return 1;
    }

    private static IReadOnlyList<string> ReRunLive(JsonNode root)
    {
        var h = root["header"] ?? throw new InvalidOperationException("replay has no header — cannot re-run live.");
        var config = new FightConfig
        {
            HpScale = (float)(h["config"]?["hpScale"]?.GetValue<double>() ?? 0),
            Mana = (float)(h["config"]?["mana"]?.GetValue<double>() ?? 250),
            AmplifyMajor = (float?)(h["config"]?["amplifyMajor"]?.GetValue<double>())
        };
        string aPath = Resolve(h["kitAPath"]!.GetValue<string>());
        string bPath = Resolve(h["kitBPath"]!.GetValue<string>());
        long seed = h["seed"]!.GetValue<long>();
        var a = Kit.Load(aPath, config.Overrides);
        var b = Kit.Load(bPath, config.Overrides);
        return FightEngine.Run(a, b, seed, config).Projection;
    }

    private static List<string> Diff(IReadOnlyList<string> live, IReadOnlyList<string> replay)
    {
        var diff = new List<string>();
        int n = Math.Max(live.Count, replay.Count);
        for (int i = 0; i < n; i++)
        {
            string l = i < live.Count ? live[i] : "<none>";
            string r = i < replay.Count ? replay[i] : "<none>";
            if (!string.Equals(l, r, StringComparison.Ordinal)) diff.Add($"  [{i}] {l} | {r}");
        }
        return diff;
    }

    // ── helpers ──
    private static JsonArray Events(JsonNode root) =>
        root["events"] as JsonArray ?? throw new InvalidOperationException("replay has no events array.");

    private static string Resolve(string p) => File.Exists(p) ? p : PromptTemplate.LocateRepoFile(p);

    private static EntityRef Ent(JsonNode p, string k) => new(I(p, k));
    private static float F(JsonNode p, string k) => (float)p[k]!.GetValue<double>();
    private static int I(JsonNode p, string k) => p[k]!.GetValue<int>();
    private static bool B(JsonNode p, string k) => p[k]!.GetValue<bool>();
    private static string S(JsonNode p, string k) => p[k]!.GetValue<string>();
    private static T En<T>(JsonNode p, string k) where T : struct, Enum => Enum.Parse<T>(S(p, k));
    private static Vec3 V(JsonNode p, string k) { var v = p[k]!; return new Vec3(F(v, "x"), F(v, "y"), F(v, "z")); }

    private static StatId ReadStat(JsonNode p)
    {
        var kind = Enum.Parse<StatKind>(S(p, "statKind"));
        string? se = p["statElement"]?.GetValue<string>();
        return new StatId(kind, se is null ? null : Enum.Parse<Element>(se));
    }
}
