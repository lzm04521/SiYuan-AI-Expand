using System.Text.Json;

namespace SiYuanSync.Core.Models;

public sealed record Notebook(string Id, string Name)
{
    public static Notebook FromJson(JsonElement e) =>
        new(e.GetProperty("id").GetString() ?? "", e.GetProperty("name").GetString() ?? "");
}

public sealed record BlockChild(string Id, string Type)
{
    public static BlockChild FromJson(JsonElement e) =>
        new(e.GetProperty("id").GetString() ?? "", e.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "");
}

public sealed record SiyuanConnectionConfig(string ServerUrl, string Token);
