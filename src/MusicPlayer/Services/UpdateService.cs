using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NetSparkleUpdater;
using NetSparkleUpdater.AssemblyAccessors;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.Avalonia;

namespace MusicPlayer.Services;

/// <summary>
/// 启动后静默检查更新：后台循环检查 appcast，发现新版本时才弹 NetSparkle 标准更新窗口。
/// </summary>
internal static class UpdateService
{
    // 默认指向 GitHub Releases 的 latest 重定向；可通过环境变量覆盖（便于测试）
    private const string AppCastUrl =
        "https://github.com/Lwc-LiuWenCheng/MusicPlayer/releases/latest/download/appcast.xml";

#if !DEBUG
    // 必须保留实例：更新检查循环与更新窗口都依赖它，被回收会导致更新流程中断
    private static SparkleUpdater? _sparkle;

    // 防止重复触发安装脚本
    private static bool _updateScriptStarted;
#endif

    public static async Task StartAsync()
    {
#if DEBUG
        // 开发构建不检查更新，避免调试版本被 Release 更新包覆盖
        return;
#else
        try
        {
            var appCastUrl = Environment.GetEnvironmentVariable("MUSICPLAYER_APPCAST_URL");
            if (string.IsNullOrWhiteSpace(appCastUrl))
            {
                appCastUrl = AppCastUrl;
            }

            // 目前不校验签名；后续发布 Ed25519 公钥后改为 SecurityMode.Strict
            var verifier = new Ed25519Checker(SecurityMode.Unsafe, "", "");
            // AssemblyDiagnosticsAccessor 需要真实文件路径（传程序集名会抛 FileNotFoundException）
            var accessor = new AssemblyDiagnosticsAccessor(Environment.ProcessPath);
            _sparkle = new SparkleUpdater(appCastUrl, verifier, Environment.ProcessPath, new UIFactory())
            {
                RelaunchAfterUpdate = true,
                LogWriter = new FileLogWriter(),
            };

            // 默认的 RegistryConfiguration 在受限环境/无注册表权限时会抛异常导致进程崩溃，
            // 这里改用程序目录下的 JSON 配置
            var statePath = Path.Combine(AppContext.BaseDirectory, "update-state.json");
            _sparkle.Configuration = new JSONConfiguration(accessor, statePath);
            if (_sparkle.Configuration.IsFirstRun)
            {
                // NetSparkle 首次运行会跳过检查（构造时已把 DidRunOnce 写入状态文件），重载一次即可立即检查
                _sparkle.Configuration = new JSONConfiguration(accessor, statePath);
            }

            // Windows 上 NetSparkle 不能直接执行 zip 更新包，由我们接管解压与重启
            _sparkle.InstallerProcessAboutToStart += OnInstallerProcessAboutToStart;

            Log($"启动更新检查：{appCastUrl}");
            // 启动时立刻检查一次（不受 24 小时间隔限制），之后每 24 小时检查一次
            await _sparkle.StartLoop(doInitialCheck: true, forceInitialCheck: true, TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            // 离线、appcast 缺失、配置不可写等都在这里兜底，不向用户弹错误
            Log($"启动更新检查失败：{ex.GetType().Name}: {ex.Message}");
        }
#endif
    }

#if !DEBUG
    /// <summary>
    /// 下载完成后（downloadFilePath 即下载好的 zip）：生成批处理，等本进程退出后解压覆盖并重启。
    /// 返回 false 表示不使用 NetSparkle 默认安装方式（Windows 只能执行 exe/msi/msp）。
    /// </summary>
    private static bool OnInstallerProcessAboutToStart(Process process, string downloadFilePath)
    {
        try
        {
            if (_updateScriptStarted)
            {
                return false;
            }

            _updateScriptStarted = true;

            var targetDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var appPath = Path.Combine(targetDir, "MusicPlayer.exe");
            var scriptPath = Path.Combine(Path.GetTempPath(), "MusicPlayer-apply-update.cmd");
            var installLog = Path.Combine(Path.GetTempPath(), "MusicPlayer-update.log");
            var pid = Environment.ProcessId;

            // 等旧进程退出 -> 用系统自带 tar.exe 解压覆盖 -> 重启；所有路径加引号
            var script = $"""
                @echo off
                chcp 65001 >nul
                setlocal
                set "ZIP={downloadFilePath}"
                set "TARGET={targetDir}"
                set "APP={appPath}"
                set "LOG={installLog}"
                >>"%LOG%" echo [%date% %time%] waiting for process {pid} to exit
                for /l %%i in (1,1,60) do (
                  tasklist /FI "PID eq {pid}" /FI "IMAGENAME eq MusicPlayer.exe" /NH | find /I "MusicPlayer.exe" >nul
                  if errorlevel 1 goto unpack
                  timeout /t 1 /nobreak >nul
                )
                :unpack
                >>"%LOG%" echo [%date% %time%] extracting "%ZIP%" to "%TARGET%"
                "%SystemRoot%\System32\tar.exe" -xf "%ZIP%" -C "%TARGET%"
                if errorlevel 1 (>>"%LOG%" echo [%date% %time%] extract failed)
                start "" "%APP%"
                >>"%LOG%" echo [%date% %time%] app started
                (goto) 2>nul & del "%~f0"
                """;

            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            // 隐藏窗口启动脚本；UseShellExecute 避免继承当前进程的标准句柄
            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            Log($"更新包已下载，已启动解压脚本：{scriptPath}");

            // 返回 false 时 NetSparkle 不会退出应用，这里主动关闭，脚本才能覆盖文件
            Dispatcher.UIThread.Post(() =>
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
            return false;
        }
        catch (Exception ex)
        {
            // 目录不可写、脚本启动失败等：只记日志，不崩溃
            Log($"启动更新脚本失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
#endif

    // 日志写入程序目录下的 logs/update.log，失败不影响主流程
    private static void Log(string message)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "update.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 忽略日志写入失败（例如安装目录不可写）
        }
    }

    // 把 NetSparkle 自身的诊断日志也写进 update.log
    private sealed class FileLogWriter : ILogger
    {
        public void PrintMessage(string message, params object[]? arguments) =>
            Log(arguments is { Length: > 0 } ? string.Format(message, arguments) : message);
    }
}
