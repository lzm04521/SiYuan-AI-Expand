using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Config;

public static class ConfigSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 保留中文/中文标点可读
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize(AppConfig cfg) => JsonSerializer.Serialize(cfg, Options);

    public static AppConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfig>(json, Options)
            ?? throw new JsonException("配置根对象为空");
}
