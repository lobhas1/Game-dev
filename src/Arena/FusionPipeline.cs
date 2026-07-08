using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spellcraft;

namespace Arena;

/// <summary>The audit record of one fusion: parents, concept, gated clauses, both prompt shas,
/// gate/repair/discard notes, timestamp. Serialized to arena/fusions/&lt;name&gt;.record.json.</summary>
public sealed record FusionRecord(
    string Name, int Tier,
    string ParentA, string ParentB, string ParentAPath, string ParentBPath,
    Concept Concept, JsonNode? Spell,
    bool FirstPassOk, bool Repaired, bool Discarded, string? GateError,
    string NamingSha, string MechanicsSha, string Timestamp)
{
    public bool Gated => !Discarded && Spell is not null;

    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["tier"] = Tier,
        ["parents"] = new JsonObject { ["a"] = ParentA, ["b"] = ParentB, ["aPath"] = ParentAPath, ["bPath"] = ParentBPath },
        ["concept"] = new JsonObject
        {
            ["name"] = Concept.Name, ["emoji"] = Concept.Emoji, ["element"] = Concept.Element,
            ["tags"] = new JsonArray(Concept.Tags.Select(t => (JsonNode)t!).ToArray()),
            ["flavor"] = Concept.Flavor, ["why"] = Concept.Why
        },
        ["gate"] = new JsonObject
        {
            ["firstPassOk"] = FirstPassOk, ["repaired"] = Repaired, ["discarded"] = Discarded,
            ["error"] = GateError is null ? null : JsonValue.Create(GateError)
        },
        ["shas"] = new JsonObject { ["naming"] = NamingSha, ["mechanics"] = MechanicsSha },
        ["timestamp"] = Timestamp,
        ["spell"] = Spell?.DeepClone()
    };

    public static FusionRecord FromJson(JsonNode n)
    {
        var concept = n["concept"]!;
        return new FusionRecord(
            n["name"]?.GetValue<string>() ?? "",
            n["tier"]?.GetValue<int>() ?? 1,
            n["parents"]?["a"]?.GetValue<string>() ?? "", n["parents"]?["b"]?.GetValue<string>() ?? "",
            n["parents"]?["aPath"]?.GetValue<string>() ?? "", n["parents"]?["bPath"]?.GetValue<string>() ?? "",
            new Concept(
                concept["name"]?.GetValue<string>() ?? "", concept["emoji"]?.GetValue<string>() ?? "",
                concept["element"]?.GetValue<string>() ?? "",
                (concept["tags"] as JsonArray)?.Where(t => t is not null).Select(t => t!.GetValue<string>()).ToList() ?? new(),
                concept["flavor"]?.GetValue<string>() ?? "", concept["why"]?.GetValue<string>() ?? ""),
            n["spell"]?.DeepClone(),
            n["gate"]?["firstPassOk"]?.GetValue<bool>() ?? false,
            n["gate"]?["repaired"]?.GetValue<bool>() ?? false,
            n["gate"]?["discarded"]?.GetValue<bool>() ?? false,
            n["gate"]?["error"]?.GetValue<string>(),
            n["shas"]?["naming"]?.GetValue<string>() ?? "", n["shas"]?["mechanics"]?.GetValue<string>() ?? "",
            n["timestamp"]?.GetValue<string>() ?? "");
    }
}

