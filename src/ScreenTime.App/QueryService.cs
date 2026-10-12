using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScreenTime.Core;

namespace ScreenTime.App;

/// <summary>处理来自 WebView2 前端的所有查询与设置操作。</summary>
internal static class QueryService
{
    public static object? Handle(string name, JsonElement args) => name switch
    {
        "overview" => Overview(),
        "apps" => Apps(RangeOf(args)),
        "appdetail" => AppDetail(args.GetProperty("app").GetString() ?? ""),
        "setcategory" => SetCategory(args),
        "settings" => Settings(),
        "setlimit" => SetLimit(args),
        "setidle" => SetIdle(args),
        "setautostart" => SetAutostart(args),
        "export" => Export(),
        "exportdata" => ExportData(),
        "pickimport" => PickImport(),
        "importdata" => ImportData(args),
        "restart" => Restart(),
        "checkupdate" => CheckUpdate(),
        "doupdate" => DoUpdate(),
        "live" => Live(),
        "jserror" => JsError(args),
        _ => null,
    };

    static QueryService()
    {
        UpdateService.DownloadProgress += p => App.PushToFrontend(new { type = "update-progress", percent = p });
        UpdateService.StageChanged += s => App.PushToFrontend(new { type = "update-stage", stage = s });
        UpdateService.Finished += ok => App.PushToFrontend(new { type = "update-finished", ok });
    }

    private static object CheckUpdate()
    {
        Task.Run(async () =>
        {
            try
            {
                var info = await UpdateService.CheckAsync();
                App.PushToFrontend(new { type = "update-check-result", hasUpdate = info.HasUpdate, version = info.Latest });
            }
            catch
            {
                App.PushToFrontend(new { type = "update-check-result", hasUpdate = false, error = true });
            }
        });
        return new { ok = true };
    }

    private static object DoUpdate()
    {
        if (UpdateService.IsBusy) return new { ok = false, error = "更新进行中" };
        Task.Run(async () =>
        {
            try
            {
                var info = await UpdateService.CheckAsync();
                if (info.HasUpdate && info.Url is not null)
                    await UpdateService.DownloadAndInstallAsync(info.Url, info.AssetId);
                else
                    App.PushToFrontend(new { type = "update-finished", ok = false });
            }
            catch
            {
                App.PushToFrontend(new { type = "update-finished", ok = false });
            }
        });
        return new { ok = true };
    }

    private static object JsError(JsonElement args)
    {
        var msg = args.TryGetProperty("msg", out var m) ? m.GetString() : "?";
        var at = args.TryGetProperty("at", out var a) ? a.GetString() : "?";
        Log.Write("前端JS错误: " + msg + " @ " + at);
        return new { ok = true };
    }

    private static (string Start, string End) RangeOf(JsonElement args)
    {
        var range = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("range", out var r)
            ? r.GetString() : "day";
        var today = DateTime.Now.Date;
        return range switch
        {
            "week" => (UsageTime.DayKey(UsageTime.StartOfWeek(today)), UsageTime.DayKey(today)),
            "month" => (UsageTime.DayKey(UsageTime.StartOfMonth(today)), UsageTime.DayKey(today)),
            _ => (UsageTime.DayKey(today), UsageTime.DayKey(today)),
        };
    }

    private static object Overview()
    {
        var now = DateTime.Now;
        var todayKey = UsageTime.DayKey(now);
        var yesterdayKey = UsageTime.DayKey(now.AddDays(-1));

        var totals = App.Db.GetAppTotals(todayKey);
        var todaySeconds = totals.Values.Sum();
        var yesterdaySeconds = App.Db.GetTotalSeconds(yesterdayKey);
        var pickups = App.Db.GetPickups(todayKey);
        var hourly = App.Db.GetHourly(todayKey);

        var weekStart = UsageTime.StartOfWeek(now);
        var days = UsageTime.DayKeys(weekStart, 7);
        var daily = App.Db.GetDailyTotals(days[0], days[^1]);
        var trend = days.Select(d => new
        {
            label = d[5..].Replace("-", "/"),
            date = d,
            seconds = daily.TryGetValue(d, out var v) ? v : 0,
        }).ToList();
        var weekTotal = trend.Sum(t => t.seconds);

        var topApp = "";
        if (totals.Count > 0)
        {
            var top = totals.OrderByDescending(kv => kv.Value).First();
            topApp = NameOf(top.Key);
        }

        return new
        {
            todaySeconds,
            yesterdaySeconds,
            pickups,
            weekTotal,
            topApp,
            hourly,
            trend,
            live = Live(),
        };
    }

