using Avalonia;

namespace MusicPlayer;

internal static class Program
{
    // Avalonia 初始化之前不要调用任何 Avalonia / 第三方 API。
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // 可视化设计器也会调用此方法，不要移除。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
