using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ScreenTime.Core;
using MessageBox = System.Windows.MessageBox;

namespace ScreenTime.App;

public partial class MainWindow : Window
{
    private bool _webReady;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public MainWindow()
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 16);
        Width = Math.Min(Width, area.Width - 24);
        Closing += (_, e) =>
        {
            if (App.Exiting) return;
            e.Cancel = true;
            Hide();
        };
        Loaded += async (_, _) => await InitWeb();
    }

    private async Task InitWeb()
    {
        if (_webReady) return;
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ScreenTime", "WebView2"));
            await Web.EnsureCoreWebView2Async(env);

        Web.CoreWebView2.WebMessageReceived += OnWebMessage;
        var index = Path.Combine(folder, "index.html");
        Web.CoreWebView2.Navigate(new Uri(index).AbsoluteUri);

        App.FrontendPush += msg => Dispatcher.BeginInvoke(() =>
        {
            if (_webReady) Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg, JsonOpts));
        });
        App.Tracker.DataChanged += OnDataChanged;
        _webReady = true;
        }
        catch (Exception ex)
        {
            Log.Write("WebView2 初始化失败: " + ex);
            MessageBox.Show(this, "WebView2 初始化失败：" + ex.Message, "屏幕使用时间",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDataChanged()
    {
        if (!_webReady) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_webReady) Web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"refresh\"}");
        });
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        int id;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            id = doc.RootElement.GetProperty("id").GetInt32();
            var name = doc.RootElement.GetProperty("name").GetString() ?? "";
            var args = doc.RootElement.TryGetProperty("args", out var a) ? a.Clone() : default;

            if (name != "live") Log.Write($"RPC -> {name}");

            var data = QueryService.Handle(name, args);
            var ok = new { id, ok = true, data };
            Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(ok, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Write("RPC处理异常: " + ex);
            var err = new { id = 0, ok = false, error = ex.Message };
            Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(err, JsonOpts));
        }
    }
}
