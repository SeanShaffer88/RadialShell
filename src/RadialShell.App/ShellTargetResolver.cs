using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RadialShell.Core;
using UIA = System.Windows.Automation;

namespace RadialShell.App;

/// <summary>
/// Best-effort Explorer/Desktop resolver for the M1 diagnostic. Shell automation
/// and accessibility are deliberately called only on the resolver STA thread.
/// </summary>
internal sealed class ShellTargetResolver
{
    private const uint GaRoot = 2;

    public bool TryResolve(NativePoint point, out ShellTarget? target, out string message)
    {
        target = null;
        message = "The pointer is not over a supported Explorer file view.";

        object? shellObject = null;
        object? folderObject = null;
        object? accessibleObject = null;
        object? childId = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                message = "Windows Shell automation is unavailable.";
                return false;
            }

            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null)
            {
                message = "Could not connect to Windows Shell automation.";
                return false;
            }

            dynamic shell = shellObject;
            var root = GetRootWindow(point);
            var rootClass = GetClassName(root);
            var isDesktop = rootClass is "Progman" or "WorkerW";
            dynamic? shellWindow = FindShellWindow(shell, root);

            string currentFolderPath;
            List<ShellItem> selected;
            if (isDesktop)
            {
                folderObject = shell.NameSpace(0);
                currentFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrWhiteSpace(currentFolderPath))
                    currentFolderPath = GetFolderPath(folderObject);
                selected = shellWindow is null ? new List<ShellItem>() : GetSelectedItems(shellWindow);
            }
            else if (shellWindow is not null)
            {
                dynamic document = shellWindow.Document;
                folderObject = document.Folder;
                currentFolderPath = GetFolderPath(folderObject);
                selected = GetSelectedItems(shellWindow);
            }
            else
            {
                message = "The Explorer window under the pointer could not be matched to a shell view.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentFolderPath))
            {
                message = "The current shell folder has no filesystem path.";
                return false;
            }

            var inFolderView = isDesktop || IsInExplorerFileView(point);
            if (!inFolderView)
            {
                message = "The pointer is over a shell control rather than the file-view surface.";
                return false;
            }

            // UIA's point element can be a generic pane in Explorer. Search the
            // file-view descendants by both cursor bounds and canonical Shell item
            // names; this also covers providers that expose rows as Custom controls
            // or named text descendants rather than ListItem/DataItem controls.
            var folderItems = GetFolderItems(folderObject);
            var hovered = TryGetUiAutomationItem(point, folderItems);
            string? accessibleName = hovered is null ? null : Path.GetFileName(hovered.Path);
            string? accessibilityError = null;
            if (hovered is null)
            {
                var accessibleResult = AccessibleObjectFromPoint(
                    new AccessiblePoint { X = point.X, Y = point.Y }, out var accessible, out var child);
                accessibleObject = accessible;
                childId = child;
                if (accessibleResult >= 0 && accessibleObject is not null)
                {
                    try
                    {
                        dynamic accessibleDispatch = accessibleObject;
                        accessibleName = Convert.ToString(accessibleDispatch.accName(childId), System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex)
                    {
                        accessibilityError = ex.GetBaseException().Message;
                    }
                }
                else
                {
                    accessibilityError = $"AccessibleObjectFromPoint returned HRESULT 0x{accessibleResult:X8}.";
                }

                if (!string.IsNullOrWhiteSpace(accessibleName) &&
                    TryMatchShellItem(accessibleName!, folderItems, out var msaaMatch))
                    hovered = msaaMatch;
            }

            var snapshot = new ShellViewSnapshot(
                currentFolderPath,
                selected,
                hovered,
                inFolderView,
                IsDesktop: isDesktop);

            if (!ShellTargetPolicy.TryResolve(snapshot, out target))
            {
                message = "No target could be resolved for this shell control.";
                return false;
            }

            message = target!.Kind == ShellTargetKind.Folder && accessibilityError is not null
                ? $"Resolved the current folder; MSAA item details were unavailable ({accessibilityError})."
                : target.Kind == ShellTargetKind.Folder && !string.IsNullOrWhiteSpace(accessibleName) && hovered is null
                    ? $"Resolved the current folder; accessible item '{accessibleName}' did not match a shell item."
                    : "Target resolved.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Shell target resolution failed: {ex.GetBaseException().Message}";
            return false;
        }
        finally
        {
            ReleaseComObject(accessibleObject);
            ReleaseComObject(folderObject);
            ReleaseComObject(shellObject);
        }
    }

    private static ShellFolderEntry[] GetFolderItems(dynamic folder)
    {
        var result = new List<ShellFolderEntry>();
        object? itemsObject = null;
        try
        {
            itemsObject = folder.Items();
            dynamic items = itemsObject;
            var count = Convert.ToInt32(items.Count, System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                object? itemObject = null;
                try
                {
                    itemObject = items.Item(index);
                    dynamic item = itemObject;
                    var name = Convert.ToString(item.Name, System.Globalization.CultureInfo.InvariantCulture);
                    var path = Convert.ToString(item.Path, System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                    {
                        var isFolder = Convert.ToBoolean(item.IsFolder, System.Globalization.CultureInfo.InvariantCulture);
                        result.Add(new ShellFolderEntry(name, new ShellItem(path, isFolder)));
                    }
                }
                catch
                {
                    // Skip virtual/transient shell entries without filesystem paths.
                }
                finally
                {
                    ReleaseComObject(itemObject);
                }
            }
        }
        catch
        {
            // Namespace folders may not expose filesystem children.
        }
        finally
        {
            ReleaseComObject(itemsObject);
        }

        return result.ToArray();
    }

    private static ShellItem? TryGetUiAutomationItem(
        NativePoint point,
        IReadOnlyList<ShellFolderEntry> folderItems)
    {
        try
        {
            var screenPoint = new System.Windows.Point(point.X, point.Y);
            var current = UIA.AutomationElement.FromPoint(screenPoint);
            var walker = UIA.TreeWalker.ControlViewWalker;

            for (var depth = 0; current is not null && depth < 12; depth++)
            {
                var info = current.Current;
                if (IsFileItemControl(info.ControlType) &&
                    TryMatchShellItem(info.Name, folderItems, out var directMatch))
                    return directMatch;

                if (info.Name.Contains("Items View", StringComparison.OrdinalIgnoreCase) ||
                    info.ControlType == UIA.ControlType.List ||
                    info.ControlType == UIA.ControlType.DataGrid)
                {
                    // Do not rely only on Explorer's reported control type. Some
                    // Windows builds expose the virtualized row as Custom (or only
                    // expose named text descendants) while MSAA reports "Items View".
                    var descendants = current.FindAll(UIA.TreeScope.Descendants, UIA.Condition.TrueCondition);
                    var count = Math.Min(descendants.Count, 10000);
                    for (var index = 0; index < count; index++)
                    {
                        try
                        {
                            var element = descendants[index];
                            var elementInfo = element.Current;
                            if (elementInfo.IsOffscreen || !elementInfo.BoundingRectangle.Contains(screenPoint) ||
                                string.IsNullOrWhiteSpace(elementInfo.Name))
                                continue;
                            if (TryMatchShellItem(elementInfo.Name, folderItems, out var match))
                                return match;
                        }
                        catch (UIA.ElementNotAvailableException)
                        {
                            // Virtualized rows can be recycled while Explorer refreshes.
                        }
                    }
                }

                current = walker.GetParent(current);
            }
        }
        catch
        {
            // MSAA/folder-view fallback below handles UIA providers that are absent
            // or don't expose usable virtualized rows.
        }

        return null;
    }

    private static bool TryMatchShellItem(
        string accessibleName,
        IReadOnlyList<ShellFolderEntry> folderItems,
        out ShellItem? match)
    {
        match = null;
        var exact = folderItems.Where(entry =>
            string.Equals(entry.Name, accessibleName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 1)
        {
            match = exact[0].Item;
            return true;
        }
        if (exact.Length > 1)
            return false;

        // Explorer often hides known file extensions in UIA names. Accept that
        // display form only when it uniquely identifies one filesystem item.
        var displayMatches = folderItems.Where(entry =>
            Path.HasExtension(entry.Name) &&
            string.Equals(Path.GetFileNameWithoutExtension(entry.Name), accessibleName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (displayMatches.Length != 1)
            return false;

        match = displayMatches[0].Item;
        return true;
    }

    private static bool IsFileItemControl(UIA.ControlType type) =>
        type == UIA.ControlType.ListItem || type == UIA.ControlType.DataItem ||
        type == UIA.ControlType.TreeItem || type == UIA.ControlType.Custom;

    private sealed record ShellFolderEntry(string Name, ShellItem Item);

    private static dynamic? FindShellWindow(dynamic shell, IntPtr root)
    {
        try
        {
            dynamic windows = shell.Windows();
            var count = Convert.ToInt32(windows.Count, System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                dynamic window = windows.Item(index);
                try
                {
                    var handle = new IntPtr(Convert.ToInt64(window.HWND, System.Globalization.CultureInfo.InvariantCulture));
                    if (handle == root || GetAncestor(handle, GaRoot) == root)
                        return window;
                }
                catch
                {
                    // Some ShellWindows entries (for example non-Explorer tabs) do
                    // not expose the expected document/window properties.
                }
            }
        }
        catch
        {
            // The Desktop can be absent from IShellWindows on some Windows builds.
        }

        return null;
    }

    private static string GetFolderPath(dynamic folder)
    {
        try
        {
            return Convert.ToString(folder.Self.Path, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<ShellItem> GetSelectedItems(dynamic shellWindow)
    {
        var result = new List<ShellItem>();
        try
        {
            dynamic selected = shellWindow.Document.SelectedItems();
            var count = Convert.ToInt32(selected.Count, System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                try
                {
                    dynamic item = selected.Item(index);
                    var path = Convert.ToString(item.Path, System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(path))
                        result.Add(new ShellItem(path, Convert.ToBoolean(item.IsFolder, System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch
                {
                    // Skip virtual or transient selected items without a path.
                }
            }
        }
        catch
        {
            // A Desktop shell window may not expose SelectedItems().
        }
        return result;
    }

    private static IntPtr GetRootWindow(NativePoint point)
    {
        var hit = WindowFromPoint(new AccessiblePoint { X = point.X, Y = point.Y });
        return hit == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hit, GaRoot);
    }

    private static bool IsInExplorerFileView(NativePoint point)
    {
        var hit = WindowFromPoint(new AccessiblePoint { X = point.X, Y = point.Y });
        var root = hit == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hit, GaRoot);
        for (var current = hit; current != IntPtr.Zero; current = GetParent(current))
        {
            if (GetClassName(current) == "SHELLDLL_DefView")
                return true;
            if (current == root)
                break;
        }
        return false;
    }

    private static string GetClassName(IntPtr window)
    {
        if (window == IntPtr.Zero) return string.Empty;
        var name = new StringBuilder(128);
        _ = GetWindowClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.ReleaseComObject(value); }
            catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccessiblePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("oleacc.dll", PreserveSig = true)]
    private static extern int AccessibleObjectFromPoint(
        AccessiblePoint point,
        [MarshalAs(UnmanagedType.IDispatch)] out object accessible,
        [MarshalAs(UnmanagedType.Struct)] out object childId);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(AccessiblePoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowClassName(IntPtr hwnd, StringBuilder className, int maxCount);
}
