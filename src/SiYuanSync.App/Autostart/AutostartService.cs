using Microsoft.Win32;

namespace SiYuanSync.App.Autostart;

/// <summary>
/// 开机自启：读写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 仅 Windows。值 = 主 exe 路径（带引号）；托盘进程在用户登录时由系统拉起。
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _exePath;
    private readonly string _valueName;

    public AutostartService(string exePath, string valueName = AppConstants.AutostartValueName)
    {
        _exePath = exePath;
        _valueName = valueName;
    }

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(_valueName) is string s && !string.IsNullOrWhiteSpace(s);
    }

    public void Enable()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("开机自启仅支持 Windows。");
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        // 路径含空格时必须加引号，否则会被空格拆成 exe + 参数
        key.SetValue(_valueName, $"\"{_exePath}\"", RegistryValueKind.String);
    }

    public void Disable()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("开机自启仅支持 Windows。");
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    public void Set(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }
}
