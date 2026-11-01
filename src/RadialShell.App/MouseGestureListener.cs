using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RadialShell.App;

/// <summary>Observes Alt+right-click over Explorer/Desktop without suppressing it.</summary>
internal sealed class MouseGestureListener : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmQuit = 0x0012;
    private const int VkMenu = 0x12;
    private const uint GaRoot = 2;
    private const uint PmNoRemove = 0;

    private readonly Action<NativePoint> _onGesture;
    private readonly bool _consumeGesture;
    private readonly BlockingCollection<NativePoint> _work = new();
    private bool _consumeNextRightUp;
    private readonly ManualResetEventSlim _hookReady = new();
    private readonly HookProc _hookProc;
    private Thread? _hookThread;
    private Thread? _workerThread;
    private uint _hookThreadId;
    private IntPtr _hook;
    private Exception? _startError;
    private bool _disposed;
    private int _stopRequested;

    public MouseGestureListener(Action<NativePoint> onGesture, bool consumeGesture = false)
    {
        _onGesture = onGesture;
        _consumeGesture = consumeGesture;
        _hookProc = HookCallback;
    }

    internal static uint CurrentNativeThreadIdForTest() => GetCurrentThreadId();

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "RadialShell shell resolver" };
        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();

        _hookThread = new Thread(HookLoop) { IsBackground = true, Name = "RadialShell mouse hook" };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        _hookReady.Wait();
        if (_startError is not null)
            throw new InvalidOperationException("Could not install the mouse hook.", _startError);
    }

    private void WorkerLoop()
    {
        foreach (var point in _work.GetConsumingEnumerable())
        {
            try { _onGesture(point); }
            catch (Exception ex) { RadialLogger.Error("gesture.report_failed", $"Target reporting failed: {ex.Message}"); }
        }
    }

    private void HookLoop()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            _hook = SetWindowsHookEx(WhMouseLl, _hookProc, IntPtr.Zero, 0);
            if (_hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _hookReady.Set();
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            _startError = ex;
            _hookReady.Set();
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                _ = UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
            return CallNextHookEx(_hook, code, wParam, lParam);

        var message = unchecked((int)wParam);
        if (message == WmRButtonUp && _consumeNextRightUp)
        {
            _consumeNextRightUp = false;
            return (IntPtr)1;
        }

        if (message == WmRButtonDown && (GetAsyncKeyState(VkMenu) & 0x8000) != 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<MouseLowLevelHookData>(lParam);
                if (IsExplorerFileViewPoint(data.Point))
                {
                    RadialLogger.Trace("hook.gesture_queued", $"Queued shell-view gesture at ({data.Point.X}, {data.Point.Y}).");
                    _work.Add(data.Point);
                    if (_consumeGesture)
                    {
                        _consumeNextRightUp = true;
                        return (IntPtr)1;
                    }
                }
            }
            catch
            {
                // Never let diagnostic/reporting errors interrupt the system hook.
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool IsExplorerFileViewPoint(NativePoint point)
    {
        var hit = WindowFromPoint(point);
        var root = GetAncestor(hit, GaRoot);
        if (root == IntPtr.Zero)
            return false;

        var rootName = GetWindowClass(root);
        var isDesktop = rootName is "Progman" or "WorkerW";
        if (!isDesktop && rootName is not ("CabinetWClass" or "ExploreWClass"))
            return false;

        // Desktop background windows can be siblings of the icon view; accept them
        // and let the resolver classify them as the Desktop folder. For Explorer,
        // require the file-view host so chrome and navigation controls pass through.
        if (isDesktop)
            return true;
        for (var current = hit; current != IntPtr.Zero; current = GetParent(current))
        {
            if (GetWindowClass(current) == "SHELLDLL_DefView")
                return true;
            if (current == root)
                break;
        }
        return false;
    }

    private static string GetWindowClass(IntPtr window)
    {
        var className = new System.Text.StringBuilder(128);
        _ = GetClassName(window, className, className.Capacity);
        return className.ToString();
    }

    public void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
            return;
        _work.CompleteAdding();
        if (_hookThreadId != 0)
            _ = PostThreadMessage(_hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RequestStop();
        _hookThread?.Join(TimeSpan.FromSeconds(3));
        _workerThread?.Join(TimeSpan.FromSeconds(3));
        if (_hookThread?.IsAlive != true && _workerThread?.IsAlive != true)
        {
            _work.Dispose();
            _hookReady.Dispose();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr HWnd;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
}

internal readonly record struct NativePoint(int X, int Y);
