using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ScreenTime.Core;

/// <summary>
/// 计时引擎：每秒轮询前台窗口，三重判定（未锁屏 + 显示器点亮 + 无键鼠空闲）后才计入使用时长。
/// 数据按「日期 + 分钟 + 应用 + 网站」聚合并周期性写入 SQLite。
/// </summary>
public sealed class UsageTracker : IDisposable
{
    public sealed record UsageKey(string Date, int Minute, string App, string Site);

    public sealed record AppSnapshot(string Key, string Name, string Title, IntPtr Hwnd);

    private readonly Database _db;
    private readonly SessionMonitor _session = new();
    private readonly BrowserTracker _browser = new();
    private readonly ConcurrentDictionary<string, string> _nameCache = new();

    /// <summary>诊断日志钩子（App 启动时注入）。</summary>
    public static Action<string>? Logger;

    /// <summary>键鼠空闲多少秒后判定为「离开」（可运行时调整）。</summary>
    public int IdleThresholdSec { get; set; }

    private Thread? _thread;
    private volatile bool _running;

    private volatile bool _locked;
    private volatile bool _displayOn = true;
    private volatile bool _suspended;

    private readonly object _stateGate = new();
    private (string App, string Site)? _lastKey;
    private string _lastTitle = "";
    private DateTime _lastTick = DateTime.Now;
    private DateTime _lastFlush = DateTime.Now;
    private readonly Dictionary<UsageKey, double> _buffer = new();

    private readonly object _todayGate = new();
    private readonly Dictionary<string, double> _today = new();
    private readonly HashSet<string> _namesPersisted = new();
    private readonly HashSet<string> _limitNotified = new();
    private Dictionary<string, int> _limits = new();
    private string _currentDate = UsageTime.DayKey(DateTime.Now);

    /// <summary>每次入库后触发（UI / 托盘刷新）。</summary>
    public event Action? DataChanged;

    /// <summary>应用超过每日限额时触发（应用Key, 应用显示名）。</summary>
    public event Action<string, string>? LimitExceeded;

    public UsageTracker(Database db, int idleThresholdSeconds = 60)
    {
        _db = db;
        IdleThresholdSec = idleThresholdSeconds;
    }

    public void Start()
    {
        foreach (var kv in _db.GetAppTotals(_currentDate)) _today[kv.Key] = kv.Value;
        foreach (var name in _db.GetAppNames()) _namesPersisted.Add(name.Key);
        _limits = _db.GetLimits();

        _session.Locked += OnLocked;
        _session.Unlocked += OnUnlocked;
        _session.DisplayOn += OnDisplayOn;
        _session.DisplayOff += OnDisplayOff;
        _session.Suspended += OnSuspended;
        _session.Resumed += OnResumed;
        _session.Start();

        _running = true;
        _lastTick = DateTime.Now;
        _lastFlush = DateTime.Now;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ScreenTime.Tracker" };
        _thread.Start();
    }

    private void Loop()
    {
        while (_running)
        {
            var sw = Stopwatch.StartNew();
            try { Tick(); } catch (Exception ex) { Debug.WriteLine(ex); }
            var workMs = (int)sw.ElapsedMilliseconds;
            Thread.Sleep(Math.Max(50, 1000 - workMs));
        }
        try { Flush(); } catch { /* 退出时尽力落盘 */ }
    }

    private void Tick()
    {
        var now = DateTime.Now;
        double dt;
        lock (_stateGate)
        {
            dt = (now - _lastTick).TotalSeconds;
            _lastTick = now;
        }
        if (dt is < 0 or > 30) dt = 0;

        // 日期切换：清空今日内存统计
        var dayKey = UsageTime.DayKey(now);
        if (dayKey != _currentDate)
        {
            _currentDate = dayKey;
            lock (_todayGate)
            {
                _today.Clear();
                _limitNotified.Clear();
            }
        }

        var active = _running && !_locked && _displayOn && !_suspended &&
                     NativeMethods.GetIdleSeconds() < IdleThresholdSec;

        var fg = active ? SnapshotForeground() : null;
        var site = "";
        if (fg != null && _browser.IsBrowser(fg.Key))
            site = _browser.ResolveSite(fg.Hwnd, fg.Title) ?? "";

        (string, string)? key = fg is null ? null : (fg.Key, site);
        bool changed;
        lock (_stateGate)
        {
            changed = key != _lastKey;
            _lastKey = key;
            _lastTitle = fg?.Title ?? _lastTitle;
        }

        if (key != null && dt > 0)
        {
            var usageKey = new UsageKey(dayKey, now.Hour * 60 + now.Minute, key.Value.Item1, key.Value.Item2);
            lock (_stateGate)
            {
                _buffer[usageKey] = _buffer.TryGetValue(usageKey, out var v) ? v + dt : dt;
            }
        }

        if (changed || (now - _lastFlush).TotalSeconds >= 10)
            Flush();
    }

