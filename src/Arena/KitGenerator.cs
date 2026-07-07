using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spellcraft;

namespace Arena;

public sealed record RejectionRecord(string Brief, int SpellIndex, string Error, string Repaired);

public sealed record GateOutcome(
    List<Spell> Accepted, List<JsonNode> AcceptedJson,
    int Generated, int FirstPassOk, int AcceptedTotal,
    List<RejectionRecord> Rejections);

/// <summary>Kit generation: render the single-authority prompt, call the oracle, and gate every
/// spell through SpellJson.Parse with exactly ONE repair attempt per failing spell.</summary>
public static class KitGenerator
{
    /// <summary>Gate ONE oracle-generated kit (one call for the array; one repair call per failing
    /// spell). Pure — no file I/O — so tests can drive it with a StubOracle.</summary>
    public static async Task<GateOutcome> GateKitAsync(IOracle oracle, string promptTemplate,
        string brief, int tier, int kitSize)
    {
        string prompt = PromptTemplate.Render(promptTemplate, brief, tier, kitSize);
        var spellNodes = ExtractArray(await oracle.CompleteAsync(prompt));

        var accepted = new List<Spell>();
        var acceptedJson = new List<JsonNode>();
        var rejections = new List<RejectionRecord>();
        int generated = 0, firstPassOk = 0, acceptedTotal = 0;

        for (int i = 0; i < spellNodes.Count; i++)
        {
            generated++;
            if (TryGate(spellNodes[i], out var spell, out var err))
            {
                firstPassOk++;
                acceptedTotal++;
                accepted.Add(spell!);
                acceptedJson.Add(spellNodes[i]);
                continue;
            }

            // exactly one repair attempt: re-prompt with the parser error appended
            var repaired = ExtractObject(await oracle.CompleteAsync(BuildRepairPrompt(prompt, spellNodes[i], err!)));
            Spell? spell2 = null;
            string? err2 = null;
            bool repairedOk = repaired is not null && TryGate(repaired, out spell2, out err2);
            if (repairedOk)
            {
                acceptedTotal++;
                accepted.Add(spell2!);
                acceptedJson.Add(repaired!);
                rejections.Add(new RejectionRecord(brief, i, err!, "yes (repaired)"));
            }
            else
            {
                rejections.Add(new RejectionRecord(brief, i, err!, $"no ({err2 ?? "repair unparseable"})"));
            }
        }

        return new GateOutcome(accepted, acceptedJson, generated, firstPassOk, acceptedTotal, rejections);
    }

    /// <summary>generate mode: gate `count` kits, persist accepted kits, append rejections.log, and
    /// print first-pass / post-repair validity and verb marginals.</summary>
    public static async Task RunGenerateAsync(IOracle oracle, string brief, int tier, int kitSize,
        int count, string outDir, TextWriter outw)
    {
        string template = PromptTemplate.LoadPrompt();
        Directory.CreateDirectory(outDir);
        string rejectionsPath = Path.Combine(PromptTemplate.ArenaDir(), "rejections.log");
        Directory.CreateDirectory(PromptTemplate.ArenaDir());
        string slug = Slug(brief);

        int totalGenerated = 0, totalFirstPass = 0, totalAccepted = 0;
        var verbMarginals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var allRejections = new List<RejectionRecord>();

        for (int k = 0; k < count; k++)
        {
            var outcome = await GateKitAsync(oracle, template, brief, tier, kitSize);
            totalGenerated += outcome.Generated;
            totalFirstPass += outcome.FirstPassOk;
            totalAccepted += outcome.AcceptedTotal;
            allRejections.AddRange(outcome.Rejections);

            foreach (var sp in outcome.Accepted)
                foreach (var verb in sp.Clauses.SelectMany(WalkVerbs))
                {
                    string v = JsonVerb(verb);
                    verbMarginals[v] = verbMarginals.GetValueOrDefault(v) + 1;
                }

            if (outcome.AcceptedJson.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var n in outcome.AcceptedJson) arr.Add(n);
                string kitPath = Path.Combine(outDir, $"{slug}-{k + 1}.json");
                File.WriteAllText(kitPath, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                outw.WriteLine($"wrote {kitPath} ({outcome.AcceptedJson.Count} spells)");
            }
        }

        if (allRejections.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var r in allRejections)
                sb.AppendLine($"{r.Brief} | spell #{r.SpellIndex} | repaired: {r.Repaired} | error: {OneLine(r.Error)}");
            File.AppendAllText(rejectionsPath, sb.ToString());
        }

