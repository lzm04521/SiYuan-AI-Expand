using System.Text.Json.Serialization;

namespace SiYuanSync.Core.Models;

public sealed class SiyuanConfig
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:6806";
    public string Token { get; set; } = "";
    public string DefaultNotebook { get; set; } = "";

    [JsonIgnore]
    public bool HasToken => !string.IsNullOrEmpty(Token);
}
