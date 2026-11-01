namespace RadialShell.Core;

/// <summary>The kind of shell context the pointer resolved to.</summary>
public enum ShellTargetKind
{
    Item,
    Selection,
    Folder
}

/// <summary>Why a target was chosen from the current shell-view state.</summary>
public enum ShellTargetSource
{
    ItemUnderPointer,
    SelectedItems,
    CurrentFolder
}

/// <summary>A filesystem object exposed by Explorer.</summary>
public sealed record ShellItem(string Path, bool IsDirectory = false);

/// <summary>
/// Snapshot of the shell view at trigger time. The platform-specific resolver is
/// responsible for obtaining this snapshot without doing work in a low-level hook.
/// </summary>
public sealed record ShellViewSnapshot(
    string CurrentFolderPath,
    IReadOnlyList<ShellItem> SelectedItems,
    ShellItem? ItemUnderPointer,
    bool PointerIsInFolderView,
    bool IsDesktop = false);

/// <summary>The target supplied to radial actions.</summary>
public sealed record ShellTarget(
    ShellTargetKind Kind,
    ShellTargetSource Source,
    IReadOnlyList<ShellItem> Items,
    string? FolderPath)
{
    public static ShellTarget ForItem(ShellItem item) =>
        new(ShellTargetKind.Item, ShellTargetSource.ItemUnderPointer, [item], null);

    public static ShellTarget ForSelection(IReadOnlyList<ShellItem> items) =>
        new(ShellTargetKind.Selection, ShellTargetSource.SelectedItems, items, null);

    public static ShellTarget ForFolder(string path) =>
        new(ShellTargetKind.Folder, ShellTargetSource.CurrentFolder, [], path);
}
