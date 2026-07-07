namespace Arena;

/// <summary>Round-robin over kit pairs — both orderings per pair (first-mover advantage is
/// decisive), every seed. Shared by tournament and sweep.</summary>
public static class Rounds
{
    public static List<FightResult> RoundRobin(
        IReadOnlyList<Kit> kits, IEnumerable<long> seeds, FightConfig config, bool sameTierOnly)
    {
        var seedList = seeds.ToList();
        var results = new List<FightResult>();
        for (int i = 0; i < kits.Count; i++)
            for (int j = i + 1; j < kits.Count; j++)
            {
                if (sameTierOnly && kits[i].Tier != kits[j].Tier) continue;
                foreach (var s in seedList)
                {
                    results.Add(FightEngine.Run(kits[i], kits[j], s, config));
                    results.Add(FightEngine.Run(kits[j], kits[i], s, config));
                }
            }
        return results;
    }
}
