using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ScreenTime.App;

public partial class ReminderWindow : Window
{
    public ReminderWindow(string appName)
    {
        InitializeComponent();
        MessageText.Text = $"「{appName}」今日使用已达限额";
        SubText.Text = "注意休息，合理安排使用时间。";
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 20;
            Top = area.Bottom - Height - 20;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        };
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e) => Close();
}
