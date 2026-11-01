using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RadialShell.Core;

namespace RadialShell.App;

/// <summary>Owns tray state, the active hook, and UI-thread shell actions.</summary>
internal sealed class RadialShellApplicationContext : ApplicationContext
{
    private readonly EscapeDispatcher _dispatcher = new();
    private readonly ShellTargetResolver _resolver = new();
    private MouseGestureListener? _listener;
    private RadialActionMenuForm? _menu;
    private ThemePickerForm? _themePicker;
    private readonly NotifyIcon _trayIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private Icon? _enabledIcon;
    private Icon? _disabledIcon;
    private bool _enabled;

    public void Start()
    {
        _dispatcher.OnEscapePressed += () => _menu?.Close();
        _ = _dispatcher.Handle;

        _enabledIcon = LoadTrayIcon("RadialShell-enabled.ico");
        _disabledIcon = LoadTrayIcon("RadialShell-disabled.ico");
        var themeItem = new ToolStripMenuItem("Theme &Picker...")
        {
            AccessibleName = "Open theme picker",
            ToolTipText = "Choose and save a radial menu theme."
        };
        themeItem.Click += (_, _) => ShowThemePicker();
        var exitItem = new ToolStripMenuItem("E&xit")
        {
            AccessibleName = "Exit RadialShell",
            ToolTipText = "Close RadialShell and remove its tray icon."
        };
        exitItem.Click += (_, _) => ExitThread();
        _trayMenu.ShowItemToolTips = true;
        _trayMenu.Items.Add(themeItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.MouseClick += OnTrayMouseClick;
        UpdateTrayStatus();
        _trayIcon.Visible = true;
    }

    public void RequestExit()
    {
        if (!_dispatcher.IsDisposed && _dispatcher.IsHandleCreated)
            _dispatcher.BeginInvoke((Action)ExitThread);
    }

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            SetEnabled(!_enabled);
    }

    private void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
            return;

        if (!enabled)
        {
            var stoppingListener = _listener;
            _listener = null;
            _enabled = false;
            stoppingListener?.RequestStop();
            if (stoppingListener is not null)
                _ = Task.Run(stoppingListener.Dispose);
            _dispatcher.UnregisterEscape();
            CloseActionMenu();
            UpdateTrayStatus();
            RadialLogger.Info("app.disabled", "RadialShell disabled from the tray icon.");
            return;
        }

        var listener = new MouseGestureListener(OnGestureResolved, consumeGesture: true);
        try
        {
            listener.Start();
            _listener = listener;
            _enabled = true;
            UpdateTrayStatus();
            RadialLogger.Info("app.enabled", "RadialShell enabled from the tray icon.");
        }
        catch (Exception ex)
        {
            listener.Dispose();
            RadialLogger.Error("app.enable.failed", $"Could not enable RadialShell: {ex.GetBaseException().Message}");
            UpdateTrayStatus();
        }
    }

    private void UpdateTrayStatus()
    {
        _trayIcon.Icon = _enabled ? _enabledIcon : _disabledIcon;
        _trayIcon.Text = _enabled
            ? "RadialShell: Enabled | Left-click toggles; right-click Exit"
            : "RadialShell: Disabled | Left-click toggles; right-click Exit";
    }

    private static Icon LoadTrayIcon(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        using var icon = new Icon(path);
        return (Icon)icon.Clone();
    }

    private void ShowThemePicker()
    {
        if (_themePicker is { IsDisposed: false })
        {
            _themePicker.Activate();
            return;
        }

        _dispatcher.UnregisterEscape();
        CloseActionMenu();
        var picker = new ThemePickerForm(RadialThemeSettings.Load());
        picker.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_themePicker, picker))
                _themePicker = null;
        };
        _themePicker = picker;
        picker.Show();
    }

    private void OnGestureResolved(NativePoint point)
    {
        if (!_enabled)
            return;
        if (!_resolver.TryResolve(point, out var target, out var message))
        {
            RadialLogger.Warn("gesture.ignored", message);
            return;
        }

        if (!string.Equals(message, "Target resolved.", StringComparison.Ordinal))
            RadialLogger.Warn("target.fallback", message);

        try
        {
            _dispatcher.BeginInvoke((Action)(() =>
            {
                if (_enabled)
                    ShowActionMenu(point, target!);
            }));
        }
        catch (InvalidOperationException)
        {
            // Shutdown may race with a final queued hook notification.
        }
    }

    private void ShowActionMenu(NativePoint point, ShellTarget target)
    {
        if (!_enabled)
            return;
        CloseActionMenu();

        _menu = new RadialActionMenuForm(target, RadialThemeSettings.Load(), point);
        _menu.Shown += (_, _) => RadialLogger.Info(
            "wheel.shown", $"Radial wheel shown for {target.Kind} target at {_menu.Bounds} (topmost={_menu.TopMost}).");
        _menu.FormClosed += (_, _) =>
        {
            _dispatcher.UnregisterEscape();
            _menu = null;
        };
        if (!_dispatcher.RegisterEscape())
            RadialLogger.Warn("wheel.escape_unavailable", "Escape hotkey unavailable; right-click the wheel or wait for it to dismiss.");
        _menu.ShowAtTrigger();
    }

    private void CloseActionMenu()
    {
        var menu = _menu;
        _menu = null;
        if (menu is null)
            return;
        menu.Close();
        menu.Dispose();
    }

    protected override void ExitThreadCore()
    {
        _enabled = false;
        _dispatcher.UnregisterEscape();
        _listener?.Dispose();
        _listener = null;
        CloseActionMenu();
        var picker = _themePicker;
        _themePicker = null;
        picker?.Close();
        picker?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.MouseClick -= OnTrayMouseClick;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        _enabledIcon?.Dispose();
        _disabledIcon?.Dispose();
        _dispatcher.Dispose();
        base.ExitThreadCore();
    }

    private sealed class EscapeDispatcher : Control
    {
        private const int WmHotKey = 0x0312;
        private const int EscapeHotKeyId = 0x5241;
        private const uint ModNoRepeat = 0x4000;
        private const uint VkEscape = 0x1B;
        private bool _escapeRegistered;

        public bool RegisterEscape()
        {
            if (_escapeRegistered) return true;
            _escapeRegistered = RegisterHotKey(Handle, EscapeHotKeyId, ModNoRepeat, VkEscape);
            return _escapeRegistered;
        }

        public void UnregisterEscape()
        {
            if (!_escapeRegistered) return;
            _ = UnregisterHotKey(Handle, EscapeHotKeyId);
            _escapeRegistered = false;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotKey && message.WParam.ToInt32() == EscapeHotKeyId)
            {
                OnEscapePressed?.Invoke();
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        public event Action? OnEscapePressed;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
