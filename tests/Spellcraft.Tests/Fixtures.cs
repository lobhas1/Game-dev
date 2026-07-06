namespace Spellcraft.Tests;

// Loads a conformance fixture by walking up from the test bin/ to the repo's fixtures/.
public static class Fixtures
{
    public static string Load(string name)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "fixtures", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException($"Fixture '{name}' not found walking up from {AppContext.BaseDirectory}.");
    }
}
