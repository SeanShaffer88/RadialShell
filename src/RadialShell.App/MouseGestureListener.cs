using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RadialShell.App;

/// <summary>Observes Alt+right-click over Explorer/Desktop without suppressing it.</summary>
internal sealed class MouseGestureListener : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmRButtonDown = 0x0204;
    private const int WmQuit = 0x0012;
    private const int VkMenu = 0x12;
    private const uint GaRoot = 2;
    private const uint PmNoRemove = 0;

    private readonly Action<NativePoint> _onGesture;
    private readonly BlockingCollection<NativePoint> _work = new();
    private readonly ManualResetEventSlim _hookReady = new();
    private readonly HookProc _hookProc;
    private Thread? _hookThread;
    private Thread? _workerThread;
    private uint _hookThreadId;
    private IntPtr _hook;
    private Exception? _startError;
    private bool _disposed;

    public MouseGestureListener(Action<NativePoint> onGesture)
    {
        _onGesture = onGesture;
        _hookProc = HookCallback;
    }

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
            catch (Exception ex) { Console.Error.WriteLine($"Target reporting failed: {ex.Message}"); }
        }
    }

    private void HookLoop()
    {
        _hookThreadId = GetCurrentThreadId();
        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        _hook = SetWindowsHookEx(WhMouseLl, _hookProc, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            _startError = new Win32Exception(Marshal.GetLastWin32Error());
            _hookReady.Set();
            return;
        }

        _hookReady.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        _ = UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && unchecked((int)wParam) == WmRButtonDown &&
            (GetAsyncKeyState(VkMenu) & 0x8000) != 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<MouseLowLevelHookData>(lParam);
                if (IsExplorerOrDesktopPoint(data.Point))
                    _work.Add(data.Point);
            }
            catch
            {
                // Never let diagnostic/reporting errors interrupt the system hook.
            }
        }

        // This is intentionally a passive prototype. Right-click and the native
        // context menu continue normally while target discovery is being validated.
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool IsExplorerOrDesktopPoint(NativePoint point)
    {
        var hit = WindowFromPoint(point);
        var root = GetAncestor(hit, GaRoot);
        if (root == IntPtr.Zero)
            return false;

        var className = new System.Text.StringBuilder(128);
        _ = GetClassName(root, className, className.Capacity);
        var name = className.ToString();
        // Restrict to the well-known shell top-level window classes. Avoid process
        // inspection or any other potentially blocking work in this hook callback.
        return name is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hookThreadId != 0)
            _ = PostThreadMessage(_hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _work.CompleteAdding();
        _hookThread?.Join(TimeSpan.FromSeconds(3));
        _workerThread?.Join(TimeSpan.FromSeconds(3));
        _work.Dispose();
        _hookReady.Dispose();
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
}

internal readonly record struct NativePoint(int X, int Y);