        double firstPassPct = totalGenerated == 0 ? 0 : 100.0 * totalFirstPass / totalGenerated;
        double postRepairPct = totalGenerated == 0 ? 0 : 100.0 * totalAccepted / totalGenerated;
        outw.WriteLine();
        outw.WriteLine($"brief: \"{brief}\"  tier: {tier}  kit-size: {kitSize}  kits: {count}");
        outw.WriteLine($"generated spells:     {totalGenerated}");
        outw.WriteLine($"first-pass validity:  {firstPassPct.ToString("0.0", CultureInfo.InvariantCulture)}%  ({totalFirstPass}/{totalGenerated})");
        outw.WriteLine($"post-repair validity: {postRepairPct.ToString("0.0", CultureInfo.InvariantCulture)}%  ({totalAccepted}/{totalGenerated})");
        outw.WriteLine("verb marginals:       " + (verbMarginals.Count == 0
            ? "(none)"
            : string.Join(", ", verbMarginals.Select(kv => $"{kv.Key}={kv.Value}"))));
        if (allRejections.Count > 0)
            outw.WriteLine($"rejections logged:    {allRejections.Count} → {rejectionsPath}");
    }

    // The gate: strict parse (vocabulary: verbs/statuses/elements/fields) THEN compile (band
    // resolution). Band validity is a compile-time check in BalanceTables, so an unknown band only
    // becomes a hard failure once compiled — both steps together are the strict front door.
    private static bool TryGate(JsonNode node, out Spell? spell, out string? error)
    {
        try { spell = SpellCompiler.Compile(SpellJson.Parse(node.ToJsonString())); error = null; return true; }
        catch (Exception ex) { spell = null; error = ex.Message; return false; }
    }

    private static string BuildRepairPrompt(string originalPrompt, JsonNode failed, string error) =>
        originalPrompt +
        "\n\n---\nREPAIR: the spell below failed the strict parser.\n" +
        "Spell:\n" + failed.ToJsonString() +
        "\nParser error: " + error +
        "\nReturn ONLY a single corrected JSON spell object that fixes this error. No prose, no fences.";

    public static List<JsonNode> ExtractArray(string raw)
    {
        var node = ParseLoose(raw);
        var list = new List<JsonNode>();
        if (node is JsonArray arr)
        {
            foreach (var el in arr) if (el is not null) list.Add(el.DeepClone());
        }
        else if (node is JsonObject) list.Add(node);
        return list;
    }

    public static JsonNode? ExtractObject(string raw)
    {
        var node = ParseLoose(raw);
        if (node is JsonObject) return node;
        if (node is JsonArray arr && arr.Count > 0 && arr[0] is not null) return arr[0]!.DeepClone();
        return null;
    }

    // Strip markdown fences and clip to the outermost [] or {} region, then parse.
    private static JsonNode? ParseLoose(string raw)
    {
        string s = raw.Trim().Replace("```json", "").Replace("```", "").Trim();
        int firstArr = s.IndexOf('['), firstObj = s.IndexOf('{');
        int start = firstArr >= 0 && (firstObj < 0 || firstArr < firstObj) ? firstArr : firstObj;
        if (start < 0) return null;
        char close = s[start] == '[' ? ']' : '}';
        int end = s.LastIndexOf(close);
        if (end <= start) return null;
        try { return JsonNode.Parse(s.Substring(start, end - start + 1)); }
        catch { return null; }
    }

    private static string JsonVerb(VerbId v)
    {
        string n = v.ToString();
        return char.ToLowerInvariant(n[0]) + n.Substring(1);
    }

    // Every verb a clause fires, recursing into spawnZone tick/enter/exit clauses and template
    // clauses — so the Phase B marginal baseline counts zone and setup archetypes, not just the
    // top-level verb.
    private static IEnumerable<VerbId> WalkVerbs(Clause c)
    {
        yield return c.Verb;
        if (c.Params is SpawnZoneParams z)
            foreach (var v in Nested(z.TickClauses).Concat(Nested(z.OnEnterClauses)).Concat(Nested(z.OnExitClauses)))
                yield return v;
        foreach (var v in Nested(TemplateClauses(c.Template)))
            yield return v;
    }

    private static IEnumerable<VerbId> Nested(IEnumerable<Clause>? clauses) =>
        clauses is null ? Enumerable.Empty<VerbId>() : clauses.SelectMany(WalkVerbs);

    private static IEnumerable<Clause> TemplateClauses(TemplateSpec? t) => t switch
    {
        IfStatusTemplate ifs => ifs.Also ?? Enumerable.Empty<Clause>(),
        OnHitTemplate o => o.Clauses,
        OnKillTemplate o => o.Clauses,
        OnCritTemplate o => o.Clauses,
        DelayedTemplate o => o.Clauses,
        RepeatingTemplate o => o.Clauses,
        OnExpireTemplate o => o.Clauses,
        HpThresholdTemplate o => o.Clauses,
        _ => Enumerable.Empty<Clause>()
    };

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        return string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ");
}
