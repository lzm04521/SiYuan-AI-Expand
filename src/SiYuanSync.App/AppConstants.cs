namespace SiYuanSync.App;

/// <summary>固定常量：仓库坐标、升级资产命名、注册表项名。</summary>
public static class AppConstants
{
    public const string RepoOwner = "lzm04521";
    public const string RepoName = "SiYuan-AI-Expand";
    public const string RepoUrl = "https://github.com/lzm04521/SiYuan-AI-Expand";

    // GitHub Release 资产命名约定（publish.ps1 打包同名 zip，发布时上传为 Release 资产）。
    public const string UpdateAssetName = "SiYuan-AI-Expand-win-x64.zip";

    public const string MainExeName = "SiYuan-AI-Expand.exe";
    public const string UpdaterExeName = "SiYuan-AI-Expand-Updater.exe";

    // HKCU\...\Run 下的开机自启值名。
    public const string AutostartValueName = "SiYuan-AI-Expand";
}