    private static object Apps((string Start, string End) range)
    {
        var totals = App.Db.GetAppTotalsRange(range.Start, range.End);
        var names = App.Db.GetAppNames();
        var limits = App.Db.GetLimits();
        var sum = totals.Values.Sum();
        var rows = totals
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new
            {
                app = kv.Key,
                name = names.TryGetValue(kv.Key, out var n) ? n : kv.Key,
                category = Categorizer.Of(kv.Key),
                seconds = kv.Value,
                percent = sum > 0 ? Math.Round(kv.Value / sum * 100, 1) : 0,
                limitMinutes = limits.TryGetValue(kv.Key, out var m) ? m : (int?)null,
            })
            .ToList();
        return new { rows, totalSeconds = sum, rangeStart = range.Start, rangeEnd = range.End };
    }

    private static object AppDetail(string app)
    {
        var now = DateTime.Now;
        var start = UsageTime.DayKey(now.AddDays(-13));
        var end = UsageTime.DayKey(now);
        var daily = App.Db.GetAppDaily(app, start, end);
        var trend = UsageTime.DayKeys(DateTime.Parse(start), 14)
            .Select(d => new
            {
                label = d[5..].Replace("-", "/"),
                seconds = daily.TryGetValue(d, out var v) ? v : 0,
            }).ToList();
        var sites = App.Db.GetSiteTotals(UsageTime.DayKey(now), app)
            .Select(s => new { site = s.Site, seconds = s.Seconds }).ToList();
        var names = App.Db.GetAppNames();
        var limits = App.Db.GetLimits();
        return new
        {
            app,
            name = names.TryGetValue(app, out var n) ? n : app,
            category = Categorizer.Of(app),
            categories = Categorizer.Categories,
            customCategory = Categorizer.OverrideOf(app) is not null,
            trend,
            sites,
            limitMinutes = limits.TryGetValue(app, out var m) ? m : (int?)null,
        };
    }

    private static object SetCategory(JsonElement args)
    {
        var app = args.GetProperty("app").GetString() ?? "";
        var category = args.TryGetProperty("category", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(category) || category == "自动") category = null;
        else if (!Categorizer.Categories.Contains(category)) return new { ok = false, error = "未知分类" };
        App.Db.SetAppCategory(app, category);
        Categorizer.SetOverride(app, category);
        return new { ok = true, category };
    }

    private static object Settings()
    {
        var names = App.Db.GetAppNames();
        var limits = App.Db.GetLimits().Select(kv => new
        {
            app = kv.Key,
            name = names.TryGetValue(kv.Key, out var n) ? n : kv.Key,
            minutes = kv.Value,
        }).ToList();
        var version = typeof(QueryService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return new
        {
            idleSeconds = App.Tracker.IdleThresholdSec,
            autostart = Autostart.IsEnabled(),
            limits,
            dataDir = Database.DefaultPath(),
            version,
            author = "kbcrt",
        };
    }

    private static object SetLimit(JsonElement args)
    {
        var app = args.GetProperty("app").GetString() ?? "";
        int? minutes = null;
        if (args.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number)
            minutes = m.GetInt32();
        App.Db.SetLimit(app, minutes is > 0 ? minutes : null);
        App.Tracker.SetLimits(App.Db.GetLimits());
        return new { ok = true };
    }

    private static object SetIdle(JsonElement args)
    {
        var seconds = args.GetProperty("seconds").GetInt32();
        if (seconds is < 15 or > 600) seconds = 60;
        App.Db.SetSetting("idle_seconds", seconds.ToString());
        App.Tracker.IdleThresholdSec = seconds;
        return new { ok = true, idleSeconds = seconds };
    }

    private static object SetAutostart(JsonElement args)
    {
        var enabled = args.GetProperty("enabled").GetBoolean();
        Autostart.Set(enabled);
        return new { ok = true, autostart = enabled };
    }

    private static object Export()
    {
        var path = CsvExporter.Export(App.Db, 30);
        return new { ok = true, path };
    }

    private static object ExportData()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出全部数据",
                Filter = "屏幕使用时间备份 (*.screentime)|*.screentime",
                FileName = $"屏幕使用时间备份_{DateTime.Now:yyyyMMdd}.screentime",
            };
            if (dialog.ShowDialog() != true) return new { ok = false, canceled = true };
            App.Db.VacuumInto(dialog.FileName);
            Log.Write("数据已导出: " + dialog.FileName);
            return new { ok = true, path = dialog.FileName };
        }
        catch (Exception ex)
        {
            Log.Write("导出失败: " + ex);
            return new { ok = false, error = ex.Message };
        }
    }

    private static object PickImport()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择备份文件",
            Filter = "屏幕使用时间备份 (*.screentime)|*.screentime|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return new { ok = false, canceled = true };
        return new { ok = true, path = dialog.FileName };
    }

    private static object ImportData(JsonElement args)
    {
        var path = args.GetProperty("path").GetString() ?? "";
        try
        {
            if (!File.Exists(path)) return new { ok = false, error = "备份文件不存在" };
            if (!ValidateBackup(path)) return new { ok = false, error = "不是有效的屏幕使用时间备份文件" };

            var dbPath = Database.DefaultPath();
            var dir = Path.GetDirectoryName(dbPath)!;

            // 先把当前数据备份一份，防止导入损坏文件导致全丢
            var safetyCopy = Path.Combine(dir, $"screentime_导入前_{DateTime.Now:yyyyMMddHHmmss}.db");
            App.Db.VacuumInto(safetyCopy);

            App.Tracker.Dispose();
            App.Db.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var f = dbPath + suffix;
                if (File.Exists(f)) File.Delete(f);
            }
            File.Copy(path, dbPath);
            Log.Write($"数据已从 {path} 导入，导入前数据保留在 {safetyCopy}");
            return new { ok = true };
        }
        catch (Exception ex)
        {
            Log.Write("导入失败: " + ex);
            return new { ok = false, error = ex.Message };
        }
    }

    private static bool ValidateBackup(string path)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('usage','events','app_names')";
            return Convert.ToInt32(cmd.ExecuteScalar()) >= 3;
        }
        catch
        {
            return false;
        }
    }

    private static object Restart()
    {
        App.RequestRestart();
        return new { ok = true };
    }

    private static object Live()
    {
        var cur = App.Tracker.CurrentWindow;
        var names = App.Db.GetAppNames();
        return new
        {
            todaySeconds = App.Tracker.TodayTotalSeconds,
            active = App.Tracker.ActiveNow,
            app = cur == null ? null : names.TryGetValue(cur.Value.App, out var n) ? n : null,
            title = cur?.Title,
        };
    }

    private static string NameOf(string appKey)
    {
        var names = App.Db.GetAppNames();
        return names.TryGetValue(appKey, out var n) ? n : appKey;
    }
}
