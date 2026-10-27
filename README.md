# MusicPlayer

基于 .NET 10 + Avalonia 的跨平台音乐播放器。当前仓库是**可运行的骨架**：空白首页 + 基于 NetSparkle / GitHub Releases 的启动自动更新。

## 环境要求

- Windows 10 / 11 x64（当前发布与验证平台）
- .NET 10 SDK（10.0.4xx，已验证 10.0.401）
- 根目录的 `NuGet.Config` 是本机内网源配置，已被 `.gitignore` 忽略，不随仓库分发
- 构建前必须设置 `AVALONIA_TELEMETRY_OPTOUT=1`，避免 Avalonia 遥测写入用户目录被限制导致构建失败

## 构建与运行

```powershell
# 构建
dotnet build MusicPlayer.sln -c Release

# 本地运行（Debug 构建不检查更新，避免覆盖开发目录）
dotnet run --project src/MusicPlayer -c Debug
```

发布独立可执行目录：

```powershell
dotnet publish src/MusicPlayer/MusicPlayer.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

## 发布流程

仓库提供 GitHub Actions 的 `Release` 工作流（手动触发）：

1. Actions → `Release` → `Run workflow`
2. 输入版本号（例如 `1.0.0`，不带 `v`）
3. 工作流会：以该版本号构建并发布 win-x64 自包含包 → 打包 zip → 生成 `appcast.xml` → 创建/更新 GitHub Release 并上传附件

**更新包 zip 要求**：zip 根目录直接是 `MusicPlayer.exe` 等发布文件，不要多套一层文件夹（更新脚本会直接解压到安装目录）。

## 自动更新机制

- **检查时机**：首页 `MainWindow.Opened` 后，通过 `Dispatcher.UIThread.Post` 启动 NetSparkle 后台循环 `StartLoop(doInitialCheck: true, forceInitialCheck: true, 24 小时)`：启动时立即检查一次，之后每 24 小时检查一次；只有发现新版本时才显示内置 Avalonia 更新窗口，平时完全静默。
- **appcast 地址**：默认 `https://github.com/Lwc-LiuWenCheng/MusicPlayer/releases/latest/download/appcast.xml`，可用环境变量 `MUSICPLAYER_APPCAST_URL` 覆盖（测试用；只支持 http/https，`file://` 路径会被 NetSparkle 拒绝，本地联调请起一个本地 HTTP 服务）。
- **版本来源**：程序集版本（`Version=1.0.0`），首页右下角显示 `v{版本}` 便于核对更新是否生效。
- **签名**：当前使用 `Ed25519Checker(SecurityMode.Unsafe, "", "")` 跳过签名校验；后续接入 Ed25519 公私钥后应改为 `SecurityMode.Strict`，并在发布流程中为 appcast 生成签名文件。
- **Windows 安装方式**：NetSparkle 在 Windows 只支持 `.exe/.msi/.msp` 安装器，不会解压 zip。应用订阅 `InstallerProcessAboutToStart`：下载完成后生成 `%TEMP%\MusicPlayer-apply-update.cmd`，等旧进程退出后用系统自带 `tar.exe` 解压覆盖安装目录并重启应用，脚本日志在 `%TEMP%\MusicPlayer-update.log`。
- **应用日志**：`<安装目录>\logs\update.log`（检查结果、失败原因、NetSparkle 诊断信息，写入失败会被忽略）。
- **配置状态**：`<安装目录>\update-state.json`。主动避开注册表配置，受限环境（注册表不可写）下也不会崩溃；同时关闭 NetSparkle 的「首次运行跳过检查」，保证首次启动就检查。
- **失败处理**：离线、appcast 404、目录不可写等情况只写日志，不弹错误、不影响启动。
- **DEBUG 构建**：直接跳过更新检查。

### 安装位置注意事项

- 必须安装在**当前用户可写**的目录（例如 `%LOCALAPPDATA%\MusicPlayer` 或用户自选目录）。
- **不要放在 `Program Files` 等需要管理员权限的位置**：更新脚本无法覆盖文件，更新会失败。

## 相关文档

- [方案：项目框架与自动更新](docs/方案-项目框架与自动更新.md)

## 目录结构

```
MusicPlayer.sln
src/MusicPlayer/
  Program.cs                  Avalonia 入口
  App.axaml(.cs)              Fluent 主题与桌面生命周期
  Views/MainWindow.axaml(.cs) 空白首页（右下角版本号）
  Services/UpdateService.cs   启动更新检查 + zip 覆盖安装
  app.manifest                Windows 清单（高 DPI）
```
