using static System.Environment;

namespace SiYuanSync.Core.Paths;

public static class AppPaths
{
    // 托盘程序以普通用户运行，数据目录放用户级 LocalAppData：
    // 无需管理员权限、无需 ACL 脚本，符合"用户态常驻应用"定位。
    public static string GetDataDir() =>
        Path.Combine(GetFolderPath(SpecialFolder.LocalApplicationData), "SiYuan-AI-Expand");

    public static string GetConfigPath() => Path.Combine(GetDataDir(), "config.json");
    public static string GetStateDbPath() => Path.Combine(GetDataDir(), "state.db");
    public static string GetLogsDir() => Path.Combine(GetDataDir(), "logs");
    public static string GetUpdateDir() => Path.Combine(GetDataDir(), "update");

    public static void EnsureDataDir()
    {
        Directory.CreateDirectory(GetDataDir());
        Directory.CreateDirectory(GetLogsDir());
    }
}