    private void Flush()
    {
        List<UsageEntry> entries;
        lock (_stateGate)
        {
            if (_buffer.Count == 0)
            {
                _lastFlush = DateTime.Now;
                return;
            }
            entries = new List<UsageEntry>(_buffer.Count);
            foreach (var (k, v) in _buffer)
                entries.Add(new UsageEntry(k.Date, k.Minute, k.App, k.Site, v));
            _buffer.Clear();
            _lastFlush = DateTime.Now;
        }

        var names = new Dictionary<string, string>();
        lock (_todayGate)
        {
            foreach (var e in entries)
            {
                _today[e.App] = _today.TryGetValue(e.App, out var t) ? t + e.Seconds : e.Seconds;
                if (!_namesPersisted.Contains(e.App) && NameOf(e.App) is { } name)
                {
                    _namesPersisted.Add(e.App);
                    names[e.App] = name;
                }
            }
        }

        _db.FlushUsage(entries, names);
        CheckLimits();
        DataChanged?.Invoke();
    }

    private void CheckLimits()
    {
        Dictionary<string, int> limits = _limits;
        foreach (var (app, minutes) in limits)
        {
            bool notify;
            lock (_todayGate)
            {
                if (_limitNotified.Contains(app)) continue;
                notify = _today.TryGetValue(app, out var sec) && sec >= minutes * 60;
                if (notify) _limitNotified.Add(app);
            }
            if (notify)
                LimitExceeded?.Invoke(app, NameOf(app) ?? app);
        }
    }

    public void SetLimits(Dictionary<string, int> limits)
    {
        _limits = limits;
    }

    private void OnLocked()
    {
        _locked = true;
        _db.AddEvent(UsageTime.ToUnix(DateTime.Now), "lock");
    }

    private void OnUnlocked()
    {
        _locked = false;
        var now = UsageTime.ToUnix(DateTime.Now);
        _db.AddEvent(now, "unlock");
        _db.AddEvent(now, "pickup");
    }

    private void OnDisplayOff()
    {
        if (_displayOn) _db.AddEvent(UsageTime.ToUnix(DateTime.Now), "display_off");
        _displayOn = false;
    }

    private void OnDisplayOn()
    {
        _displayOn = true;
        if (!_locked && !_suspended)
        {
            var now = UsageTime.ToUnix(DateTime.Now);
            _db.AddEvent(now, "display_on");
            _db.AddEvent(now, "pickup");
        }
    }

    private void OnSuspended()
    {
        _suspended = true;
        _displayOn = false;
        _db.AddEvent(UsageTime.ToUnix(DateTime.Now), "sleep");
    }

    private void OnResumed()
    {
        _suspended = false;
        _lastTick = DateTime.Now;
        _db.AddEvent(UsageTime.ToUnix(DateTime.Now), "resume");
    }

    private AppSnapshot? SnapshotForeground()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            if (NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0) return null;

            var path = NativeMethods.GetProcessImagePath((int)pid);
            if (path != null &&
                string.Equals(Path.GetFileName(path), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                // UWP 宿主进程：取内部 CoreWindow 子窗口的真实应用进程
                var child = NativeMethods.FindWindowExW(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                if (child != IntPtr.Zero &&
                    NativeMethods.GetWindowThreadProcessId(child, out var childPid) != 0 && childPid != 0)
                {
                    var childPath = NativeMethods.GetProcessImagePath((int)childPid);
                    if (childPath != null) path = childPath;
                }
            }

            var title = NativeMethods.GetWindowTitle(hwnd);
            if (path == null)
                return new AppSnapshot("pid:" + pid, "未知程序", title, hwnd);

            var key = path.ToLowerInvariant();
            return new AppSnapshot(key, NameOf(key) ?? Path.GetFileName(path), title, hwnd);
        }
        catch
        {
            return null;
        }
    }

    private string? NameOf(string appKey)
    {
        return _nameCache.GetOrAdd(appKey, key =>
        {
            try
            {
                if (key.StartsWith("pid:") || !File.Exists(key)) return key;
                var info = FileVersionInfo.GetVersionInfo(key);
                var name = (info.FileDescription ?? "").Trim();
                if (name.Length == 0) name = (info.ProductName ?? "").Trim();
                return name.Length == 0 ? Path.GetFileNameWithoutExtension(key) : name;
            }
            catch
            {
                try { return Path.GetFileName(key); } catch { return key; }
            }
        });
    }

    /// <summary>当前是否处于「计入屏幕时间」状态。</summary>
    public bool ActiveNow =>
        _running && !_locked && _displayOn && !_suspended &&
        NativeMethods.GetIdleSeconds() < IdleThresholdSec;

    /// <summary>当前前台（应用Key, 窗口标题），无活动为 null。</summary>
    public (string App, string Title)? CurrentWindow
    {
        get
        {
            lock (_stateGate)
            {
                return _lastKey is null ? null : (_lastKey.Value.App, _lastTitle);
            }
        }
    }

    public double TodayTotalSeconds
    {
        get
        {
            lock (_todayGate)
            {
                return _today.Values.Sum();
            }
        }
    }

    public Dictionary<string, double> GetTodayTotals()
    {
        lock (_todayGate)
        {
            return new Dictionary<string, double>(_today);
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(3000);
        _session.Dispose();
    }
}
