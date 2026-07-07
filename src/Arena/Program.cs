using System.Globalization;
using Arena;

if (args.Length == 0) { PrintUsage(); return 2; }

try
{
    switch (args[0])
    {
        case "generate": return await RunGenerate(args);
        case "fight": return RunFight(args);
        case "tournament": return RunTournament(args);
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
    await KitGenerator.RunGenerateAsync(oracle, brief, tier, kitSize, count, outDir, Console.Out);
    return 0;
}

static int RunFight(string[] args)
{
    if (args.Length < 3) { Console.Error.WriteLine("usage: fight <kitA.json> <kitB.json> --seed S"); return 2; }
    var a = Kit.Load(args[1]);
    var b = Kit.Load(args[2]);
    long seed = long.Parse(Opt(args, "--seed") ?? "1", CultureInfo.InvariantCulture);

    var r = FightEngine.Run(a, b, seed);
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
    return 0;
}

static int RunTournament(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: tournament <kitsDir> --seeds A..B"); return 2; }
    var seeds = ParseSeeds(Opt(args, "--seeds") ?? "1..5");
    Tournament.Run(args[1], seeds, Console.Out);
    return 0;
}

static string? Opt(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
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
    Console.WriteLine("  fight <kitA.json> <kitB.json> --seed S");
    Console.WriteLine("  tournament <kitsDir> --seeds 1..5");
}
