using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SiYuanSync.App.Hosting;
using SiYuanSync.App.Tray;
using SiYuanSync.Core.Config;
using HostBuilder = SiYuanSync.App.Hosting.HostBuilder;

namespace SiYuanSync.App;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        bool console = args.Contains("--console");
        try
        {
            using var host = HostBuilder.Build(args);

            if (console)
            {
                // 控制台调试模式：分配控制台后阻塞运行，关闭窗口 / Ctrl+C 退出
                NativeMethods.AllocConsole();
                try { Console.Title = "SiYuan-AI-Expand（控制台模式）"; } catch { }
                host.Run();
            }
            else
            {
                // 托盘模式：先算出 Web 管理页地址（供托盘"打开管理页"使用）
                var snap = host.Services.GetRequiredService<ConfigStore>().GetSnapshot();
                var browserAddr = snap.Web.Bind switch
                {
                    "0.0.0.0" or "" => "127.0.0.1",
                    var b => b
                };
                var webUrl = $"http://{browserAddr}:{snap.Web.Port}/";

                ApplicationConfiguration.Initialize();
                await host.StartAsync();
                using var tray = new TrayApp(host, webUrl);
                try { Application.Run(); }
                finally { await host.StopAsync(); }
            }
        }
        catch (ConfigCorruptException ex)
        {
            // 保留原文件，记事件日志；托盘模式额外弹窗，console 模式仅控制台输出
            try { if (OperatingSystem.IsWindows()) EventLog.WriteEntry("SiYuan-AI-Expand", $"启动失败：配置损坏 — {ex.Message}", EventLogEntryType.Error, 1); } catch { }
            Console.Error.WriteLine($"启动失败：配置损坏 — {ex.Message}");
            if (!console)
                try { if (OperatingSystem.IsWindows()) MessageBox.Show($"启动失败：配置损坏\n\n{ex.Message}", "SiYuan-AI-Expand", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            return 1;
        }
        catch (Exception ex)
        {
            try { if (OperatingSystem.IsWindows()) EventLog.WriteEntry("SiYuan-AI-Expand", $"启动失败：{ex}", EventLogEntryType.Error, 2); } catch { }
            Console.Error.WriteLine($"启动失败：{ex}");
            if (!console)
                try { if (OperatingSystem.IsWindows()) MessageBox.Show($"启动失败：\n\n{ex}", "SiYuan-AI-Expand", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            return 2;
        }
        return 0;
    }
}

// --console 模式创建独立控制台窗口（WinExe 默认无 stdout）
internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();
}
