using System.Text.Json.Serialization;

namespace SiYuanSync.App.Update;

// GitHub REST API: GET /repos/{owner}/{repo}/releases/latest 响应（仅反序列化需要的字段）。
internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAssetDto> Assets { get; set; } = new();
}

internal sealed class GitHubAssetDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}
