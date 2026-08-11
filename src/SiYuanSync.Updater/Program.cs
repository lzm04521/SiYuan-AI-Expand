// SiYuan-AI-Expand-Updater.exe
// 用法：--apply --pid <主进程PID> --dir <安装目录> --zip <升级包路径>
//
// 流程：
//   1. 等主进程（--pid）退出（最多 60s；主进程启动本程序后即 Application.Exit）
//   2. 解压 --zip 到临时 staging 目录
//   3. 复制 staging 覆盖 --dir（跳过正在运行的 Updater.exe 自身）
//   4. 启动 --dir\SiYuan-AI-Expand.exe
//   5. 清理临时目录，退出
//
// 设计为纯命令行工具，不带窗口；由主程序在"应用更新"时以 CreateNoWindow 启动。
using System.Diagnostics;
using System.IO.Compression;

const string MainExeName = "SiYuan-AI-Expand.exe";
const string UpdaterExeName = "SiYuan-AI-Expand-Updater.exe";

if (args.Length == 0 || !args.Contains("--apply"))
{
    Console.Error.WriteLine("本程序由 SiYuan-AI-Expand 在升级时自动调用，不应手动运行。");
    Console.Error.WriteLine("用法：SiYuan-AI-Expand-Updater.exe --apply --pid <pid> --dir <installdir> --zip <zippath>");
    return 1;
}

int? pid = ParseIntOption(args, "--pid");
string? dir = ParseStringOption(args, "--dir");
string? zip = ParseStringOption(args, "--zip");

if (pid is null || dir is null || zip is null)
{
    Console.Error.WriteLine("参数缺失：需要 --pid / --dir / --zip");
    return 2;
}
if (!Directory.Exists(dir))
{
    Console.Error.WriteLine($"安装目录不存在：{dir}");
    return 3;
}
if (!File.Exists(zip))
{
    Console.Error.WriteLine($"升级包不存在：{zip}");
    return 4;
}

Log("SiYuan-AI-Expand 升级程序启动");
Log($"  主进程 PID: {pid}");
Log($"  安装目录:   {dir}");
Log($"  升级包:     {zip}");

// 1. 等主进程退出
Log("等待主进程退出...");
if (!WaitForExit(pid.Value, TimeSpan.FromSeconds(60)))
{
    Console.Error.WriteLine($"主进程 {pid} 未在 60s 内退出，中止升级。");
    return 5;
}
Log("主进程已退出。");

// 2. 解压到 staging
var staging = Path.Combine(Path.GetTempPath(), "SiYuan-AI-Expand-update-" + Guid.NewGuid().ToString("N"));
Log($"解压升级包到临时目录：{staging}");
try
{
    ZipFile.ExtractToDirectory(zip, staging, overwriteFiles: true);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"解压失败：{ex.Message}");
    return 6;
}

// 3. 复制 staging → installDir，跳过正在运行的 Updater.exe（自身文件锁）
//    主 exe 已退出可覆盖；Updater.exe 跳过，下次主程序启动后可自行替换。
var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UpdaterExeName };
Log($"覆盖安装目录：{dir}");
try
{
    CopyDirectory(staging, dir, skipNames);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"覆盖安装目录失败：{ex.Message}");
    return 7;
}

// 4. 启动主程序
var mainExe = Path.Combine(dir, MainExeName);
if (!File.Exists(mainExe))
{
    Console.Error.WriteLine($"升级后未找到主程序：{mainExe}");
    return 8;
}
Log($"启动主程序：{mainExe}");
try
{
    Process.Start(new ProcessStartInfo(mainExe)
    {
        UseShellExecute = false,
        WorkingDirectory = dir
    });
}
catch (Exception ex)
{
    Console.Error.WriteLine($"启动主程序失败：{ex.Message}");
    return 9;
}

// 5. 清理临时目录（失败不阻断）
try { Directory.Delete(staging, recursive: true); } catch { }

Log("升级完成。");
return 0;


// --- 辅助函数 ---

static void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

static string? ParseStringOption(string[] args, string name)
{
    for (int i = 0; i + 1 < args.Length; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

static int? ParseIntOption(string[] args, string name)
{
    var s = ParseStringOption(args, name);
    return s is not null && int.TryParse(s, out var n) ? n : null;
}

/// <summary>等待进程退出；进程已不存在（ArgumentException）视为已退出。</summary>
static bool WaitForExit(int pid, TimeSpan timeout)
{
    try
    {
        using var p = Process.GetProcessById(pid);
        return p.WaitForExit((int)timeout.TotalMilliseconds);
    }
    catch (ArgumentException)
    {
        return true; // 进程已不存在
    }
    catch (InvalidOperationException)
    {
        return true; // 进程已退出
    }
}

static void CopyDirectory(string src, string dst, IReadOnlySet<string> skipNames)
{
    foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(src, file);
        var fileName = Path.GetFileName(file);
        if (skipNames.Contains(fileName))
        {
            Log($"  跳过（运行中）：{rel}");
            continue;
        }
        var target = Path.Combine(dst, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
        Log($"  覆盖：{rel}");
    }
}
