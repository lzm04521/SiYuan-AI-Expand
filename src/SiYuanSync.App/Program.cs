using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using SiYuanSync.App.Hosting;
using SiYuanSync.Core.Config;
using HostBuilder = SiYuanSync.App.Hosting.HostBuilder;

try
{
    using var host = HostBuilder.Build(args);
    host.Run();
}
catch (ConfigCorruptException ex)
{
    // 保留原文件，记事件日志，非零退出
    try { if (OperatingSystem.IsWindows()) EventLog.WriteEntry("SiYuan-AI-Expand", $"启动失败：配置损坏 — {ex.Message}", EventLogEntryType.Error, 1); } catch { }
    Console.Error.WriteLine($"启动失败：配置损坏 — {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    try { if (OperatingSystem.IsWindows()) EventLog.WriteEntry("SiYuan-AI-Expand", $"启动失败：{ex}", EventLogEntryType.Error, 2); } catch { }
    Console.Error.WriteLine($"启动失败：{ex}");
    return 2;
}
return 0;
