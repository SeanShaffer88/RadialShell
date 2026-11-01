using System.Windows.Forms;
using RadialShell.Core;

namespace RadialShell.App;

/// <summary>Owns the active hook and marshals resolved targets to the WinForms UI thread.</summary>
internal sealed class RadialShellApplicationContext : ApplicationContext
{
    private readonly EscapeDispatcher _dispatcher = new();
    private readonly ShellTargetResolver _resolver = new();
    private MouseGestureListener? _listener;
    private RadialActionMenuForm? _menu;

    public void Start()
    {
        _dispatcher.OnEscapePressed += () => _menu?.Close();
        _ = _dispatcher.Handle;
        _listener = new MouseGestureListener(OnGestureResolved, consumeGesture: true);
        _listener.Start();
    }

    public void RequestExit()
    {
        if (!_dispatcher.IsDisposed && _dispatcher.IsHandleCreated)
            _dispatcher.BeginInvoke((Action)ExitThread);
    }

    private void OnGestureResolved(NativePoint point)
    {
        if (!_resolver.TryResolve(point, out var target, out var message))
        {
            RadialLogger.Warn("gesture.ignored", message);
            return;
        }

        if (!string.Equals(message, "Target resolved.", StringComparison.Ordinal))
            RadialLogger.Warn("target.fallback", message);

        try
        {
            _dispatcher.BeginInvoke((Action)(() => ShowActionMenu(point, target!)));
        }
        catch (InvalidOperationException)
        {
            // Shutdown may race with a final queued hook notification.
        }
    }

    private void ShowActionMenu(NativePoint point, ShellTarget target)
    {
        _menu?.Close();
        _menu?.Dispose();

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

    protected override void ExitThreadCore()
    {
        _dispatcher.UnregisterEscape();
        _listener?.Dispose();
        _menu?.Close();
        _menu?.Dispose();
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
