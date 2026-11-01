using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using RadialShell.Core;

namespace RadialShell.App;

internal static class RadialActionCatalog
{
    public static IReadOnlyList<string> Labels { get; } =
    [
        "Copy\npath", "Copy\nname", "Open\nfolder",
        "Terminal\nhere", "POSIX\npath", "Copy\ncontents"
    ];

    public static IReadOnlyList<string> Names { get; } =
    ["Copy Path", "Copy Name", "Open Folder", "Terminal Here", "POSIX Path", "Copy Contents"];
}

/// <summary>Runs the six built-in actions after an explicit wheel-slice click.</summary>
internal static class RadialActionExecutor
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".log", ".xml", ".yaml", ".yml", ".ini", ".toml"
    };
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const long MaxCombinedBytes = 5 * 1024 * 1024;

    public static string Execute(int actionIndex, ShellTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var paths = GetPaths(target);
        if (paths.Count == 0)
            throw new InvalidOperationException("The resolved target has no filesystem path.");

        return actionIndex switch
        {
            0 => Copy(string.Join(Environment.NewLine, paths), $"Copied {paths.Count} path(s)."),
            1 => Copy(string.Join(Environment.NewLine, paths.Select(GetName)), $"Copied {paths.Count} name(s)."),
            2 => OpenInExplorer(target, paths),
            3 => OpenTerminal(target, paths),
            4 => Copy(string.Join(Environment.NewLine, paths.Select(ToPosixPath)), $"Copied {paths.Count} POSIX path(s)."),
            5 => CopyContents(paths),
            _ => throw new ArgumentOutOfRangeException(nameof(actionIndex), "Unknown radial action.")
        };
    }

    private static List<string> GetPaths(ShellTarget target)
    {
        if (target.Kind == ShellTargetKind.Folder)
            return string.IsNullOrWhiteSpace(target.FolderPath) ? [] : new List<string> { target.FolderPath };
        return target.Items.Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
    }

    private static string Copy(string text, string result)
    {
        Clipboard.SetText(text, TextDataFormat.UnicodeText);
        return result;
    }

    internal static string GetName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    internal static string ToPosixPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/')
            return $"/{char.ToLowerInvariant(normalized[0])}/{normalized[3..]}";
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            return normalized;
        return normalized;
    }

    private static string OpenInExplorer(ShellTarget target, IReadOnlyList<string> paths)
    {
        var path = paths[0];
        var isDirectory = target.Kind == ShellTargetKind.Folder ||
                          target.Items.FirstOrDefault()?.IsDirectory == true || Directory.Exists(path);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        if (isDirectory)
            start.ArgumentList.Add(path);
        else
            start.Arguments = $"/select,\"{path}\"";
        Process.Start(start);
        return isDirectory ? "Opened folder in Explorer." : "Selected item in Explorer.";
    }

    private static string OpenTerminal(ShellTarget target, IReadOnlyList<string> paths)
    {
        var path = paths[0];
        var isDirectory = target.Kind == ShellTargetKind.Folder ||
                          target.Items.FirstOrDefault()?.IsDirectory == true || Directory.Exists(path);
        var directory = isDirectory ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new InvalidOperationException("The target has no accessible parent folder.");

        try
        {
            var terminal = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            terminal.ArgumentList.Add("-d");
            terminal.ArgumentList.Add(directory);
            Process.Start(terminal);
        }
        catch (Win32Exception)
        {
            // Keep the action useful on Windows installations without Windows Terminal.
            var commandPrompt = new ProcessStartInfo(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe")
            {
                UseShellExecute = true,
                WorkingDirectory = directory,
                Arguments = "/K"
            };
            Process.Start(commandPrompt);
        }

        return "Opened terminal in target folder.";
    }

    private static string CopyContents(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException("Copy Contents needs one or more files, not a folder target.");

        long totalBytes = 0;
        var output = new StringBuilder();
        foreach (var path in files)
        {
            var extension = Path.GetExtension(path);
            if (!TextExtensions.Contains(extension))
                throw new InvalidOperationException($"'{extension}' is not in the text-file allowlist.");

            var size = new FileInfo(path).Length;
            if (size > MaxFileBytes || totalBytes + size > MaxCombinedBytes)
                throw new InvalidOperationException("Text copy is limited to 2 MiB per file and 5 MiB total.");
            totalBytes += size;

            if (files.Length > 1)
                output.AppendLine($"===== {Path.GetFileName(path)} =====");
            output.AppendLine(File.ReadAllText(path, Encoding.UTF8));
        }

        return Copy(output.ToString(), $"Copied contents from {files.Length} text file(s).");
    }
}
