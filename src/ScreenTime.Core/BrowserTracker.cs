using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ScreenTime.Core;

/// <summary>
/// 通过 UI Automation 读取浏览器前台标签页，提取网站域名。
/// UIA 查询由专用 STA 线程执行；单个查询卡死时由看门狗重置并换新线程（旧线程遗弃为后台）。
/// 窗口标题变化会立即触发重新识别；识别未确认前不沿用旧域名，保证归属准确。
/// </summary>
public sealed class BrowserTracker : IDisposable
{
    private static readonly HashSet<string> BrowserExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx",
        "vivaldi", "360se", "360chrome", "qqbrowser", "sogou_explorer",
    };

    private sealed class CacheEntry
    {
        public string? Site;
        public DateTime At;
        public string Title = "";
        public bool InFlight;
    }

    private sealed record WorkItem(IntPtr Hwnd, string Title);

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);
    private static readonly int HangTimeoutSec = 8;

    private readonly Dictionary<IntPtr, CacheEntry> _cache = new();
    private readonly object _gate = new();
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly object _workerGate = new();

    private Thread? _worker;
    private int _generation;
    private WorkItem? _current;
    private DateTime _currentStart;

    public BrowserTracker()
    {
        EnsureWorker();
        var watchdog = new Thread(WatchdogLoop) { IsBackground = true, Name = "ScreenTime.UIA.Watchdog" };
        watchdog.Start();
    }

    public bool IsBrowser(string appKey)
    {
        try
        {
            return BrowserExes.Contains(Path.GetFileNameWithoutExtension(appKey));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 返回当前标签页的域名（如 "bilibili.com"）。
    /// 识别尚未完成或失败时返回 null（按应用整体计时，不误归属到旧网站）。
    /// </summary>
    public string? ResolveSite(IntPtr hwnd, string title)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(hwnd, out var found) || found is null)
            {
                found = new CacheEntry();
                _cache[hwnd] = found;
            }
            var sameTitle = string.Equals(found.Title, title, StringComparison.Ordinal);
            if (found.InFlight) return sameTitle ? found.Site : null;
            if (DateTime.Now - found.At < Ttl && sameTitle) return found.Site;
            found.InFlight = true;
        }

        try
        {
            _queue.TryAdd(new WorkItem(hwnd, title));
        }
        catch
        {
            lock (_gate) { if (_cache.TryGetValue(hwnd, out var e)) e.InFlight = false; }
        }
        return null;
    }

    private void EnsureWorker()
    {
        lock (_workerGate)
        {
            // 无条件换代：旧线程若卡死会遗弃在后台，醒来后检测到代次不符自动退出
            _generation++;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ScreenTime.UIA" };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start(_generation);
        }
    }

    private void WorkerLoop(object? generationObj)
    {
        var myGen = (int)generationObj!;
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                if (myGen != Volatile.Read(ref _generation)) return;
                lock (_gate)
                {
                    _current = item;
                    _currentStart = DateTime.Now;
                }

                string? site = null;
                try
                {
                    site = ResolveNow(item.Hwnd);
                }
                catch (Exception ex)
                {
                    UsageTracker.Logger?.Invoke($"UIA查询失败 hwnd=0x{item.Hwnd.ToInt64():X}: {ex.Message}");
                }

                if (myGen != Volatile.Read(ref _generation)) return;

                lock (_gate)
                {
                    _current = null;
                    if (_cache.TryGetValue(item.Hwnd, out var e))
                    {
                        e.Site = site;
                        e.At = DateTime.Now;
                        e.Title = item.Title;
                        e.InFlight = false;
                    }
                    if (_cache.Count > 64) Cleanup();
                }
            }
        }
        catch (Exception ex)
        {
            UsageTracker.Logger?.Invoke("UIA工作线程异常退出: " + ex.Message);
        }
    }

    private void WatchdogLoop()
    {
        while (true)
        {
            Thread.Sleep(2000);
            WorkItem? current;
            DateTime start;
            lock (_gate)
            {
                current = _current;
                start = _currentStart;
            }
            if (current is null) continue;
            if ((DateTime.Now - start).TotalSeconds <= HangTimeoutSec) continue;

            UsageTracker.Logger?.Invoke($"UIA查询超过{HangTimeoutSec}秒未返回，重置并重启UIA线程（标题: {current.Title}）");
            lock (_gate)
            {
                _current = null;
                if (_cache.TryGetValue(current.Hwnd, out var e))
                {
                    e.InFlight = false;
                    e.At = DateTime.Now;
                    e.Title = current.Title;
                    e.Site = null;
                }
            }
            EnsureWorker();
        }
    }

    private static string? ResolveNow(IntPtr hwnd)
    {
        var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
        if (root is null) return null;

        // 地址栏：找到值以 http(s):// 开头的编辑框（跨浏览器、跨语言通用）
        var edits = root.FindAll(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.ControlTypeProperty,
                System.Windows.Automation.ControlType.Edit));
        string? url = null;
        foreach (System.Windows.Automation.AutomationElement edit in edits)
        {
            try
            {
                if (!edit.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var pattern)) continue;
                var value = ((System.Windows.Automation.ValuePattern)pattern).Current.Value ?? string.Empty;
                if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = value;
                    break;
                }
            }
            catch
            {
                // 单个元素读取失败继续尝试
            }
        }

        if (url is null)
        {
            var doc = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Document));
            var title = doc?.Current.Name;
            if (!string.IsNullOrEmpty(title) &&
                (title.Contains("新标签页") || title.Contains("新选项卡") || title.Contains("New Tab")))
                return "新标签页";
            return null;
        }
        return DomainOf(url);
    }

    public static string? DomainOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        return host.Length == 0 ? null : host;
    }

    private void Cleanup()
    {
        var now = DateTime.Now;
        var stale = new List<IntPtr>();
        foreach (var (hwnd, entry) in _cache)
            if (now - entry.At > MaxAge) stale.Add(hwnd);
        foreach (var hwnd in stale) _cache.Remove(hwnd);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
    }
}
