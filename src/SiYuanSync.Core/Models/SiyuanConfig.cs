using System.Text.Json.Serialization;

namespace SiYuanSync.Core.Models;

public sealed class SiyuanConfig
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:6806";
    public string Token { get; set; } = "";
    public string DefaultNotebook { get; set; } = "";
    /// <summary>同步前检测思源未运行时，自动以隐藏窗口方式拉起思源。</summary>
    public bool AutoStartOnSync { get; set; }
    /// <summary>思源 exe 显式路径；空 = 自动搜索（NSIS 常见路径 → siyuan:// 协议 → Microsoft Store 包）。</summary>
    public string ExePath { get; set; } = "";

    [JsonIgnore]
    public bool HasToken => !string.IsNullOrEmpty(Token);
}
