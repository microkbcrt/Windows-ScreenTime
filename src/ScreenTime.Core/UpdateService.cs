using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScreenTime.Core;

/// <summary>
/// GitHub Releases 自更新：检查最新版本、下载安装包并静默原地升级。
/// 升级流程：下载 Setup → 静默安装（/FORCECLOSEAPPLICATIONS 关闭本应用）→ 安装器自动重启新版。
/// </summary>
public static class UpdateService
{
    public const string Repo = "microkbcrt/Windows-ScreenTime";
    private const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
    private const string AssetPrefix = "Windows-ScreenTime_Setup";

    /// <summary>GitHub 下载加速镜像（按顺序尝试，全部失败再回退 API 资产端点与直链）。</summary>
    private static readonly string[] MirrorPrefixes =
    {
        "https://gh.jjj.gv.uy/",
        "https://gh.inkchills.cn/",
    };

    /// <summary>更新流程末尾由宿主设置：退出当前进程（安装器会重新拉起新版本）。</summary>
    public static Action? ExitHost;

    public static bool IsBusy { get; private set; }

    public static event Action<int>? DownloadProgress;
    public static event Action<string>? StageChanged;
    public static event Action<bool>? Finished;

    /// <summary>检查 GitHub 上的最新版本。返回 hasUpdate=false 时 Latest 仍为最新版本号。</summary>
    public static async Task<(bool HasUpdate, string Latest, string? Notes, string? Url, long AssetId)> CheckAsync()
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Windows-ScreenTime");
        using var resp = await http.GetAsync(ReleasesApi);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);

        var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        var latest = tag.TrimStart('v', 'V');

        string? url = null;
        long assetId = 0;
        if (json.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.StartsWith(AssetPrefix, StringComparison.Ordinal) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    url = asset.GetProperty("browser_download_url").GetString();
                    assetId = asset.GetProperty("id").GetInt64();
                    break;
                }
            }
        }

        string? notes = json.RootElement.TryGetProperty("body", out var body) ? body.GetString() : null;

        var hasUpdate = false;
        if (url is not null && Version.TryParse(latest, out var latestVer))
        {
            var current = Assembly.GetEntryAssembly()?.GetName().Version;
            hasUpdate = current is null || latestVer > current;
        }
        return (hasUpdate, latest, notes, url, assetId);
    }

    public static async Task DownloadAndInstallAsync(string url, long assetId = 0)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            StageChanged?.Invoke("正在下载更新…");
            var temp = Path.Combine(Path.GetTempPath(), $"ScreenTime_Setup_{Guid.NewGuid():N}.exe");

            // 多级回退：镜像（按序） → api.github.com 资产端点 → 直链
            bool ok = false;
            if (url is not null)
            {
                foreach (var mirror in MirrorPrefixes)
                {
                    try
                    {
                        ok = await DownloadToAsync(mirror + url, temp, octetStream: false);
                        if (ok) break;
                        UsageTracker.Logger?.Invoke("镜像不可用: " + mirror);
                    }
                    catch (Exception ex) { UsageTracker.Logger?.Invoke("镜像下载失败(" + mirror + "): " + ex.Message); }
                }
            }
            if (!ok && assetId > 0)
            {
                try { ok = await DownloadToAsync($"https://api.github.com/repos/{Repo}/releases/assets/{assetId}", temp, octetStream: true); }
                catch (Exception ex) { UsageTracker.Logger?.Invoke("API资产下载失败: " + ex.Message); }
            }
            if (!ok && url is not null) ok = await DownloadToAsync(url, temp, octetStream: false);
            if (!ok) throw new IOException("下载更新失败");

            StageChanged?.Invoke("正在安装更新，应用即将自动重启…");
            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = temp,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS",
                UseShellExecute = true,
            });
            if (installer is null) throw new InvalidOperationException("无法启动安装程序");
            await Task.Delay(2000);
            Finished?.Invoke(true);
            ExitHost?.Invoke();
        }
        catch (Exception ex)
        {
            UsageTracker.Logger?.Invoke("自动更新失败: " + ex);
            Finished?.Invoke(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<bool> DownloadToAsync(string url, string destPath, bool octetStream)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(15);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Windows-ScreenTime");
        if (octetStream) http.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode) return false;
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var file = File.Create(destPath);
        var buffer = new byte[81920];
        long received = 0;
        int lastPct = -1;
        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            received += read;
            if (total > 0)
            {
                var pct = (int)(received * 100 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    DownloadProgress?.Invoke(pct);
                }
            }
        }
        return true;
    }
}
