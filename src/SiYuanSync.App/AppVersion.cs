namespace SiYuanSync.App;

/// <summary>当前程序集版本（来自 Directory.Build.props 的 Version 属性）。</summary>
public static class AppVersion
{
    public static Version Current => typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>主.次.生成 三段显示串。</summary>
    public static string CurrentString => Current.ToString(3);
}
