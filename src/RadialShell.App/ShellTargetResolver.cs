using System.Runtime.InteropServices;
using System.Text;
using RadialShell.Core;

namespace RadialShell.App;

/// <summary>
/// Best-effort Explorer/Desktop resolver for the M1 diagnostic. Shell automation
/// and accessibility are deliberately called only on the resolver STA thread.
/// </summary>
internal sealed class ShellTargetResolver
{
    private const uint GaRoot = 2;
    private const int RoleList = 0x21;
    private const int RoleListItem = 0x22;

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

            var accessibleResult = AccessibleObjectFromPoint(
                new AccessiblePoint { X = point.X, Y = point.Y }, out var accessible, out var child);
            accessibleObject = accessible;
            childId = child;
            if (accessibleResult < 0 || accessibleObject is null)
            {
                message = "Windows accessibility did not identify a file-view target.";
                return false;
            }

            dynamic accessibleDispatch = accessibleObject;
            int role;
            string? accessibleName;
            try
            {
                role = Convert.ToInt32(accessibleDispatch.get_accRole(childId), System.Globalization.CultureInfo.InvariantCulture);
                accessibleName = Convert.ToString(accessibleDispatch.get_accName(childId), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                message = "The pointer is over a shell control rather than a file-view item.";
                return false;
            }

            // List-item accessibility names are matched against the shell folder's
            // own item collection so actions receive canonical filesystem paths.
            ShellItem? hovered = null;
            if (role == RoleListItem && !string.IsNullOrWhiteSpace(accessibleName))
                hovered = FindFolderItem(folderObject, accessibleName!);

            var inFolderView = role is RoleList or RoleListItem;
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

            message = "Target resolved.";
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

    private static ShellItem? FindFolderItem(dynamic folder, string accessibleName)
    {
        try
        {
            dynamic items = folder.Items();
            var count = Convert.ToInt32(items.Count, System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                try
                {
                    dynamic item = items.Item(index);
                    var name = Convert.ToString(item.Name, System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.Equals(name, accessibleName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var path = Convert.ToString(item.Path, System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(path))
                        return new ShellItem(path, Convert.ToBoolean(item.IsFolder, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch
                {
                    // Skip non-filesystem shell items for this diagnostic.
                }
            }
        }
        catch
        {
            // Namespace folders may not expose filesystem children.
        }
        return null;
    }

    private static IntPtr GetRootWindow(NativePoint point)
    {
        var hit = WindowFromPoint(new AccessiblePoint { X = point.X, Y = point.Y });
        return hit == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hit, GaRoot);
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
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowClassName(IntPtr hwnd, StringBuilder className, int maxCount);
}
