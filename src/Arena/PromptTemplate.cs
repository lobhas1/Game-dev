using System.Globalization;

namespace Arena;

/// <summary>Loads and renders the single-authority oracle prompt, and locates repo files by walking
/// up from the executable directory (the Fixtures.cs pattern).</summary>
public static class PromptTemplate
{
    public static string Render(string template, string brief, int tier, int kitSize) =>
        template
            .Replace("{{BRIEF}}", brief)
            .Replace("{{TIER}}", tier.ToString(CultureInfo.InvariantCulture))
            .Replace("{{KIT_SIZE}}", kitSize.ToString(CultureInfo.InvariantCulture));

    public static string LoadPrompt() => File.ReadAllText(LocateRepoFile("prompts/proposal-oracle.md"));

    /// <summary>Repo root (the directory holding Spellcraft.sln), found by walking up from the
    /// executable. Falls back to the current directory.</summary>
    public static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Spellcraft.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>The experiment output directory, always &lt;repo-root&gt;/arena.</summary>
    public static string ArenaDir() => Path.Combine(RepoRoot(), "arena");

    public static string LocateRepoFile(string relativePath)
    {
        string relative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException($"'{relativePath}' not found walking up from {AppContext.BaseDirectory}.");
    }
}
