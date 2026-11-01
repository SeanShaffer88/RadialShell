namespace RadialShell.Core;

/// <summary>Applies RadialShell's pointer/selection/folder precedence to a shell-view snapshot.</summary>
public static class ShellTargetPolicy
{
    /// <summary>
    /// Resolve the action target. A selected item under the pointer expands to the
    /// full selection; an unselected item wins over an unrelated selection; empty
    /// folder-view space targets the current folder.
    /// </summary>
    public static bool TryResolve(ShellViewSnapshot snapshot, out ShellTarget? target)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        target = null;

        if (string.IsNullOrWhiteSpace(snapshot.CurrentFolderPath))
            return false;

        var selection = snapshot.SelectedItems ?? Array.Empty<ShellItem>();
        var hovered = snapshot.ItemUnderPointer;

        if (hovered is not null)
        {
            if (selection.Any(item => PathsEqual(item.Path, hovered.Path)))
            {
                // Preserve Explorer's familiar behavior: invoking an action on an
                // already-selected item applies it to the entire selection.
                var distinctSelection = selection
                    .DistinctBy(item => item.Path, WindowsPathComparer.Instance)
                    .ToArray();
                target = ShellTarget.ForSelection(distinctSelection);
            }
            else
            {
                target = ShellTarget.ForItem(hovered);
            }

            return true;
        }

        if (!snapshot.PointerIsInFolderView)
            return false;

        // On blank space, the folder is the useful target even if Explorer has a
        // stale selection. Desktop snapshots use the Desktop folder path here too.
        target = ShellTarget.ForFolder(snapshot.CurrentFolderPath);
        return true;
    }

    private static bool PathsEqual(string left, string right) =>
        WindowsPathComparer.Instance.Equals(left, right);

    private sealed class WindowsPathComparer : IEqualityComparer<string>
    {
        public static WindowsPathComparer Instance { get; } = new();

        public bool Equals(string? x, string? y) =>
            StringComparer.OrdinalIgnoreCase.Equals(Normalize(x), Normalize(y));

        public int GetHashCode(string value) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(value));

        private static string Normalize(string? path)
        {
            var normalized = (path ?? string.Empty).Replace('/', '\\');

            // Keep drive roots distinct from drive-relative paths (C:\\ is not C:).
            if (normalized.Length == 3 && char.IsAsciiLetter(normalized[0]) &&
                normalized[1] == ':' && normalized[2] == '\\')
                return normalized;
            if (normalized.Length == 7 && normalized.StartsWith("\\\\?\\", StringComparison.Ordinal) &&
                char.IsAsciiLetter(normalized[4]) && normalized[5] == ':' && normalized[6] == '\\')
                return normalized;

            return normalized.TrimEnd('\\');
        }
    }
}
