using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;

namespace SiYuanSync.App.Siyuan;

/// <summary>
/// 同步前"确保思源在运行"：探测 API 可用性 → 未运行则解析思源 exe 并以 --openAsHidden 隐藏启动
/// → 按 SiyuanAutoStart 的节奏（拉起后先固定等 60s 给内核启动留时间，再最多 5 次、间隔 30s 轮询至就绪）。
/// 路径解析顺序：显式 ExePath → NSIS 常见安装路径 → siyuan:// 协议注册表 → Microsoft Store 包
/// （Store 包安装目录带版本号且 .NET 无法直接枚举 WindowsApps，只能经 Get-AppxPackage 动态解析）。
/// </summary>
public sealed class SiyuanAutoStartService
{
    private const string HiddenArg = "--openAsHidden";
    // 思源 Microsoft Store 包名（开发者 ID 前缀，官方商店发行）
    private const string StorePackageName = "89C2A984.SiYuan";

    private readonly Func<SiyuanConnectionConfig, ISiyuanClient> _clientFactory;
    private readonly ILogger<SiyuanAutoStartService> _logger;
    private readonly SiyuanAutoStart _autoStart = new();

    public SiyuanAutoStartService(Func<SiyuanConnectionConfig, ISiyuanClient> clientFactory,
        ILogger<SiyuanAutoStartService> logger)
    { _clientFactory = clientFactory; _logger = logger; }

    /// <summary>就绪返回 true；启动失败或轮询超时返回 false（异常均记日志，不再向上抛）。</summary>
    public async Task<bool> EnsureRunningAsync(SiyuanConfig siyuan, CancellationToken ct)
    {
        var conn = new SiyuanConnectionConfig(siyuan.ServerUrl, siyuan.Token);
        try
        {
            var ok = await _autoStart.EnsureRunningAsync(
                probe: c => ProbeAsync(conn, c),
                launch: () =>
                {
                    var exe = FindSiyuanExe(siyuan.ExePath);
                    _logger.LogInformation("思源未运行，自动隐藏启动：{Exe}", exe);
                    Process.Start(new ProcessStartInfo { FileName = exe, Arguments = HiddenArg, UseShellExecute = false });
                },
                ct);
            if (!ok) _logger.LogWarning("思源启动后 {Polls} 次轮询均未就绪，放弃等待", SiyuanAutoStart.DefaultMaxPolls);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动启动思源失败");
            return false;
        }
    }

    /// <summary>思源 API 真正可用 = true。网络不可达 = false（未运行）；鉴权失败说明端口已通，视为在运行；
    /// 业务错误 = false —— 端口已通但内核尚未完成启动（或 lsNotebooks 本身失败）时同步必然失败，不算就绪。</summary>
    private async Task<bool> ProbeAsync(SiyuanConnectionConfig conn, CancellationToken ct)
    {
        var client = _clientFactory(conn);
        try { await client.ListNotebooksAsync(ct); return true; }
        catch (SiyuanTransientException) { return false; }
        catch (SiyuanAuthException) { return true; }
    }

    /// <summary>按优先级解析思源 exe 完整路径；全部失败抛 InvalidOperationException 说明已尝试的位置。</summary>
    internal string FindSiyuanExe(string? explicitPath)
    {
        // 1. 显式配置路径：以存在性校验失败明确报错（不静默回退，避免配置被无视却无感知）
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(explicitPath)) return explicitPath;
            throw new InvalidOperationException($"配置的思源 exe 路径不存在：{explicitPath}");
        }

        // 2. NSIS 安装版常见路径
        string[] nsiPaths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SiYuan", "SiYuan.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SiYuan", "SiYuan.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SiYuan", "SiYuan.exe"),
        };
        var found = nsiPaths.FirstOrDefault(File.Exists);
        if (found is not null) return found;

        // 3. siyuan:// 协议注册表（NSIS 版注册；Store 版此处为空）
        var protocolCmd = FindProtocolCommand();
        if (protocolCmd is not null) return protocolCmd;

        // 4. Microsoft Store 版：Get-AppxPackage 动态解析（安装目录带版本号，不能缓存）
        var storeExe = FindStoreExe();
        if (storeExe is not null) return storeExe;

        throw new InvalidOperationException(
            "未找到思源 exe。已尝试：NSIS 常见安装路径、siyuan:// 协议注册表、Microsoft Store 包（Get-AppxPackage）。" +
            "请在 Web 配置页 siyuan.exePath 显式指定路径。");
    }

    /// <summary>从 HKCR siyuan:// 协议的 shell\open\command 提取 exe 路径（形如 "C:\...\SiYuan.exe" "%1"）。</summary>
    private static string? FindProtocolCommand()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(@"siyuan\shell\open\command");
        if (key?.GetValue(null) is not string cmd) return null;
        var exe = cmd.Replace("\"%1\"", "").Trim().Trim('"');
        return exe.Length > 0 && File.Exists(exe) ? exe : null;
    }

    /// <summary>Get-AppxPackage 解析 Store 版 InstallLocation（输出 ASCII 路径，无编码问题）；未安装返回 null。</summary>
    private string? FindStoreExe()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"(Get-AppxPackage -Name '{StorePackageName}').InstallLocation\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var ps = Process.Start(psi);
        if (ps is null) return null;
        // 先异步读再限时等退出：ReadToEnd 直接阻塞无超时，PowerShell 挂死会永久占用同步轮
        var outputTask = ps.StandardOutput.ReadToEndAsync();
        if (!ps.WaitForExit(15_000))
        {
            try { ps.Kill(); } catch { /* 已退出 */ }
            return null;
        }
        var location = outputTask.Result.Trim();
        if (string.IsNullOrEmpty(location)) return null;
        var exe = Path.Combine(location, "app", "SiYuan.exe");
        return File.Exists(exe) ? exe : null;
    }
}
