using System;

namespace ScreenTime.Core;

public static class UsageTime
{
    public static string DayKey(DateTime time) => time.ToString("yyyy-MM-dd");

    public static DateTime StartOfWeek(DateTime time)
    {
        var d = time.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-diff);
    }

    public static DateTime StartOfMonth(DateTime time) => new(time.Year, time.Month, 1);

    public static string[] DayKeys(DateTime startInclusive, int days)
    {
        var keys = new string[days];
        for (var i = 0; i < days; i++) keys[i] = DayKey(startInclusive.AddDays(i));
        return keys;
    }

    public static long ToUnix(DateTime localTime) =>
        new DateTimeOffset(localTime).ToUnixTimeSeconds();

    public static int IdleSeconds => NativeMethods.GetIdleSeconds();

    public static string WindowTitle(IntPtr hWnd) => NativeMethods.GetWindowTitle(hWnd);
}
