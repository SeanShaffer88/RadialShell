using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadialShell.App;

internal sealed record RadialTheme(
    string Name,
    Color Window,
    Color Hub,
    Color Sector,
    Color SectorAlternate,
    Color Hover,
    Color Accent,
    Color Text,
    Color Muted,
    Color Border);

internal static class RadialThemeCatalog
{
    public static IReadOnlyList<RadialTheme> All { get; } =
    [
        new("Midnight", Color.FromArgb(12, 18, 32), Color.FromArgb(18, 28, 48), Color.FromArgb(27, 40, 64), Color.FromArgb(33, 49, 75), Color.FromArgb(49, 78, 108), Color.FromArgb(62, 218, 235), Color.FromArgb(243, 248, 255), Color.FromArgb(158, 179, 204), Color.FromArgb(71, 102, 132)),
        new("Neon Circuit", Color.FromArgb(15, 10, 28), Color.FromArgb(27, 18, 45), Color.FromArgb(39, 27, 61), Color.FromArgb(49, 32, 73), Color.FromArgb(70, 47, 92), Color.FromArgb(190, 255, 68), Color.FromArgb(249, 246, 255), Color.FromArgb(184, 168, 207), Color.FromArgb(112, 80, 156)),
        new("Frost", Color.FromArgb(226, 239, 250), Color.FromArgb(246, 251, 255), Color.FromArgb(255, 255, 255), Color.FromArgb(235, 245, 253), Color.FromArgb(208, 231, 248), Color.FromArgb(39, 132, 210), Color.FromArgb(25, 47, 68), Color.FromArgb(91, 119, 143), Color.FromArgb(169, 199, 222)),
        new("Sunset", Color.FromArgb(34, 18, 35), Color.FromArgb(48, 26, 46), Color.FromArgb(70, 35, 54), Color.FromArgb(86, 42, 58), Color.FromArgb(112, 49, 59), Color.FromArgb(255, 143, 103), Color.FromArgb(255, 245, 239), Color.FromArgb(211, 174, 174), Color.FromArgb(143, 77, 83)),
        new("Emerald", Color.FromArgb(11, 29, 27), Color.FromArgb(16, 42, 38), Color.FromArgb(24, 57, 49), Color.FromArgb(30, 68, 57), Color.FromArgb(43, 91, 69), Color.FromArgb(91, 226, 163), Color.FromArgb(238, 255, 247), Color.FromArgb(156, 195, 178), Color.FromArgb(57, 112, 89)),
        new("Paper", Color.FromArgb(245, 241, 232), Color.FromArgb(255, 253, 248), Color.FromArgb(255, 255, 255), Color.FromArgb(242, 239, 231), Color.FromArgb(232, 223, 252), Color.FromArgb(112, 75, 194), Color.FromArgb(40, 35, 49), Color.FromArgb(119, 111, 126), Color.FromArgb(208, 199, 218)),
        new("Ocean", Color.FromArgb(8, 29, 48), Color.FromArgb(12, 43, 66), Color.FromArgb(18, 57, 81), Color.FromArgb(22, 68, 92), Color.FromArgb(30, 90, 117), Color.FromArgb(72, 205, 255), Color.FromArgb(237, 250, 255), Color.FromArgb(151, 190, 211), Color.FromArgb(49, 116, 147)),
        new("Orchid", Color.FromArgb(30, 16, 38), Color.FromArgb(45, 25, 56), Color.FromArgb(64, 35, 78), Color.FromArgb(76, 43, 91), Color.FromArgb(99, 53, 112), Color.FromArgb(238, 145, 255), Color.FromArgb(253, 243, 255), Color.FromArgb(204, 173, 214), Color.FromArgb(126, 77, 142))
    ];

    public static RadialTheme Default => All[0];

    public static bool TryFind(string? name, out RadialTheme theme)
    {
        theme = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return theme is not null;
    }
}

internal static class RadialThemeSettings
{
    private const int CurrentSchemaVersion = 1;
    private const long MaxSettingsBytes = 64 * 1024;

    private sealed record Preferences(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("theme")] string Theme);

    private static readonly string SettingsPath = Path.Combine(
        AppContext.BaseDirectory, "data", "settings.json");

    public static RadialTheme Load()
    {
        if (!File.Exists(SettingsPath))
            return RadialThemeCatalog.Default;

        try
        {
            var settingsBytes = File.ReadAllBytes(SettingsPath);
            if (settingsBytes.LongLength > MaxSettingsBytes)
                throw new InvalidDataException($"Settings file exceeds {MaxSettingsBytes} bytes.");

            using var document = JsonDocument.Parse(settingsBytes, new JsonDocumentOptions
            {
                MaxDepth = 8,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });

            if (!TryReadSettings(document.RootElement, out var theme, out var requiresMigration, out var error))
                throw new InvalidDataException(error);

            if (requiresMigration && !Save(theme!.Name, out var migrationError))
                Console.Error.WriteLine($"Theme settings migration was not saved: {migrationError} Existing settings were left unchanged.");

            return theme!;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Could not load theme settings: {ex.GetBaseException().Message} Existing settings were left unchanged; using the built-in default.");
            return RadialThemeCatalog.Default;
        }
    }

    internal static bool TryReadSettings(
        JsonElement root,
        out RadialTheme? theme,
        out bool requiresMigration,
        out string? error)
    {
        theme = null;
        requiresMigration = false;
        error = null;

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "The settings root must be a JSON object.";
            return false;
        }

        string? themeName;
        if (!root.TryGetProperty("schema_version", out var schemaElement))
        {
            // Version 0 was the original { "Theme": "..." } file written beside
            // the executable. Preserve its value by migrating it explicitly to v1.
            if (!TryGetStringProperty(root, "Theme", out themeName) &&
                !TryGetStringProperty(root, "theme", out themeName))
            {
                error = "Unversioned settings must contain a string Theme value.";
                return false;
            }
            requiresMigration = true;
        }
        else
        {
            if (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out var version))
            {
                error = "schema_version must be an integer.";
                return false;
            }
            if (version != CurrentSchemaVersion)
            {
                error = $"Settings schema version {version} is not supported; this build supports version {CurrentSchemaVersion}.";
                return false;
            }
            if (!TryGetStringProperty(root, "theme", out themeName))
            {
                error = "Version 1 settings must contain a string theme value.";
                return false;
            }
        }

        if (!RadialThemeCatalog.TryFind(themeName, out var resolvedTheme))
        {
            error = $"Unknown theme '{themeName}'.";
            return false;
        }

        theme = resolvedTheme;
        return true;
    }

    private static bool TryGetStringProperty(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    public static bool Save(string themeName, out string? error)
    {
        error = null;
        if (!RadialThemeCatalog.TryFind(themeName, out var theme))
        {
            error = $"Unknown theme '{themeName}'.";
            return false;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    new Preferences(CurrentSchemaVersion, theme.Name),
                    new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(SettingsPath))
                File.Replace(temporaryPath, SettingsPath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, SettingsPath);
            temporaryPath = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not save theme settings: {ex.Message}";
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch { }
            }
        }
    }
}