/// <summary>The fuse pipeline: naming call → concept, mechanics call → ONE proposal spell, gate with
/// one repair. Pure over its oracle (StubOracle offline), so the whole pipeline is testable.</summary>
public static class FusionPipeline
{
    public static async Task<FusionRecord> FuseAsync(
        IOracle oracle, Seed a, Seed b, int? tierOverride,
        string namingTemplate, string mechanicsTemplate,
        string namingSha, string mechanicsSha, string timestamp, string aPath, string bPath)
    {
        int tier = tierOverride ?? TierLaw.Of(a.Tier, b.Tier);
        bool self = string.Equals(aPath, bPath, StringComparison.OrdinalIgnoreCase);

        // ── naming call ──
        string namingPrompt = RenderNaming(namingTemplate, a, b, self, tier);
        var concept = Concept.Parse(await oracle.CompleteAsync(namingPrompt));
        if (concept is null)
            return new FusionRecord(Kebab(a.Name + "-" + b.Name), tier, a.Name, b.Name, aPath, bPath,
                new Concept("(naming failed)", "", "", new List<string>(), "", ""), null,
                false, false, true, "naming reply unparseable", namingSha, mechanicsSha, timestamp);

        string name = Kebab(concept.Name);

        // ── mechanics call + gate (one repair) ──
        string mechPrompt = RenderMechanics(mechanicsTemplate, a, b, concept, tier);
        JsonNode? node = KitGenerator.ExtractObject(await oracle.CompleteAsync(mechPrompt));
        string? err1 = "mechanics reply unparseable";
        bool firstPass = node is not null && TryGate(node, out err1);
        if (firstPass)
            return Rec(name, tier, a, b, aPath, bPath, concept, node!, true, false, false, null, namingSha, mechanicsSha, timestamp);

        string repairPrompt = BuildRepair(mechPrompt, node, err1);
        JsonNode? node2 = KitGenerator.ExtractObject(await oracle.CompleteAsync(repairPrompt));
        string? err2 = "repair reply unparseable";
        bool repairedOk = node2 is not null && TryGate(node2, out err2);
        if (repairedOk)
            return Rec(name, tier, a, b, aPath, bPath, concept, node2!, false, true, false, null, namingSha, mechanicsSha, timestamp);

        return Rec(name, tier, a, b, aPath, bPath, concept, null, false, false, true, err2 ?? err1, namingSha, mechanicsSha, timestamp);
    }

    private static FusionRecord Rec(string name, int tier, Seed a, Seed b, string aPath, string bPath,
        Concept concept, JsonNode? spell, bool firstPass, bool repaired, bool discarded, string? err,
        string namingSha, string mechanicsSha, string ts) =>
        new(name, tier, a.Name, b.Name, aPath, bPath, concept, spell, firstPass, repaired, discarded, err, namingSha, mechanicsSha, ts);

