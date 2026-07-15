using System.Text.Json.Nodes;

namespace Arena;

/// <summary>A fusion parent / seed card: naming metadata bundled with a gate-clean proposal spell.
/// The top-level fields feed the naming line; <see cref="Spell"/>'s clauses feed the mechanics
/// oracle.</summary>
public sealed record Seed(string Name, string Emoji, string Element,
    IReadOnlyList<string> Tags, string Flavor, JsonNode Spell)
{
    public int Tier => Spell["tier"]?.GetValue<int>() ?? 1;
    public JsonNode Clauses => Spell["clauses"] ?? new JsonArray();

    public static Seed Load(string path)
    {
        var o = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidOperationException($"seed '{path}' must be a JSON object.");
        // A fusion record loaded as a depth-2 parent carries its naming fields under "concept" (its
        // top-level "name" is the kebab file-id, e.g. "steam"); a seed card carries them at the top
        // level. Read the ingredient identity from whichever exists so the naming line reads "Steam".
        var meta = o["concept"] as JsonObject ?? o;
        var spell = o["spell"] ?? throw new InvalidOperationException($"seed '{path}' has no 'spell'.");
        return new Seed(
            Str(meta, "name") ?? Path.GetFileNameWithoutExtension(path),
            Str(meta, "emoji") ?? "",
            Str(meta, "element") ?? "",
            ReadTags(meta), Str(meta, "flavor") ?? "", spell.DeepClone());
    }

    /// <summary>The doctrine's ingredient line: <c>"Name" — element: e, tags: [a, b], flavor: "f"</c>.</summary>
    public string NamingLine() =>
        $"\"{Name}\" — element: {Element}, tags: [{string.Join(", ", Tags)}], flavor: \"{Flavor}\"";

    private static string? Str(JsonObject o, string k) =>
        o[k] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : null;
    private static List<string> ReadTags(JsonObject o) =>
        (o["tags"] as JsonArray)?.Where(t => t is not null).Select(t => t!.GetValue<string>()).ToList() ?? new();
}

/// <summary>The naming oracle's ruling: what the fusion MEANS.</summary>
public sealed record Concept(string Name, string Emoji, string Element,
    IReadOnlyList<string> Tags, string Flavor, string Why)
{
    public string Block() =>
        $"\"{Name}\" — element: {Element}, tags: [{string.Join(", ", Tags)}], flavor: \"{Flavor}\", why: \"{Why}\"";

    /// <summary>Parse a naming-oracle JSON reply (loose: fences stripped, outermost object clipped).</summary>
    public static Concept? Parse(string raw)
    {
        if (KitGenerator.ExtractObject(raw) is not JsonObject o) return null;
        string? name = S(o, "name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new Concept(name!, S(o, "emoji") ?? "", S(o, "element") ?? "",
            (o["tags"] as JsonArray)?.Where(t => t is not null).Select(t => t!.GetValue<string>()).ToList() ?? new(),
            S(o, "flavor") ?? "", S(o, "why") ?? "");
    }

    private static string? S(JsonObject o, string k) =>
        o[k] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : null;
}

/// <summary>Recipe law for the T0 slice: equal tiers ascend by one, unequal keep the higher, capped
/// at tier 3 (the doctrine's cap-5 is out of slice).</summary>
public static class TierLaw
{
    public static int Of(int a, int b) => a == b ? Math.Min(3, a + 1) : Math.Max(a, b);
}

/// <summary>The FIXED tag → mechanics mapping (spec §"Tag → mechanics mapping"). Scores how much of a
/// concept's mappable tags its clauses actually express. summon/transform/perceive are UNMAPPABLE:
/// excluded from the denominator, counted as quantization pressure.</summary>
public static class TagCoverage
{
    public static readonly IReadOnlySet<string> Unmappable =
        new HashSet<string>(StringComparer.Ordinal) { "summon", "transform", "perceive" };

    private static readonly HashSet<string> ControlStatuses =
        new(StringComparer.Ordinal) { "freeze", "stun", "root", "slow", "chill", "fear", "silence", "disarm" };
    private static readonly HashSet<string> ConcealStatuses =
        new(StringComparer.Ordinal) { "stealth", "blind" };

