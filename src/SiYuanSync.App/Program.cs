using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SiYuanSync.App.Hosting;
using SiYuanSync.App.Tray;
using SiYuanSync.App.Web;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Paths;
using HostBuilder = SiYuanSync.App.Hosting.HostBuilder;

namespace SiYuanSync.App;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        bool console = args.Contains("--console");

        // 单实例限制（先于端口验证）：命名 Mutex 唯一标识本应用，第二个实例提示后退出。
        // 进程退出（含崩溃）时 OS 自动释放 Mutex；更新流程 Updater 等旧进程退出后才拉起新 exe，不受影响。
        using var singleInstance = new Mutex(initiallyOwned: true,
            $"Local\\{AppConstants.RepoName}.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            if (console)
                Console.Error.WriteLine("SiYuan-AI-Expand 已在运行，不允许多开，本次启动退出。");
            else
                try { if (OperatingSystem.IsWindows()) MessageBox.Show("SiYuan-AI-Expand 已在运行，不允许多开，本次启动退出。", "SiYuan-AI-Expand", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            return 3;
        }

        try
        {
            using var host = HostBuilder.Build(args);
            var snap = host.Services.GetRequiredService<ConfigStore>().GetSnapshot();

            // 端口预检：Kestrel 真正监听前用同地址端口试绑定并立即释放，被占用则提示后退出
            if (TryProbeWebPort(snap.Web, out string portError))
            {
                try { if (OperatingSystem.IsWindows()) EventLog.WriteEntry("SiYuan-AI-Expand", $"启动失败：{portError}", EventLogEntryType.Error, 3); } catch { }
                Console.Error.WriteLine($"启动失败：{portError}");
                if (!console)
                    try { if (OperatingSystem.IsWindows()) MessageBox.Show($"启动失败：\n\n{portError}", "SiYuan-AI-Expand", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                return 4;
            }

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

    /// <summary>
    /// 以与 Kestrel 相同的地址/端口试绑定并立即释放。
    /// 仅拦截明确的端口占用（AddressAlreadyInUse），其余异常（权限等）留给 Kestrel 启动时的原生报错。
    /// </summary>
    private static bool TryProbeWebPort(WebConfig web, out string error)
    {
        error = "";
        var addr = WebHostBuilder.MapBind(web.Bind);
        try
        {
            using var probe = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            probe.Bind(new IPEndPoint(addr, web.Port));
            probe.Listen(1);
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            error = $"Web 端口 {web.Port}（绑定地址 {web.Bind}）已被其他程序占用。\n\n" +
                    $"请关闭占用该端口的程序，或修改配置文件中的 Web.Port 后重试。\n" +
                    $"配置文件：{AppPaths.GetConfigPath()}";
            return true;
        }
    }
}

// --console 模式创建独立控制台窗口（WinExe 默认无 stdout）
internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();
}
