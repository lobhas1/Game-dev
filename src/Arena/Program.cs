using System.Globalization;
using System.Text.Json;
using Arena;

if (args.Length == 0) { PrintUsage(); return 2; }

try
{
    switch (args[0])
    {
        case "generate": return await RunGenerate(args);
        case "fight": return RunFight(args);
        case "tournament": return RunTournament(args);
        case "sweep": return RunSweep(args);
        case "evaluate": return RunEvaluate(args);
        case "fuse": return await RunFuse(args);
        case "fuse-batch": return await RunFuseBatch(args);
        case "evaluate-fusions": return RunEvaluateFusions(args);
        case "quiz": return RunQuiz(args);
        case "record-f3": return RunRecordF3(args);
        case "flavor-eval": return RunFlavorEval(args);
        case "replay-verify": return RunReplayVerify(args);
        case "showcase": return RunShowcase(args);
        case "showcase-batch": return RunShowcaseBatch(args);
        default: PrintUsage(); return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 1;
}

static async Task<int> RunGenerate(string[] args)
{
    string brief = Opt(args, "--brief") ?? throw new ArgumentException("generate requires --brief \"…\"");
    int tier = int.Parse(Opt(args, "--tier") ?? "1", CultureInfo.InvariantCulture);
    int kitSize = int.Parse(Opt(args, "--kit-size") ?? "3", CultureInfo.InvariantCulture);
    int count = int.Parse(Opt(args, "--count") ?? "1", CultureInfo.InvariantCulture);
    string model = Opt(args, "--model") ?? "claude-sonnet-4-6";
    string outDir = Opt(args, "--out") ?? Path.Combine(PromptTemplate.ArenaDir(), "kits");

    IOracle oracle = new LiveAnthropicOracle(model); // reads ANTHROPIC_API_KEY; throws if unset
    await KitGenerator.RunGenerateAsync(oracle, model, brief, tier, kitSize, count, outDir, Console.Out);
    return 0;
}

static int RunEvaluate(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: evaluate <kitsDir>"); return 2; }
    return Evaluator.RunEvaluate(args[1], Console.Out);
}

// ── Phase B: fusion pipeline modes ──

static async Task<int> RunFuse(string[] args)
{
    if (args.Length < 3) { Console.Error.WriteLine("usage: fuse <parentA.json> <parentB.json> [--tier auto|N] [--stub-responses f] [--arena dir]"); return 2; }
    int? tier = ParseTier(Opt(args, "--tier"));
    string model = Opt(args, "--model") ?? "claude-sonnet-4-6";
    string arena = Opt(args, "--arena") ?? PromptTemplate.ArenaDir();
    return await FusionPipeline.RunFuse(args[1], args[2], tier, model, Opt(args, "--stub-responses"), arena, Console.Out);
}

static async Task<int> RunFuseBatch(string[] args)
{
    string pairs = Opt(args, "--pairs-file") ?? throw new ArgumentException("fuse-batch requires --pairs-file <f>");
    string model = Opt(args, "--model") ?? "claude-sonnet-4-6";
    string arena = Opt(args, "--arena") ?? PromptTemplate.ArenaDir();
    return await FusionPipeline.RunFuseBatch(pairs, model, Opt(args, "--stub-responses"), arena, Console.Out);
}

static int RunEvaluateFusions(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: evaluate-fusions <fusionsDir>"); return 2; }
    return FusionEvaluator.RunEvaluate(args[1], Console.Out);
}

static int RunQuiz(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: quiz <fusionsDir> --seed S [--same-element-decoys]"); return 2; }
    long seed = long.Parse(Opt(args, "--seed") ?? "1", CultureInfo.InvariantCulture);
    return FusionQuiz.RunQuiz(args[1], seed, HasFlag(args, "--same-element-decoys"), Console.Out);
}

static int RunFlavorEval(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: flavor-eval <fusionsDir>"); return 2; }
    return FlavorEval.RunEvaluate(args[1], Console.Out);
}

static int RunRecordF3(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: record-f3 <verdictDoc> --score n/20"); return 2; }
    string score = Opt(args, "--score") ?? throw new ArgumentException("record-f3 requires --score n/20");
    return FusionEvaluator.RunRecordF3(args[1], score, Console.Out);
}

static int? ParseTier(string? s) => s is null || s == "auto" ? null : int.Parse(s, CultureInfo.InvariantCulture);

static int RunFight(string[] args)
{
    if (args.Length < 3) { Console.Error.WriteLine("usage: fight <kitA.json> <kitB.json> --seed S [--hp-scale k] [--mana N] [--amplify-major f] [--replay-out file.json]"); return 2; }
    var config = new FightConfig
    {
        HpScale = float.Parse(Opt(args, "--hp-scale") ?? "0", CultureInfo.InvariantCulture),
        Mana = float.Parse(Opt(args, "--mana") ?? "250", CultureInfo.InvariantCulture),
        AmplifyMajor = OptFloatNullable(args, "--amplify-major")
    };
    var a = Kit.Load(args[1], config.Overrides);
    var b = Kit.Load(args[2], config.Overrides);
    long seed = long.Parse(Opt(args, "--seed") ?? "1", CultureInfo.InvariantCulture);
    string? replayOut = Opt(args, "--replay-out");
    var recorder = replayOut is null ? null : new ReplayRecorder();

    var r = FightEngine.Run(a, b, seed, config, recorder);
    Console.WriteLine($"=== FIGHT  {a.Name} (E1) vs {b.Name} (E2)  seed={seed} ===");
    foreach (var line in r.Projection) Console.WriteLine("  " + line);
    Console.WriteLine();
    Console.WriteLine("--- summary ---");
    Console.WriteLine($"winner:          {r.Winner}");
    Console.WriteLine($"end reason:      {r.EndReason}");
    Console.WriteLine($"duration:        {r.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s");
    Console.WriteLine($"casts:           {r.KitA}={r.CastsA}  {r.KitB}={r.CastsB}");
    Console.WriteLine($"distinct verbs:  {r.DistinctVerbs}");
    Console.WriteLine($"statuses applied:{r.StatusesApplied}");
    Console.WriteLine($"lead changes:    {r.LeadChanges}");
    Console.WriteLine($"damage taken:    {r.KitA}={r.DamageToA.ToString("0.0", CultureInfo.InvariantCulture)}  {r.KitB}={r.DamageToB.ToString("0.0", CultureInfo.InvariantCulture)}");

    if (replayOut is not null)
    {
        var json = Replay.Export(r, recorder!, args[1], args[2]);
        string full = Path.GetFullPath(replayOut);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"replay:          {replayOut} ({recorder!.Events.Count} events, schema {Replay.SchemaVersion})");
    }
    return 0;
}