    private static bool TryGate(JsonNode node, out string? error)
    {
        try { _ = SpellCompiler.Compile(SpellJson.Parse(node.ToJsonString())); error = null; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static string RenderNaming(string tmpl, Seed a, Seed b, bool self, int tier) =>
        tmpl.Replace("{{SPELL_A_LINE}}", a.NamingLine())
            .Replace("{{SPELL_B_LINE}}", b.NamingLine())
            .Replace("{{SELF_FUSION_NOTE}}", self
                ? "A and B are the same spell: rule a refined, purified, or intensified form of that concept.\n\n" : "")
            .Replace("{{TIER}}", tier.ToString(CultureInfo.InvariantCulture));

    private static string RenderMechanics(string tmpl, Seed a, Seed b, Concept c, int tier) =>
        tmpl.Replace("{{PARENT_A}}", ParentBlock(a))
            .Replace("{{PARENT_B}}", ParentBlock(b))
            .Replace("{{CONCEPT}}", c.Block())
            .Replace("{{TIER}}", tier.ToString(CultureInfo.InvariantCulture));

    private static string ParentBlock(Seed s) => s.NamingLine() + "\n  clauses: " + s.Clauses.ToJsonString();

    private static string BuildRepair(string prompt, JsonNode? failed, string? error) =>
        prompt + "\n\n---\nREPAIR: the spell below failed the strict parser.\nSpell:\n" +
        (failed?.ToJsonString() ?? "(unparseable)") + "\nParser error: " + (error ?? "unparseable") +
        "\nReturn ONLY a single corrected JSON spell object that fixes this error. No prose, no fences.";

    public static string Kebab(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        return string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    // ── mode runners ──

    public static async Task<int> RunFuse(string pathA, string pathB, int? tierOverride, string model, string? stubFile, TextWriter outw)
    {
        var oracle = MakeOracle(model, stubFile);
        var rec = await FuseOne(oracle, pathA, pathB, tierOverride);
        WriteRecord(rec, outw);
        return rec.Gated ? 0 : 3;
    }

    public static async Task<int> RunFuseBatch(string pairsFile, string model, string? stubFile, TextWriter outw)
    {
        var oracle = MakeOracle(model, stubFile);
        int gated = 0, discarded = 0, total = 0;
        foreach (var raw in File.ReadAllLines(pairsFile))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { outw.WriteLine($"skip (need two paths): {line}"); continue; }
            total++;
            var rec = await FuseOne(oracle, ResolvePath(parts[0]), ResolvePath(parts[1]), tierOverride: null);
            WriteRecord(rec, outw);
            if (rec.Gated) gated++; else discarded++;
        }
        outw.WriteLine($"\nfuse-batch: {total} pairs → {gated} gated, {discarded} discarded.");
        return 0;
    }

    private static async Task<FusionRecord> FuseOne(IOracle oracle, string pathA, string pathB, int? tierOverride)
    {
        var a = Seed.Load(pathA);
        var b = Seed.Load(pathB);
        string namingTmpl = File.ReadAllText(PromptTemplate.LocateRepoFile("prompts/naming-oracle.md"));
        string mechTmpl = File.ReadAllText(PromptTemplate.LocateRepoFile("prompts/fusion-mechanics-oracle.md"));
        string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return await FuseAsync(oracle, a, b, tierOverride, namingTmpl, mechTmpl,
            Evaluator.Sha256Hex(namingTmpl), Evaluator.Sha256Hex(mechTmpl), ts,
            RelPath(pathA), RelPath(pathB));
    }

    private static IOracle MakeOracle(string model, string? stubFile)
    {
        if (stubFile is null) return new LiveAnthropicOracle(model); // reads ANTHROPIC_API_KEY
        var blocks = File.ReadAllText(stubFile).Replace("\r\n", "\n").Split("\n===\n", StringSplitOptions.None)
            .Select(b => b.Trim()).Where(b => b.Length > 0).ToArray();
        return new StubOracle(blocks);
    }

    public static void WriteRecord(FusionRecord rec, TextWriter outw)
    {
        string dir = Path.Combine(PromptTemplate.ArenaDir(), "fusions");
        Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = true };

        File.WriteAllText(Path.Combine(dir, rec.Name + ".record.json"), rec.ToJson().ToJsonString(opts));
        if (rec.Gated)
            File.WriteAllText(Path.Combine(dir, rec.Name + ".spell.json"), rec.Spell!.ToJsonString(opts));

        string status = rec.Discarded ? "DISCARD" : rec.Repaired ? "repaired" : "first-pass";
        File.AppendAllText(Path.Combine(PromptTemplate.ArenaDir(), "fusions.log"),
            string.Join(" | ",
                rec.Timestamp, "name=" + rec.Name, "tier=" + rec.Tier,
                "parents=" + rec.ParentA + "+" + rec.ParentB,
                "tags=[" + string.Join(",", rec.Concept.Tags) + "]",
                "gate=" + status,
                "namingSha=" + rec.NamingSha, "mechanicsSha=" + rec.MechanicsSha,
                rec.GateError is null ? "" : "error=" + rec.GateError.Replace("|", "/").Replace("\n", " ")) + Environment.NewLine);

        outw.WriteLine($"{status,-10} {rec.ParentA} + {rec.ParentB} → {rec.Concept.Name} (T{rec.Tier})  [{string.Join(",", rec.Concept.Tags)}]  → {rec.Name}.record.json");
    }

    private static string RelPath(string p)
    {
        try { return Path.GetRelativePath(PromptTemplate.RepoRoot(), p).Replace('\\', '/'); }
        catch { return p.Replace('\\', '/'); }
    }

    private static string ResolvePath(string p) => File.Exists(p) ? p : PromptTemplate.LocateRepoFile(p);
}
