using System;
using System.Drawing;
using System.Windows;
using ScreenTime.Core;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ScreenTime.App;

public partial class App : Application
{
    public static Database Db { get; private set; } = null!;
    public static UsageTracker Tracker { get; private set; } = null!;
    public static bool Exiting { get; private set; }

    /// <summary>后台推送到前端的消息通道（更新进度、检查结果等）。</summary>
    public static event Action<object>? FrontendPush;

    public static void PushToFrontend(object message) => FrontendPush?.Invoke(message);

    private static System.Windows.Forms.NotifyIcon? _tray;
    private static System.Windows.Forms.ToolStripMenuItem? _trayTodayItem;
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：重复启动直接激活提示后退出
        _singleInstance = new Mutex(true, "Local\\ScreenTimeApp.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("屏幕使用时间已在运行中（可查看系统托盘图标）。", "屏幕使用时间",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        System.Windows.Application.Current.DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("UI异常: " + args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("未处理异常: " + args.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("任务异常: " + args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        UsageTracker.Logger = m => Log.Write(m);
        Db = new Database(Database.DefaultPath());
        Categorizer.SetOverrides(Db.GetAppCategories());
        var idle = 60;
        if (int.TryParse(Db.GetSetting("idle_seconds"), out var saved) && saved is >= 15 and <= 600)
            idle = saved;

        Tracker = new UsageTracker(Db, idle);
        Tracker.LimitExceeded += (_, appName) =>
            Dispatcher.BeginInvoke(() => new ReminderWindow(appName).Show());
        Tracker.Start();

        InitTray();
        UpdateService.ExitHost = ExitForUpdate;

        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            Log.Write("应用以静默模式启动");
        }
        else
        {
            new MainWindow().Show();
        }

        // 启动时检查更新（延迟几秒，避免抢占启动）
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000);
                var info = await UpdateService.CheckAsync();
                if (info.HasUpdate)
                    PushToFrontend(new { type = "update-available", version = info.Latest, notes = info.Notes });
            }
            catch { /* 网络不可达时静默跳过，下次启动再查 */ }
        });
    }

    public static void ExitForUpdate()
    {
        if (Exiting) return;
        Exiting = true;
        try
        {
            Tracker.Dispose();
            Db.Dispose();
        }
        catch { }
        if (_tray != null) _tray.Visible = false;
        Current.Dispatcher.BeginInvoke(new Action(() => Current.Shutdown()));
    }

    public static void RequestRestart()
    {
        if (Exiting) return;
        Exiting = true;
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                System.Diagnostics.Process.Start(Environment.ProcessPath);
        }
        catch (Exception ex)
        {
            Log.Write("重启失败: " + ex);
        }
        Current.Dispatcher.BeginInvoke(new Action(() => Current.Shutdown()));
    }

    private void InitTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "屏幕使用时间",
            Visible = true,
            Icon = MakeIcon(),
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开仪表盘", null, (_, _) => ShowMain());
        _trayTodayItem = new System.Windows.Forms.ToolStripMenuItem("今日 0 分钟") { Enabled = false };
        menu.Items.Add(_trayTodayItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();

        UpdateTray();
        Tracker.DataChanged += () => Dispatcher.BeginInvoke(UpdateTray);
    }

    private void UpdateTray()
    {
        if (_tray is null || _trayTodayItem is null) return;
        var minutes = Tracker.TodayTotalSeconds / 60;
        var text = $"今日 {(int)minutes} 分钟";
        _trayTodayItem.Text = text;
        _tray.Text = $"屏幕使用时间 — {text}";
    }

    private void ShowMain()
    {
        foreach (var w in Windows)
        {
            if (w is MainWindow mw && mw.IsLoaded)
            {
                mw.Show();
                if (mw.WindowState == WindowState.Minimized)
                    mw.WindowState = WindowState.Normal;
                mw.Activate();
                return;
            }
        }
        var win = new MainWindow();
        win.Show();
        win.Activate();
    }

    private void ExitApp()
    {
        Exiting = true;
        Tracker.Dispose();
        Db.Dispose();
        if (_tray != null) _tray.Visible = false;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) _tray.Visible = false;
        base.OnExit(e);
    }

    private static Icon MakeIcon()
    {
        // 优先使用 exe 内嵌图标（与资源管理器、任务栏一致）
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
            {
                var embedded = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (embedded != null) return embedded;
            }
        }
        catch { /* 回退到运行时绘制 */ }
        try
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(10, 132, 255));
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 3);
            pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawLine(pen, 16, 16, 16, 9);
            g.DrawLine(pen, 16, 16, 22, 19);
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
