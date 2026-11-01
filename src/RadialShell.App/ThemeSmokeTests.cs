namespace RadialShell.App;

internal static class ThemeSmokeTests
{
    public static void Run()
    {
        var themes = RadialThemeCatalog.All;
        if (themes.Count < 6)
            throw new InvalidOperationException("Expected a useful selection of built-in themes.");
        if (themes.Select(theme => theme.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != themes.Count)
            throw new InvalidOperationException("Theme names must be unique, ignoring case.");

        foreach (var theme in themes)
        {
            if (!RadialThemeCatalog.TryFind(theme.Name.ToUpperInvariant(), out var resolved) || resolved != theme)
                throw new InvalidOperationException($"Theme lookup failed for {theme.Name}.");
            if (theme.Text.ToArgb() == theme.Sector.ToArgb() || theme.Accent.ToArgb() == theme.Window.ToArgb())
                throw new InvalidOperationException($"Theme {theme.Name} has indistinguishable foreground/surface colors.");
            Console.WriteLine($"  PASS: {theme.Name} theme is registered");
        }

        if (RadialThemeCatalog.TryFind("not-a-theme", out _))
            throw new InvalidOperationException("An unknown theme unexpectedly resolved.");

        Console.WriteLine($"All {themes.Count} theme checks passed.");
    }
}
