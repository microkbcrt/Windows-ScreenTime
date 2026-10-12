using ScreenTime.Core;
using System.Diagnostics;

// summary 模式：打印今日统计后退出
if (args.Length > 0 && args[0] == "summary")
{
    using var db2 = new Database(Database.DefaultPath());
    var today = UsageTime.DayKey(DateTime.Now);
    Console.WriteLine($"日期: {today}");
    var totals = db2.GetAppTotals(today);
    foreach (var kv in totals.OrderByDescending(x => x.Value))
        Console.WriteLine($"  {kv.Key,-70} {kv.Value / 60.0,8:F1} 分钟");
    var sites = new List<(string, double)>();
    Console.WriteLine("网站级统计（今日）:");
    foreach (var kv in totals)
    {
        if (!Path.GetFileName(kv.Key).StartsWith("msedge")
            && !Path.GetFileName(kv.Key).StartsWith("chrome")
            && !Path.GetFileName(kv.Key).StartsWith("firefox")) continue;
        foreach (var s in db2.GetSiteTotals(today, kv.Key))
            Console.WriteLine($"  {Path.GetFileName(kv.Key),-10} {s.Item1,-30} {s.Item2 / 60.0,6:F1} 分钟");
    }
    Console.WriteLine($"总屏幕时间: {db2.GetTotalSeconds(today) / 60.0:F1} 分钟    拿起次数: {db2.GetPickups(today)}");
    Console.WriteLine("最近事件:");
    foreach (var (ts, type) in db2.GetEvents(today).TakeLast(15))
        Console.WriteLine($"  {DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime():T}  {type}");
    return;
}

// sitetest 模式：对当前打开的浏览器窗口做一次网站识别测试
if (args.Length > 0 && args[0] == "sitetest")
{
    UsageTracker.Logger = m => Console.WriteLine("  [log] " + m);
    using var bt = new BrowserTracker();
    var found = false;
    foreach (var pname in new[] { "msedge", "chrome", "firefox", "brave" })
    {
        foreach (var p in Process.GetProcessesByName(pname))
        {
            var hwnd = p.MainWindowHandle;
            if (hwnd == IntPtr.Zero) continue;
            found = true;
            var title = UsageTime.WindowTitle(hwnd);
            Console.WriteLine($"浏览器: {pname}  标题: {title}");
            var first = bt.ResolveSite(hwnd, title);
            Console.WriteLine($"  第一次返回: {first ?? "(null，后台识别中)"}");
            Thread.Sleep(4000);
            var second = bt.ResolveSite(hwnd, title);
            Console.WriteLine($"  4秒后返回: {second ?? "(null)"}");
        }
    }
    if (!found) Console.WriteLine("未找到打开的浏览器窗口");
    return;
}

// updatetest 模式：走一遍真实的「检查更新 → 下载 → 静默安装」流程
if (args.Length > 0 && args[0] == "updatetest")
{
    Console.WriteLine("检查更新…");
    var info = await UpdateService.CheckAsync();
    Console.WriteLine($"最新版本: {info.Latest}    有更新: {info.HasUpdate}");
    Console.WriteLine($"安装包: {info.Url}");
    if (info is { HasUpdate: true, Url: not null })
    {
        UpdateService.DownloadProgress += p => Console.Write($"\r下载进度: {p}%   ");
        UpdateService.Finished += ok => Console.WriteLine($"\r安装程序已启动 (成功={ok})          ");
        await UpdateService.DownloadAndInstallAsync(info.Url);
    }
    return;
}

using var db = new Database(Database.DefaultPath());
using var tracker = new UsageTracker(db, idleThresholdSeconds: 60);
tracker.Start();
Console.WriteLine("采集运行中，Ctrl+C 停止……");
var exit = false;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exit = true;
};
while (!exit)
{
    Thread.Sleep(5000);
    var cur = tracker.CurrentWindow;
    var idle = UsageTime.IdleSeconds;
    Console.WriteLine(
        $"{DateTime.Now:T}  前台: {(cur?.App ?? "-"),-55}  今日 {tracker.TodayTotalSeconds / 60,7:F1} 分钟  空闲 {idle,3}s  {cur?.Title}");
}
tracker.Dispose();
Console.WriteLine("已停止，数据已入库。");
