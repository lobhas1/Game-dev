using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spellcraft;

namespace Arena;

/// <summary>M2 showcase: cast ONE spell in a controlled scene (inert dummy, ample HP/mana) and
/// export a normal schema-1 replay. The sim produces every event — this class only stages the scene
/// and decides when to stop ticking; no combat logic lives here.</summary>
public static class Showcase
{
    public const float DummyDistance = FightEngine.SpawnDistance; // standard arena spacing
    public const float CasterMana = 10_000f;                       // ample — cost never gates the one cast
    public const float HpBudgetFactor = 10f;                       // target HP = 10× tier budget
    public const float TailSeconds = 1f;                           // quiescence tail
    public const float DefaultMaxSeconds = 30f;                    // sim-time cap
    private const float Quantum = 0.5f;                            // the fight engine's decision quantum

    /// <summary>Auto-detect the input shape: a seed card or fusion record carries a "spell" key
    /// (extract it); anything else is a bare proposal spell. Always gated through the strict
    /// parser+compiler — an invalid spell fails loudly, it is never patched.</summary>
    public static JsonNode LoadSpellNode(string path)
    {
        var o = JsonNode.Parse(File.ReadAllText(path))
                ?? throw new InvalidOperationException($"'{path}' is not JSON.");
        return o["spell"]?.DeepClone() ?? o;
    }

    public static (FightResult Result, ReplayRecorder Recorder) Run(string spellPath, long seed, float maxSeconds)
    {
        var node = LoadSpellNode(spellPath);
        var spell = SpellCompiler.Compile(SpellJson.Parse(node.ToJsonString()));
        float hp = HpBudgetFactor * BalanceTables.TierBudget(spell.Tier);

        var sim = new Sim(seed);
        var caster = sim.State.AddEntity(spell.Id.Value, Faction.Player, hp, new Vec3(0, 0, 0));
        caster.Resources[ResourceKind.Mana] = CasterMana;
        var dummy = sim.State.AddEntity("dummy", Faction.Enemy, hp, new Vec3(DummyDistance, 0, 0));
        // The dummy is INERT: it has no kit, no mana, and no decision loop — it never acts.

        var rec = new ReplayRecorder();
        rec.Init(
            new ReplayEntity(caster.Ref.Value, spell.Id.Value, hp, new Vec3(0, 0, 0)),
            new ReplayEntity(dummy.Ref.Value, "dummy", hp, new Vec3(DummyDistance, 0, 0)));

        // One cast at t=0. Self delivery targets the caster; groundAoE aims at the dummy's position;
        // everything else targets the dummy.
        EntityRef target = default;
        Vec3? point = null;
        switch (spell.Delivery.Type)
        {
            case DeliveryType.Self: target = caster.Ref; break;
            case DeliveryType.GroundAoE: point = sim.State.Get(dummy.Ref).Position; break;
            default: target = dummy.Ref; break;
        }
        sim.Cast(caster.Ref, spell, target, point);
        rec.Mark(sim);

        // Tick to quiescence: past the event-derived effect horizon plus the tail, capped.
        string endReason;
        int maxIters = (int)(maxSeconds / Quantum) + 4; // backstop; the cap check is authoritative
        for (int i = 0; ; i++)
        {
            float now = sim.State.Now;
            if (now >= ActiveUntil(rec) + TailSeconds) { endReason = "quiescent"; break; }
            if (now >= maxSeconds || i >= maxIters) { endReason = "cap"; break; }
            sim.Tick(Quantum);
            rec.Mark(sim);
        }

        var result = new FightResult(
            spell.Id.Value, "dummy", seed, endReason, "draw", sim.State.Now,
            CastsA: 1, CastsB: 0, DistinctVerbs: 0, StatusesApplied: 0, LeadChanges: 0,
            DamageToA: 0, DamageToB: 0,
            HpScale: 0f, HpA: hp, HpB: hp, Mana: CasterMana,
            AmplifyMajor: BalanceTables.Multiplier("major"),
            sim.Events.Projection())
        { TierA = spell.Tier, TierB = spell.Tier };
        return (result, rec);
    }

