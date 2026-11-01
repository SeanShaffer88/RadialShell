using System.Windows.Forms;
using RadialShell.Core;

namespace RadialShell.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (Has(args, "--help") || Has(args, "-h"))
        {
            PrintHelp();
            return 0;
        }

        if (Has(args, "--self-test"))
        {
            TargetPolicySmokeTests.Run();
            ThemeSmokeTests.Run();
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
                Console.Error.WriteLine("Missing theme name after --theme.");
                PrintThemes();
                return 2;
            }

            var requested = args[themeArg + 1];
            if (!RadialThemeCatalog.TryFind(requested, out var theme))
            {
                Console.Error.WriteLine($"Unknown theme '{requested}'.");
                PrintThemes();
                return 2;
            }

            if (!RadialThemeSettings.Save(theme.Name, out var error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            Console.WriteLine($"Theme preference saved: {theme.Name}");
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
                    Console.Error.WriteLine($"Unknown theme '{args[previewArg + 1]}'.");
                    PrintThemes();
                    return 2;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new RadialMenuPreviewForm(previewTheme));
            return 0;
        }

        if (Has(args, "--listen"))
        {
            RunListener();
            return 0;
        }

        PrintHelp();
        return 0;
    }

    private static void RunListener()
    {
        Console.WriteLine("RadialShell diagnostic listener is active.");
        Console.WriteLine("Alt + Right-Click an item or blank area in File Explorer/Desktop.");
        Console.WriteLine("This diagnostic observes the gesture only; normal Windows behavior is preserved.");
        Console.WriteLine("Press Enter to exit.\n");

        var resolver = new ShellTargetResolver();
        using var listener = new MouseGestureListener(point =>
        {
            Console.WriteLine($"Gesture at ({point.X}, {point.Y})");
            if (!resolver.TryResolve(point, out var target, out var message))
            {
                Console.WriteLine($"  Not resolved: {message}");
                return;
            }

            Console.WriteLine($"  {target!.Kind} ({target.Source})");
            if (target.Kind == ShellTargetKind.Folder)
                Console.WriteLine($"  Folder: {target.FolderPath}");
            foreach (var item in target.Items)
                Console.WriteLine($"  {(item.IsDirectory ? "[Folder] " : "[File]   ")}{item.Path}");
        });

        try
        {
            listener.Start();
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not start listener: {ex.GetBaseException().Message}");
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
        Console.WriteLine("  RadialShell.exe --preview [name]  Preview the wheel; ←/→ changes and saves themes");
        Console.WriteLine("  RadialShell.exe --themes          List built-in themes");
        Console.WriteLine("  RadialShell.exe --theme <name>    Save the preferred theme");
        Console.WriteLine("\nThe diagnostic listener is passive and does not suppress the native context menu.");
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
