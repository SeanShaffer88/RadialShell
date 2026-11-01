using RadialShell.App;
using RadialShell.Core;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("RadialShell diagnostic prototype\n\nUsage:\n  RadialShell.exe --self-test   Run target policy checks\n  RadialShell.exe --listen      Observe Alt+Right-Click in Explorer/Desktop\n\nThe prototype does not suppress the normal right-click or context menu.");
    return;
}

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    TargetPolicySmokeTests.Run();
    return;
}

if (!args.Contains("--listen", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Run with --self-test or --listen. Use --help for details.");
    return;
}

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

listener.Start();
Console.ReadLine();