    /// <summary>Which mappable tags of <paramref name="conceptTags"/> are satisfied by the spell.</summary>
    public static (int Satisfied, int Mappable, List<string> Unsatisfied) Score(IEnumerable<string> conceptTags, JsonNode spell)
    {
        var facts = Facts(spell);
        int satisfied = 0, mappable = 0;
        var unsat = new List<string>();
        foreach (var tag in conceptTags.Distinct(StringComparer.Ordinal))
        {
            if (Unmappable.Contains(tag)) continue;
            if (!Mappings.ContainsKey(tag)) continue; // unknown tag: not mappable, not counted
            mappable++;
            if (Mappings[tag](facts)) satisfied++;
            else unsat.Add(tag);
        }
        return (satisfied, mappable, unsat);
    }

    private static readonly Dictionary<string, Func<MechFacts, bool>> Mappings = new(StringComparer.Ordinal)
    {
        ["damage"]   = f => f.HasDamage,
        ["heal"]     = f => f.HasHeal,
        ["ward"]     = f => f.HasShield || f.HasArmorResistBuff,
        ["control"]  = f => f.HasControlStatus || f.HasPull,
        ["movement"] = f => f.HasDisplace,
        ["conceal"]  = f => f.HasConcealStatus || f.HasZone,
        ["area"]     = f => f.HasGroundAoE || f.HasZone,
        ["duration"] = f => f.HasMedLongDuration || f.HasZone,
    };

    /// <summary>Single-source accessor over the frozen F2 mapping: does the spell's mechanics deliver
    /// <paramref name="tag"/>? Unmappable/unknown tags (summon/transform/perceive) are never covered.
    /// Reused by ancestry-eval so it can never drift from the F2 mapper.</summary>
    public static bool Covers(string tag, MechFacts facts) =>
        Mappings.TryGetValue(tag, out var pred) && pred(facts);

    public sealed class MechFacts
    {
        public bool HasDamage, HasHeal, HasShield, HasArmorResistBuff, HasControlStatus,
            HasPull, HasDisplace, HasConcealStatus, HasZone, HasGroundAoE, HasMedLongDuration;
    }

    public static MechFacts Facts(JsonNode spell)
    {
        var f = new MechFacts();
        if ((spell["delivery"]?["type"]?.GetValue<string>()) == "groundAoE") f.HasGroundAoE = true;
        if (spell["clauses"] is JsonArray clauses)
            foreach (var c in clauses) Walk(c, f);
        return f;
    }

    private static void Walk(JsonNode? clause, MechFacts f)
    {
        if (clause is not JsonObject c) return;
        string verb = c["verb"]?.GetValue<string>() ?? "";
        string? duration = c["duration"] is JsonValue dv && dv.GetValueKind() == System.Text.Json.JsonValueKind.String ? dv.GetValue<string>() : null;
        if (duration is "medium" or "long") f.HasMedLongDuration = true;

        switch (verb)
        {
            case "damage": f.HasDamage = true; break;
            case "heal": f.HasHeal = true; break;
            case "shield": f.HasShield = true; break;
            case "applyStatus":
                string st = c["status"]?.GetValue<string>() ?? "";
                if (ControlStatuses.Contains(st)) f.HasControlStatus = true;
                if (ConcealStatuses.Contains(st)) f.HasConcealStatus = true;
                break;
            case "modifyStat":
                string stat = c["stat"]?.GetValue<string>() ?? "";
                string dir = c["direction"]?.GetValue<string>() ?? "";
                if ((stat == "armor" || stat == "resist") && dir == "buff") f.HasArmorResistBuff = true;
                break;
            case "displace":
                f.HasDisplace = true;
                if ((c["mode"]?.GetValue<string>() ?? "") == "pull") f.HasPull = true;
                break;
            case "spawnZone":
                f.HasZone = true;
                foreach (var key in new[] { "tickClauses", "onEnterClauses", "onExitClauses" })
                    if (c[key] is JsonArray arr) foreach (var tc in arr) Walk(tc, f);
                break;
        }

        // recurse into template also/clauses
        if (c["template"] is JsonObject t)
            foreach (var key in new[] { "also", "clauses" })
                if (t[key] is JsonArray arr) foreach (var tc in arr) Walk(tc, f);
    }
}