static int RunReplayVerify(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: replay-verify <file.json> [--diff-against-live]"); return 2; }
    return Replay.RunVerify(args[1], HasFlag(args, "--diff-against-live"), Console.Out);
}

// ── M2: showcase modes ──

static int RunShowcase(string[] args)
{
    string? replayOut = Opt(args, "--replay-out");
    if (args.Length < 2 || replayOut is null)
    { Console.Error.WriteLine("usage: showcase <spellfile> --seed N --replay-out <file.json> [--max-seconds S]"); return 2; }
    long seed = long.Parse(Opt(args, "--seed") ?? "1", CultureInfo.InvariantCulture);
    float maxSeconds = float.Parse(Opt(args, "--max-seconds") ?? Showcase.DefaultMaxSeconds.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    return Showcase.RunShowcase(args[1], seed, maxSeconds, replayOut, Console.Out);
}

static int RunShowcaseBatch(string[] args)
{
    string outDir = Opt(args, "--out") ?? Path.Combine(PromptTemplate.ArenaDir(), "showcases");
    string coverageDoc = Opt(args, "--coverage-doc")
        ?? Path.Combine(PromptTemplate.RepoRoot(), "docs", "specs", "2026-07-11-m2-vocabulary-coverage.md");
    long seed = long.Parse(Opt(args, "--seed") ?? "1", CultureInfo.InvariantCulture);
    return Showcase.RunBatch(outDir, coverageDoc, seed, Console.Out);
}

static int RunTournament(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: tournament <kitsDir> [--same-tier] [--hp-scale k] [--mana N] --seeds A..B"); return 2; }
    var seeds = ParseSeeds(Opt(args, "--seeds") ?? "1..5");
    var config = new FightConfig
    {
        HpScale = float.Parse(Opt(args, "--hp-scale") ?? "0", CultureInfo.InvariantCulture),
        Mana = float.Parse(Opt(args, "--mana") ?? "250", CultureInfo.InvariantCulture),
        AmplifyMajor = OptFloatNullable(args, "--amplify-major")
    };
    Tournament.Run(args[1], seeds, config, HasFlag(args, "--same-tier"), Console.Out);
    return 0;
}

static int RunSweep(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: sweep <kitsDir> --seeds A..B [--hp-scales 3,4,5,6,8 | --hp-scale 8 --amplify-majors 2.5,2.25,2.0,1.75,1.5] [--mana N]"); return 2; }
    var seeds = ParseSeeds(Opt(args, "--seeds") ?? "1..5");
    float mana = float.Parse(Opt(args, "--mana") ?? "250", CultureInfo.InvariantCulture);

    // v4 amplify axis: fixed --hp-scale, vary --amplify-majors. Otherwise the v3 hpScale axis.
    string? amplifyList = Opt(args, "--amplify-majors");
    if (amplifyList is not null)
    {
        float hpScale = float.Parse(Opt(args, "--hp-scale") ?? "8", CultureInfo.InvariantCulture);
        var amps = amplifyList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => float.Parse(s, CultureInfo.InvariantCulture));
        Sweep.RunAmplify(args[1], seeds, hpScale, mana, amps, Console.Out);
        return 0;
    }

    var scales = (Opt(args, "--hp-scales") ?? "3,4,5,6,8")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => float.Parse(s, CultureInfo.InvariantCulture));
    Sweep.Run(args[1], seeds, scales, mana, Console.Out);
    return 0;
}

