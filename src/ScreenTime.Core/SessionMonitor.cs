using System;
using System.Threading;

namespace ScreenTime.Core;

/// <summary>
/// 监听锁屏/解锁（WTS）与显示器亮灭、睡眠唤醒（电源事件），通过隐藏窗口接收消息。
/// </summary>
public sealed class SessionMonitor : IDisposable
{
    public event Action? Locked;
    public event Action? Unlocked;
    public event Action? DisplayOn;
    public event Action? DisplayOff;
    public event Action? Suspended;
    public event Action? Resumed;

    private readonly NativeMethods.WndProcDelegate _wndProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private IntPtr _hwnd;
    private IntPtr _powerHandle;
    private uint _threadId;
    private volatile bool _running;
    private string? _className;

    public SessionMonitor()
    {
        _wndProc = WndProc;
    }

    public void Start()
    {
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "ScreenTime.Session" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _className = "ScreenTimeEvt_" + Guid.NewGuid().ToString("N");

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = _className,
        };
        NativeMethods.RegisterClassExW(ref wc);

        _hwnd = NativeMethods.CreateWindowExW(0, _className, "", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.WTSRegisterSessionNotification(_hwnd, 0);
            var handle = IntPtr.Zero;
            var displayGuid = NativeMethods.GuidConsoleDisplayState;
            NativeMethods.PowerSettingRegisterNotification(
                ref displayGuid,
                NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE, _hwnd, out handle);
            _powerHandle = handle;
        }
        _ready.Set();

        while (_running)
        {
            if (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0) <= 0) break;
            if (msg.message == NativeMethods.WM_STOP) break;
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.WTSUnRegisterSessionNotification(_hwnd);
            if (_powerHandle != IntPtr.Zero)
                NativeMethods.PowerUnregisterSuspendResumeNotification(ref _powerHandle);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        _ready.Set();
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_WTSSESSION_CHANGE:
                var evt = (uint)wParam;
                if (evt == NativeMethods.WTS_SESSION_LOCK) SafeRaise(Locked);
                else if (evt == NativeMethods.WTS_SESSION_UNLOCK) SafeRaise(Unlocked);
                return IntPtr.Zero;

            case NativeMethods.WM_POWERBROADCAST:
                if (wParam == NativeMethods.PBT_POWERSETTINGCHANGE)
                {
                    var setting = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.POWERBROADCAST_SETTING>(lParam);
                    if (setting.PowerSetting == NativeMethods.GuidConsoleDisplayState && setting.DataLength >= 4)
                    {
                        if (setting.Data == 0) SafeRaise(DisplayOff);
                        else SafeRaise(DisplayOn);
                    }
                }
                else if (wParam == NativeMethods.PBT_APMSUSPEND)
                {
                    SafeRaise(Suspended);
                }
                else if (wParam == NativeMethods.PBT_APMRESUMEAUTOMATIC || wParam == NativeMethods.PBT_APMRESUMESUSPEND)
                {
                    SafeRaise(Resumed);
                }
                return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void SafeRaise(Action? handler)
    {
        try { handler?.Invoke(); } catch { /* 事件处理异常不影响监控 */ }
    }

    public void Dispose()
    {
        _running = false;
        if (_threadId != 0)
            NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_STOP, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
        _ready.Dispose();
        GC.KeepAlive(_wndProc);
    }
}
