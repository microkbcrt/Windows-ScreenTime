using System;
using System.IO;

namespace ScreenTime.App;

/// <summary>简易日志：写入 %LOCALAPPDATA%\ScreenTime\log.txt，便于排查问题。</summary>
internal static class Log
{
    private static readonly object Gate = new();
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTime", "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}\r\n");
            }
        }
        catch
        {
            // 日志失败时静默
        }
    }
}
