using System;
using Avalonia.Controls;
using Avalonia.Threading;
using MusicPlayer.Services;

namespace MusicPlayer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 显示当前版本，便于核对更新结果
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = $"v{version?.ToString(3) ?? "1.0.0"}";
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // 首页显示完成后再启动检查，避免阻塞启动；检查失败只写日志，不打扰用户
        Dispatcher.UIThread.Post(() => _ = UpdateService.StartAsync());
    }
}