    /// <summary>The end of every effect the events promised so far. Recomputed each quantum, so a
    /// zone that applies statuses near its end extends the horizon.</summary>
    private static float ActiveUntil(ReplayRecorder rec)
    {
        float until = 0f;
        foreach (var (t, e) in rec.Events)
            until = MathF.Max(until, t + e switch
            {
                StatusApplied { Resisted: false } s => s.Duration,
                ShieldGranted sh => sh.Duration,
                ZoneSpawned z => z.Duration,
                StatModified m => m.Duration,
                _ => 0f
            });
        return until;
    }

    /// <summary>Export through the existing schema-1 writer; showcase metadata rides in ADDITIONAL
    /// header keys, which every schema-1 reader (Unreal included) ignores.</summary>
    public static JsonObject BuildReplay(FightResult r, ReplayRecorder rec, string spellPath, float maxSeconds)
    {
        var json = Replay.Export(r, rec, spellPath, "dummy");
        var h = (JsonObject)json["header"]!;
        h["mode"] = "showcase";
        h["maxSeconds"] = (double)maxSeconds;
        return json;
    }

    /// <summary>replay-verify's live-rerun path for showcase files.</summary>
    public static IReadOnlyList<string> RunLive(string spellPath, long seed, float maxSeconds) =>
        Run(spellPath, seed, maxSeconds).Result.Projection;

    // ── CLI runners ──

