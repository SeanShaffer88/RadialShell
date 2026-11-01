using System.Windows.Forms;
using RadialShell.Core;

namespace RadialShell.App;

/// <summary>Owns the active hook and marshals resolved targets to the WinForms UI thread.</summary>
internal sealed class RadialShellApplicationContext : ApplicationContext
{
    private readonly Control _dispatcher = new();
    private readonly ShellTargetResolver _resolver = new();
    private MouseGestureListener? _listener;
    private RadialActionMenuForm? _menu;

    public void Start()
    {
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
            Console.WriteLine($"Gesture ignored: {message}");
            return;
        }

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
        _menu.FormClosed += (_, _) => _menu = null;
        _menu.ShowAtTrigger();
    }

    protected override void ExitThreadCore()
    {
        _listener?.Dispose();
        _menu?.Close();
        _menu?.Dispose();
        _dispatcher.Dispose();
        base.ExitThreadCore();
    }
}
