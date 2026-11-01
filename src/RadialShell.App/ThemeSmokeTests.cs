using System.Text.Json;

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

        VerifySettings("{\"Theme\":\"Ocean\"}", "Ocean", migrationExpected: true);
        VerifySettings("{\"schema_version\":1,\"theme\":\"Frost\"}", "Frost", migrationExpected: false);
        VerifyRejectedSettings("{\"schema_version\":99,\"theme\":\"Ocean\"}", "unsupported future schema");
        VerifyRejectedSettings("{\"schema_version\":1,\"theme\":\"Unknown\"}", "unknown saved theme");

        Console.WriteLine($"All {themes.Count} theme checks passed.");
    }

    private static void VerifySettings(string json, string expectedTheme, bool migrationExpected)
    {
        using var document = JsonDocument.Parse(json);
        if (!RadialThemeSettings.TryReadSettings(document.RootElement, out var theme, out var requiresMigration, out var error) ||
            theme?.Name != expectedTheme || requiresMigration != migrationExpected)
            throw new InvalidOperationException($"Theme settings validation failed for {expectedTheme}: {error}");
        Console.WriteLine($"  PASS: {expectedTheme} settings parsed (migration={requiresMigration})");
    }

    private static void VerifyRejectedSettings(string json, string description)
    {
        using var document = JsonDocument.Parse(json);
        if (RadialThemeSettings.TryReadSettings(document.RootElement, out _, out _, out var error))
            throw new InvalidOperationException($"Invalid settings were accepted: {description}.");
        if (string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"Rejected settings had no diagnostic: {description}.");
        Console.WriteLine($"  PASS: {description} rejected without rewriting");
    }
}
