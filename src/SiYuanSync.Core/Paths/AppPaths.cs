using static System.Environment;

namespace SiYuanSync.Core.Paths;

public static class AppPaths
{
    public static string GetDataDir() =>
        Path.Combine(GetFolderPath(SpecialFolder.CommonApplicationData), "SiYuan-AI-Expand");

    public static string GetConfigPath() => Path.Combine(GetDataDir(), "config.json");
    public static string GetStateDbPath() => Path.Combine(GetDataDir(), "state.db");
    public static string GetLogsDir() => Path.Combine(GetDataDir(), "logs");

    public static void EnsureDataDir()
    {
        Directory.CreateDirectory(GetDataDir());
        Directory.CreateDirectory(GetLogsDir());
    }
}
