using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScreenTime.Core;

namespace ScreenTime.App;

internal static class CsvExporter
{
    public static string Export(Database db, int days)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var file = Path.Combine(desktop, $"屏幕使用时间_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var names = db.GetAppNames();
        var start = DateTime.Now.Date.AddDays(-(days - 1));
        var sb = new StringBuilder();
        sb.AppendLine("日期,应用,分类,使用分钟");
        for (var i = 0; i < days; i++)
        {
            var day = start.AddDays(i);
            var key = UsageTime.DayKey(day);
            foreach (var kv in db.GetAppTotals(key).OrderByDescending(x => x.Value))
                sb.AppendLine($"{key},\"{Escape(names.TryGetValue(kv.Key, out var n) ? n : kv.Key)}\",{Categorizer.Of(kv.Key)},{kv.Value / 60:F1}");
        }
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        return file;
    }

    private static string Escape(string s) => s.Replace("\"", "\"\"");
}