    public static int RunShowcase(string spellPath, long seed, float maxSeconds, string replayOut, TextWriter outw)
    {
        var (r, rec) = Run(spellPath, seed, maxSeconds);
        string full = Path.GetFullPath(replayOut);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, BuildReplay(r, rec, spellPath, maxSeconds).ToJsonString(Indented));
        outw.WriteLine($"showcase {r.KitA} (T{r.TierA})  end={r.EndReason}  dur={F(r.DurationSeconds)}s  events={rec.Events.Count}  → {replayOut}");
        return 0;
    }

    public static int RunBatch(string outDir, string coverageDocPath, long seed, TextWriter outw)
    {
        // The full castable canon: seeds + anchors (both seed-card shaped) + fusion records. Filenames
        // are collision-proof — seeds/anchors are uniquely named, fusion records carry a hash suffix.
        var inputs = Directory.GetFiles(Path.Combine(PromptTemplate.RepoRoot(), "fixtures", "seeds"), "*.seed.json")
            .Concat(Directory.GetFiles(Path.Combine(PromptTemplate.RepoRoot(), "fixtures", "anchors"), "*.seed.json"))
            .Concat(Directory.GetFiles(Path.Combine(PromptTemplate.ArenaDir(), "fusions"), "*.record.json"))
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).ToList();
        Directory.CreateDirectory(outDir);

        var manifest = new JsonArray();
        foreach (var path in inputs)
        {
            string id = Path.GetFileName(path).Replace(".seed.json", "").Replace(".record.json", "");
            string outFile = Path.Combine(outDir, id + ".replay.json");
            var (r, rec) = Run(path, seed, DefaultMaxSeconds);
            File.WriteAllText(outFile, BuildReplay(r, rec, RelPath(path), DefaultMaxSeconds).ToJsonString(Indented));
            manifest.Add(ManifestEntry(id, path, r, outFile));
            outw.WriteLine($"showcase {id,-28} T{r.TierA}  end={r.EndReason}  dur={F(r.DurationSeconds)}s  events={rec.Events.Count}");
        }

        string manifestPath = Path.Combine(outDir, "manifest.json");
        File.WriteAllText(manifestPath, new JsonObject { ["spells"] = manifest }.ToJsonString(Indented));
        outw.WriteLine($"manifest: {manifestPath} ({manifest.Count} spells)");

        File.WriteAllText(coverageDocPath, CoverageDoc(inputs));
        outw.WriteLine($"coverage: {coverageDocPath}");
        return 0;
    }

    private static JsonObject ManifestEntry(string id, string inputPath, FightResult r, string outFile)
    {
        var o = JsonNode.Parse(File.ReadAllText(inputPath)) as JsonObject
                ?? throw new InvalidOperationException($"'{inputPath}' is not a JSON object.");
        var meta = o["concept"] as JsonObject ?? o; // fusion record → concept; seed card → top level
        var spell = (o["spell"] ?? o)!;
        var occ = new Occurrences();
        Walk(spell, occ);

        return new JsonObject
        {
            ["id"] = id,
            ["displayName"] = meta["name"]?.GetValue<string>() ?? id,
            ["element"] = meta["element"]?.GetValue<string>() ?? "",
            ["tags"] = new JsonArray(((meta["tags"] as JsonArray)?.Select(t => (JsonNode)t!.GetValue<string>()) ?? Array.Empty<JsonNode>()).ToArray()),
            ["clauseVerbs"] = Arr(occ.Verbs),
            ["deliveryType"] = spell["delivery"]?["type"]?.GetValue<string>() ?? "",
            ["bandsUsed"] = new JsonObject(occ.Bands.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, Arr(kv.Value)))),
            ["file"] = Path.GetFileName(outFile)
        };
    }

    private static JsonArray Arr(IEnumerable<string> xs) =>
        new(xs.OrderBy(x => x, StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray());

    // ── vocabulary occurrence walking (proposal JSON, recursive) ──

    private sealed class Occurrences
    {
        public readonly SortedSet<string> Verbs = new(StringComparer.Ordinal);
        public readonly SortedSet<string> Statuses = new(StringComparer.Ordinal);
        public readonly SortedSet<string> Elements = new(StringComparer.Ordinal);
        public readonly SortedSet<string> Deliveries = new(StringComparer.Ordinal);
        public readonly Dictionary<string, SortedSet<string>> Bands = new(StringComparer.Ordinal);
        public void Band(string family, string? token)
        {
            if (token is null) return;
            if (!Bands.TryGetValue(family, out var set)) Bands[family] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(token);
        }
    }

    // JSON band field → BalanceTables band family (size/range/distance share the size table).
    private static readonly (string Field, string Family)[] BandFields =
    {
        ("castTime", "castTime"), ("cooldown", "cooldown"), ("duration", "duration"),
        ("size", "size"), ("range", "size"), ("distance", "size"),
        ("speed", "speed"), ("tickInterval", "interval"), ("amplify", "multiplier")
    };

    private static void Walk(JsonNode? node, Occurrences occ)
    {
        if (node is not JsonObject o) return;
        if (o["delivery"] is JsonObject del)
        {
            if (del["type"]?.GetValue<string>() is string dt) occ.Deliveries.Add(dt);
            CollectBands(del, occ);
        }
        if (o["cast"] is JsonObject cast)
        {
            CollectBands(cast, occ);
            if (cast["cost"]?["amount"]?.GetValue<string>() is string amt) occ.Band("cost", amt);
        }
        if (o["clauses"] is JsonArray top) foreach (var c in top) WalkClause(c, occ);
    }

    private static void WalkClause(JsonNode? node, Occurrences occ)
    {
        if (node is not JsonObject c) return;
        if (Str(c, "verb") is string v) occ.Verbs.Add(v);
        if (Str(c, "status") is string st) occ.Statuses.Add(st);
        if (Str(c, "element") is string el) occ.Elements.Add(el);
        CollectBands(c, occ);
        foreach (var key in new[] { "tickClauses", "onEnterClauses", "onExitClauses" })
            if (c[key] is JsonArray arr) foreach (var n in arr) WalkClause(n, occ);
        if (c["template"] is JsonObject t)
        {
            if (Str(t, "status") is string ts) occ.Statuses.Add(ts);
            CollectBands(t, occ);
            foreach (var key in new[] { "also", "clauses" })
                if (t[key] is JsonArray arr) foreach (var n in arr) WalkClause(n, occ);
        }
    }

    private static void CollectBands(JsonObject o, Occurrences occ)
    {
        foreach (var (field, family) in BandFields)
            if (Str(o, field) is string tok) occ.Band(family, tok);
    }

    private static string? Str(JsonObject o, string k) =>
        o[k] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    // ── the coverage doc: occurring tokens with exhibiting spells; missing tokens named ──

    public static string CoverageDoc(IReadOnlyList<string> inputPaths)
    {
        // token → spells exhibiting it, per category
        var byCat = new Dictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
        void Add(string cat, string token, string spell)
        {
            if (!byCat.TryGetValue(cat, out var m)) byCat[cat] = m = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            if (!m.TryGetValue(token, out var s)) m[token] = s = new SortedSet<string>(StringComparer.Ordinal);
            s.Add(spell);
        }

        foreach (var path in inputPaths)
        {
            string id = Path.GetFileName(path).Replace(".seed.json", "").Replace(".record.json", "");
            var o = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            var occ = new Occurrences();
            Walk(o["spell"] ?? o, occ);
            foreach (var v in occ.Verbs) Add("verb", v, id);
            foreach (var d in occ.Deliveries) Add("delivery", d, id);
            foreach (var e in occ.Elements) Add("element", e, id);
            foreach (var s in occ.Statuses) Add("status", s, id);
            foreach (var (family, toks) in occ.Bands) foreach (var t in toks) Add("band:" + family, t, id);
        }

        // vocabulary universe (bands reflected from BalanceTables so the universe cannot drift)
        var universe = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal)
        {
            ["verb"] = new(Enum.GetValues<VerbId>().Take(8).Select(Camel), StringComparer.Ordinal),
            ["delivery"] = new(new[] { "self", "targetUnit", "projectile", "groundAoE" }, StringComparer.Ordinal),
            ["element"] = new(Enum.GetValues<Element>().Select(Camel), StringComparer.Ordinal),
            ["status"] = new(Enum.GetValues<StatusId>().Select(Camel), StringComparer.Ordinal),
        };
        foreach (var f in typeof(BalanceTables).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                     .Where(f => f.FieldType == typeof(Dictionary<string, float>)))
        {
            string family = char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1).Replace("Bands", "");
            universe["band:" + family] = new(((Dictionary<string, float>)f.GetValue(null)!).Keys, StringComparer.Ordinal);
        }

        static bool In(string p, string seg) => p.Replace('\\', '/').Contains(seg, StringComparison.Ordinal);
        int seeds = inputPaths.Count(p => In(p, "/fixtures/seeds/"));
        int anchors = inputPaths.Count(p => In(p, "/fixtures/anchors/"));
        int fusions = inputPaths.Count(p => In(p, "/arena/fusions/"));

        var sb = new StringBuilder();
        sb.AppendLine("# Vocabulary coverage (generated by `showcase-batch`; do not hand-edit)");
        sb.AppendLine();
        sb.AppendLine($"Corpus: the {inputPaths.Count} showcased spells ({seeds} seeds + {anchors} anchors + {fusions} fusions) — the full castable canon. Every verb, delivery, element, status, and band value occurring in the corpus, with the spells that exhibit it. Band families are reflected from `BalanceTables`.");
        foreach (var (cat, tokens) in universe)
        {
            sb.AppendLine();
            sb.AppendLine($"## {cat}");
            sb.AppendLine();
            sb.AppendLine("| token | spells |");
            sb.AppendLine("|---|---|");
            var occ = byCat.GetValueOrDefault(cat);
            foreach (var tok in tokens)
            {
                var spells = occ?.GetValueOrDefault(tok);
                if (spells is { Count: > 0 })
                    sb.AppendLine($"| {tok} | {string.Join(", ", spells)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Missing tokens (occur NOWHERE in the corpus — grammar-spec items with no test material)");
        sb.AppendLine();
        bool any = false;
        foreach (var (cat, tokens) in universe)
            foreach (var tok in tokens)
                if (!(byCat.GetValueOrDefault(cat)?.ContainsKey(tok) ?? false))
                {
                    sb.AppendLine($"- {cat}: **{tok}**");
                    any = true;
                }
        if (!any) sb.AppendLine("(none — full vocabulary coverage)");
        return sb.ToString();
    }

    private static string Camel<T>(T v) where T : notnull
    {
        string n = v.ToString()!;
        return char.ToLowerInvariant(n[0]) + n.Substring(1);
    }

    private static string RelPath(string p)
    {
        try { return Path.GetRelativePath(PromptTemplate.RepoRoot(), p).Replace('\\', '/'); }
        catch { return p.Replace('\\', '/'); }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static string F(float x) => x.ToString("0.0", CultureInfo.InvariantCulture);
}
