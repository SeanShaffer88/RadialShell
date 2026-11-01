using System.Runtime.InteropServices;
using System.Windows.Forms;
using RadialShell.Core;

namespace RadialShell.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var trayLaunch = IsTrayLaunch(args);
        if (!trayLaunch)
            AttachConsoleForCommandLineMode();

        if (Has(args, "--help") || Has(args, "-h"))
        {
            PrintHelp();
            return 0;
        }

        if (!TryGetLogLevel(args, out var logLevel, out var logLevelError))
        {
            Console.Error.WriteLine(logLevelError);
            return 2;
        }

        using var logging = RadialLogger.Initialize(logLevel);

        if (Has(args, "--self-test"))
        {
            TargetPolicySmokeTests.Run();
            ThemeSmokeTests.Run();
            RadialActionSmokeTests.Run();
            RadialLoggerSmokeTests.Run();
            var threadId = MouseGestureListener.CurrentNativeThreadIdForTest();
            if (threadId == 0)
                throw new InvalidOperationException("Win32 GetCurrentThreadId returned zero.");
            Console.WriteLine("PASS: Win32 GetCurrentThreadId entry point is available.");
            return 0;
        }

        if (Has(args, "--themes"))
        {
            PrintThemes();
            return 0;
        }

        var themeArg = IndexOf(args, "--theme");
        if (themeArg >= 0)
        {
            if (themeArg + 1 >= args.Length)
            {
                RadialLogger.Error("theme.argument.invalid", "Missing theme name after --theme.");
                PrintThemes();
                return 2;
            }

            var requested = args[themeArg + 1];
            if (!RadialThemeCatalog.TryFind(requested, out var theme))
            {
                RadialLogger.Error("theme.not_found", $"Unknown theme '{requested}'.");
                PrintThemes();
                return 2;
            }

            if (!RadialThemeSettings.Save(theme.Name, out var error))
            {
                RadialLogger.Error("theme.save.failed", error ?? "Could not save theme settings.");
                return 1;
            }

            RadialLogger.Info("theme.saved", $"Theme preference saved: {theme.Name}");
            return 0;
        }

        if (Has(args, "--preview"))
        {
            var previewTheme = RadialThemeSettings.Load();
            var previewArg = IndexOf(args, "--preview");
            if (previewArg + 1 < args.Length && !args[previewArg + 1].StartsWith("--", StringComparison.Ordinal))
            {
                if (!RadialThemeCatalog.TryFind(args[previewArg + 1], out previewTheme))
                {
                    RadialLogger.Error("theme.not_found", $"Unknown theme '{args[previewArg + 1]}'.");
                    PrintThemes();
                    return 2;
                }
            }

            InitializeWinForms();
            Application.Run(new RadialMenuPreviewForm(previewTheme));
            return 0;
        }

        if (Has(args, "--listen"))
        {
            RunListener();
            return 0;
        }

        if (trayLaunch)
        {
            RunTrayApplication();
            return 0;
        }

        PrintHelp();
        return 0;
    }

    private static void RunTrayApplication()
    {
        InitializeWinForms();
        using var context = new RadialShellApplicationContext();
        try
        {
            context.Start();
            RadialLogger.Info("app.started", "RadialShell tray application started disabled.");
            Application.Run(context);
        }
        catch (Exception ex)
        {
            RadialLogger.Error("app.start.failed", $"Could not start RadialShell: {ex.GetBaseException().Message}");
        }
    }

    private static void InitializeWinForms()
    {
        _ = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    private static void RunListener()
    {
        RadialLogger.Info("listener.started", "RadialShell diagnostic listener started.");
        Console.WriteLine("RadialShell diagnostic listener is active.");
        Console.WriteLine("Alt + Right-Click an item or blank area in File Explorer/Desktop.");
        Console.WriteLine("This diagnostic observes the gesture only; normal Windows behavior is preserved.");
        Console.WriteLine("Press Enter to exit.\n");

        var resolver = new ShellTargetResolver();
        using var listener = new MouseGestureListener(point =>
        {
            RadialLogger.Info("gesture.detected", $"Alt+right-click at ({point.X}, {point.Y}).");
            if (!resolver.TryResolve(point, out var target, out var message))
            {
                RadialLogger.Warn("target.unresolved", message);
                return;
            }

            RadialLogger.Info("target.resolved", $"{target!.Kind} ({target.Source}).");
            if (target.Kind == ShellTargetKind.Folder)
                RadialLogger.Info("target.folder", $"Folder: {target.FolderPath}");
            foreach (var item in target.Items)
                RadialLogger.Info("target.item", $"{(item.IsDirectory ? "[Folder] " : "[File]   ")}{item.Path}");
        });

        try
        {
            listener.Start();
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            RadialLogger.Error("listener.start.failed", $"Could not start listener: {ex.GetBaseException().Message}");
        }
    }

    private static void PrintThemes()
    {
        var current = RadialThemeSettings.Load();
        Console.WriteLine("Available RadialShell themes:");
        foreach (var theme in RadialThemeCatalog.All)
            Console.WriteLine($"  {(theme.Name == current.Name ? "* " : "  ")}{theme.Name}");
        Console.WriteLine("\nSet one with: RadialShell.exe --theme \"Theme Name\"");
        Console.WriteLine("Preview and browse with: RadialShell.exe --preview");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("RadialShell — Windows Explorer radial quick-action utility\n");
        Console.WriteLine("Usage:");
        Console.WriteLine("  RadialShell.exe --self-test       Run target and theme checks");
        Console.WriteLine("  RadialShell.exe --listen          Observe Alt+Right-Click and print resolved targets");
        Console.WriteLine("  RadialShell.exe                    Start the tray app (disabled until left-clicked)");
        Console.WriteLine("  RadialShell.exe --run             Start the tray app without a console window");
        Console.WriteLine("  RadialShell.exe --preview [name]  Preview the wheel; ←/→ changes and saves themes");
        Console.WriteLine("  RadialShell.exe --themes          List built-in themes");
        Console.WriteLine("  RadialShell.exe --theme <name>    Save the preferred theme");
        Console.WriteLine("  RadialShell.exe --log-level <level> Set error|warn|info|debug|trace (default: info)");
        Console.WriteLine("  --verbose / --debug / --trace     Increase diagnostic detail");
        Console.WriteLine("\nStructured logs are written under .\\data\\logs and rotated within a bounded size.");
        Console.WriteLine("The diagnostic listener is passive and does not suppress the native context menu.");
    }

    private static bool IsTrayLaunch(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return true;

        var runRequested = false;
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--run", StringComparison.OrdinalIgnoreCase))
                runRequested = true;
            else if (string.Equals(args[index], "--log-level", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                    !RadialLogger.TryParseLevel(args[index + 1], out _))
                    return false;
                index++;
            }
            else if (!string.Equals(args[index], "--verbose", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(args[index], "--debug", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(args[index], "--trace", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return runRequested;
    }

    private static void AttachConsoleForCommandLineMode()
    {
        if (!AttachConsole(AttachParentProcess) && Marshal.GetLastWin32Error() != ErrorAccessDenied)
            _ = AllocConsole();

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int ErrorAccessDenied = 5;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    internal static bool TryGetLogLevel(IReadOnlyList<string> args, out RadialLogLevel level, out string? error)
    {
        level = RadialLogLevel.Info;
        error = null;
        var optionIndex = IndexOf(args, "--log-level");
        if (optionIndex >= 0)
        {
            if (args.Count(arg => string.Equals(arg, "--log-level", StringComparison.OrdinalIgnoreCase)) != 1 ||
                optionIndex + 1 >= args.Count || args[optionIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = "Use --log-level <error|warn|info|debug|trace> once.";
                return false;
            }

            if (!RadialLogger.TryParseLevel(args[optionIndex + 1], out level))
            {
                error = $"Unknown log level '{args[optionIndex + 1]}'. Use error, warn, info, debug, or trace.";
                return false;
            }
            return true;
        }

        if (Has(args, "--trace"))
            level = RadialLogLevel.Trace;
        else if (Has(args, "--debug") || Has(args, "--verbose"))
            level = RadialLogLevel.Debug;
        return true;
    }

    private static bool Has(IEnumerable<string> args, string value) =>
        args.Any(arg => string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));

    private static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var index = 0; index < args.Count; index++)
            if (string.Equals(args[index], value, StringComparison.OrdinalIgnoreCase))
                return index;
        return -1;
    }
}
