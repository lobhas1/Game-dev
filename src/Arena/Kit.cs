using System.Text.Json.Nodes;
using Spellcraft;

namespace Arena;

/// <summary>A named list of compiled spells. A kit file is a JSON array of proposal-stage spell
/// objects; each is strictly parsed (the sole gate) and compiled.</summary>
public sealed class Kit
{
    public string Name { get; }
    public IReadOnlyList<Spell> Spells { get; }

    /// <summary>The kit's tier (kits are generated at a single tier; max is a safe fold).</summary>
    public int Tier => Spells.Count == 0 ? 1 : Spells.Max(s => s.Tier);

    public Kit(string name, IReadOnlyList<Spell> spells)
    {
        Name = name;
        Spells = spells;
    }

    public static Kit Load(string path, BalanceOverrides? overrides = null)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var arr = JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                  ?? throw new InvalidOperationException($"Kit '{path}' must be a JSON array of proposal spells.");
        var spells = new List<Spell>();
        foreach (var el in arr)
        {
            if (el is null) continue;
            spells.Add(SpellCompiler.Compile(SpellJson.Parse(el.ToJsonString()), overrides));
        }
        if (spells.Count == 0)
            throw new InvalidOperationException($"Kit '{path}' contained no spells.");
        return new Kit(name, spells);
    }
}
