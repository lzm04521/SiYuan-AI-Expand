using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SiYuanSync.App.Autostart;
using SiYuanSync.App.Update;
using SiYuanSync.Core.Paths;

namespace SiYuanSync.App.Web.Endpoints;

/// <summary>
/// 系统信息与升级端点：版本/仓库地址、检查更新、应用更新、开机自启。
/// 升级流程与托盘设置窗口共用同一套 UpdateChecker + Updater，两入口等价；
/// 开机自启直接读写 HKCU Run 键，AutostartService 由 DI 注入单例。
/// </summary>
public static class SystemEndpoints
{
    public static void Map(IEndpointRouteBuilder app, AutostartService autostart)
    {
        app.MapGet("/api/system/info", () => Results.Json(new
        {
            version = AppVersion.CurrentString,
            repoUrl = AppConstants.RepoUrl,
            uptimeSeconds = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
            workingSetBytes = Environment.WorkingSet
        }));

        // 开机自启状态：非 Windows 平台 supported=false、enabled=false
        app.MapGet("/api/system/autostart", () =>
        {
            bool supported = OperatingSystem.IsWindows();
            bool enabled = false;
            if (supported)
            {
                try { enabled = autostart.IsEnabled(); } catch { enabled = false; }
            }
            return Results.Json(new { supported, enabled });
        });

        // 开机自启开关：非 Windows 拒绝；写注册表失败回传错误信息
        app.MapPut("/api/system/autostart", (AutostartDto body) =>
        {
            if (!OperatingSystem.IsWindows())
                return Results.Json(new { ok = false, error = "开机自启仅支持 Windows。" });
            if (body.Enabled is null)
                return Results.Json(new { ok = false, error = "缺少 enabled 字段。" });
            try
            {
                autostart.Set(body.Enabled.Value);
                return Results.Json(new { ok = true, enabled = body.Enabled.Value });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // 仅检查（不下载）：返回是否有新版 + 最新版本号 + 资产大小 + Release 页
        app.MapPost("/api/system/update/check", async () =>
        {
            using var checker = new UpdateChecker();
            var r = await checker.CheckAsync(AppVersion.Current);
            if (r.Error is not null)
                return Results.Json(new { ok = false, error = r.Error });
            var u = r.Update;
            return Results.Json(new
            {
                ok = true,
                hasUpdate = r.HasUpdate,
                currentVersion = AppVersion.CurrentString,
                latestVersion = u?.Version.ToString(3),
                sizeBytes = u?.SizeBytes ?? 0,
                releaseUrl = u?.ReleaseUrl ?? ""
            });
        });

        // 应用更新：检查 → 下载 → 启动 Updater → 响应后延迟退出主程序
        app.MapPost("/api/system/update/apply", async () =>
        {
            using var checker = new UpdateChecker();
            var r = await checker.CheckAsync(AppVersion.Current);
            if (r.Error is not null || !r.HasUpdate || r.Update is null)
                return Results.Json(new { ok = false, error = r.Error ?? "无可用更新" });

            var u = r.Update;
            var zipPath = Path.Combine(AppPaths.GetUpdateDir(), $"{u.Version}_{AppConstants.UpdateAssetName}");
            var dlErr = await checker.DownloadAsync(u.DownloadUrl, zipPath);
            if (dlErr is not null)
                return Results.Json(new { ok = false, error = $"下载失败：{dlErr}" });

            var exePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, AppConstants.MainExeName);
            var installDir = Path.GetDirectoryName(exePath)!;
            var updaterPath = Path.Combine(installDir, AppConstants.UpdaterExeName);
            if (!File.Exists(updaterPath))
                return Results.Json(new { ok = false, error = $"未找到升级程序 {AppConstants.UpdaterExeName}" });

            var args = $"--apply --pid {Environment.ProcessId} --dir \"{installDir}\" --zip \"{zipPath}\"";
            Process.Start(new ProcessStartInfo(updaterPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });

            // 响应发回客户端后延迟退出，让 Updater 接管（Application.Exit 线程安全，内部 post 到主线程）
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                try { Application.Exit(); } catch { }
            });

            return Results.Json(new { ok = true, message = "升级已启动，程序即将退出并重启" });
        });
    }

    public sealed class AutostartDto
    {
        public bool? Enabled { get; set; }
    }
}
