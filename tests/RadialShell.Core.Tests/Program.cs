using RadialShell.Core;

var selectedA = new ShellItem(@"C:\Work\alpha.txt");
var selectedB = new ShellItem(@"C:\Work\beta.txt");
var unrelated = new ShellItem(@"C:\Work\other.txt");

AssertTarget(
    new ShellViewSnapshot(@"C:\Work", [selectedA, selectedB], selectedA, true),
    ShellTargetKind.Selection,
    [selectedA, selectedB],
    null,
    "pointer on a selected item applies to the full selection");

AssertTarget(
    new ShellViewSnapshot(@"C:\Work", [selectedA, selectedB], unrelated, true),
    ShellTargetKind.Item,
    [unrelated],
    null,
    "pointer on an unselected item overrides unrelated selection");

AssertTarget(
    new ShellViewSnapshot(@"C:\Work", [selectedA], null, true),
    ShellTargetKind.Folder,
    [],
    @"C:\Work",
    "blank folder-view space targets the current folder");

AssertTarget(
    new ShellViewSnapshot(@"C:\Users\Ada\Desktop", [], null, true, IsDesktop: true),
    ShellTargetKind.Folder,
    [],
    @"C:\Users\Ada\Desktop",
    "blank Desktop space targets the Desktop folder");

var outsideView = new ShellViewSnapshot(@"C:\Work", [], null, false);
if (ShellTargetPolicy.TryResolve(outsideView, out _))
    throw new Exception("A non-folder-view control must not resolve to a target.");

var caseVariant = new ShellItem(@"c:/WORK/ALPHA.TXT");
AssertTarget(
    new ShellViewSnapshot(@"C:\Work", [selectedA, selectedB], caseVariant, true),
    ShellTargetKind.Selection,
    [selectedA, selectedB],
    null,
    "selected-item matching is case-insensitive and separator-insensitive");

Console.WriteLine("All target policy checks passed.");

static void AssertTarget(
    ShellViewSnapshot snapshot,
    ShellTargetKind expectedKind,
    IReadOnlyList<ShellItem> expectedItems,
    string? expectedFolder,
    string description)
{
    if (!ShellTargetPolicy.TryResolve(snapshot, out var target) || target is null)
        throw new Exception($"Expected target: {description}.");
    if (target.Kind != expectedKind)
        throw new Exception($"Wrong target kind for: {description}. Got {target.Kind}.");
    if (target.Items.Count != expectedItems.Count ||
        !target.Items.Select(x => x.Path).SequenceEqual(expectedItems.Select(x => x.Path), StringComparer.OrdinalIgnoreCase))
        throw new Exception($"Wrong item list for: {description}.");
    if (!StringComparer.OrdinalIgnoreCase.Equals(target.FolderPath, expectedFolder))
        throw new Exception($"Wrong folder path for: {description}.");
}