static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

static string? Opt(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

// null when the flag is absent — so FightConfig stays at the engine default (bit-for-bit today).
static float? OptFloatNullable(string[] args, string name)
{
    var s = Opt(args, name);
    return s is null ? null : float.Parse(s, CultureInfo.InvariantCulture);
}

static IEnumerable<long> ParseSeeds(string spec)
{
    var range = spec.Split("..", StringSplitOptions.RemoveEmptyEntries);
    if (spec.Contains("..") && range.Length == 2
        && long.TryParse(range[0], out var a) && long.TryParse(range[1], out var b))
    {
        for (long s = a; s <= b; s++) yield return s;
    }
    else
    {
        foreach (var p in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (long.TryParse(p.Trim(), out var v)) yield return v;
    }
}

static void PrintUsage()
{
    Console.WriteLine("Arena — the oracle → sim bridge");
    Console.WriteLine("  generate --brief \"…\" --tier N --kit-size 3 --count M [--model claude-sonnet-4-6] [--out arena/kits/]");
    Console.WriteLine("  fight <kitA.json> <kitB.json> --seed S [--hp-scale k] [--mana N] [--amplify-major f] [--replay-out file.json]");
    Console.WriteLine("  replay-verify <file.json> [--diff-against-live]");
    Console.WriteLine("  showcase <spellfile> --seed N --replay-out <file.json> [--max-seconds S]");
    Console.WriteLine("  showcase-batch [--out arena/showcases] [--seed N]");
    Console.WriteLine("  tournament <kitsDir> [--same-tier] [--hp-scale k] [--mana N] [--amplify-major f] --seeds 1..5");
    Console.WriteLine("  sweep <kitsDir> --seeds 1..5 [--hp-scales 3,4,5,6,8 | --hp-scale 8 --amplify-majors 2.5,2.25,2.0,1.75,1.5] [--mana N]");
    Console.WriteLine("  evaluate <kitsDir>");
    Console.WriteLine("  fuse <parentA.json> <parentB.json> [--tier auto|N] [--stub-responses f] [--arena dir]");
    Console.WriteLine("  fuse-batch --pairs-file <f> [--stub-responses f] [--arena dir]");
    Console.WriteLine("  evaluate-fusions <fusionsDir>");
    Console.WriteLine("  quiz <fusionsDir> --seed S [--same-element-decoys]");
    Console.WriteLine("  record-f3 <verdictDoc> --score n/20");
    Console.WriteLine("  flavor-eval <fusionsDir>");
}
