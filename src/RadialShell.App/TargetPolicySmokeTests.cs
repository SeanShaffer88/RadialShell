using RadialShell.Core;

namespace RadialShell.App;

internal static class TargetPolicySmokeTests
{
    public static void Run()
    {
        var selectedA = new ShellItem(@"C:\Work\alpha.txt");
        var selectedB = new ShellItem(@"C:\Work\beta.txt");
        var unrelated = new ShellItem(@"C:\Work\other.txt");

        Verify(
            new ShellViewSnapshot(@"C:\Work", [selectedA, selectedB], selectedA, true),
            ShellTargetKind.Selection, [selectedA, selectedB], null,
            "selected item expands to the complete selection");
        Verify(
            new ShellViewSnapshot(@"C:\Work", [selectedA, selectedB], unrelated, true),
            ShellTargetKind.Item, [unrelated], null,
            "unselected hovered item overrides unrelated selection");
        Verify(
            new ShellViewSnapshot(@"C:\Work", [selectedA], null, true),
            ShellTargetKind.Folder, [], @"C:\Work",
            "blank Explorer view targets its current folder");
        Verify(
            new ShellViewSnapshot(@"C:\Users\Ada\Desktop", [], null, true, IsDesktop: true),
            ShellTargetKind.Folder, [], @"C:\Users\Ada\Desktop",
            "blank Desktop targets the Desktop folder");

        var outside = new ShellViewSnapshot(@"C:\Work", [], null, false);
        if (ShellTargetPolicy.TryResolve(outside, out _))
            throw new InvalidOperationException("A non-file-view control unexpectedly resolved.");

        var caseVariant = new ShellItem(@"c:/WORK/ALPHA.TXT");
        Verify(
            new ShellViewSnapshot(@"C:\Work", [selectedA, selectedB], caseVariant, true),
            ShellTargetKind.Selection, [selectedA, selectedB], null,
            "path matching ignores case and slash direction");

        Console.WriteLine("All RadialShell target-policy checks passed.");
    }

    private static void Verify(
        ShellViewSnapshot snapshot,
        ShellTargetKind expectedKind,
        IReadOnlyList<ShellItem> expectedItems,
        string? expectedFolder,
        string description)
    {
        if (!ShellTargetPolicy.TryResolve(snapshot, out var actual) || actual is null)
            throw new InvalidOperationException($"Expected a target: {description}.");
        if (actual.Kind != expectedKind ||
            !actual.Items.Select(item => item.Path).SequenceEqual(
                expectedItems.Select(item => item.Path), StringComparer.OrdinalIgnoreCase) ||
            !StringComparer.OrdinalIgnoreCase.Equals(actual.FolderPath, expectedFolder))
            throw new InvalidOperationException($"Target policy check failed: {description}.");
        Console.WriteLine($"  PASS: {description}");
    }
}
