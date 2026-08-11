using System.Net.Http.Json;

namespace SiYuanSync.App.Update;

/// <summary>
/// 查询 GitHub Releases 最新版并与本地版本比对，下载升级资产到本地。
/// 供设置窗口的"检查更新"与托盘菜单复用。
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private const string UserAgent = "SiYuan-AI-Expand-updater";
    private const string ApiBase = "https://api.github.com";

    private readonly string _owner;
    private readonly string _repo;
    private readonly string _assetName;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public UpdateChecker(string owner, string repo, string assetName, HttpClient? http = null)
    {
        _owner = owner;
        _repo = repo;
        _assetName = assetName;
        _http = http ?? new HttpClient();
        _ownsHttp = http is null;
    }

    public UpdateChecker(HttpClient? http = null)
        : this(AppConstants.RepoOwner, AppConstants.RepoName, AppConstants.UpdateAssetName, http) { }

    /// <summary>查询 latest release，比对版本。失败返回 Error（不抛）。</summary>
    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{ApiBase}/repos/{_owner}/{_repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd(UserAgent);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Error = resp.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? "GitHub API 限流（未认证每小时 60 次），请稍后再试。"
                        : $"GitHub API 返回 {(int)resp.StatusCode} {resp.ReasonPhrase}"
                };
            }
            var rel = await resp.Content.ReadFromJsonAsync<GitHubReleaseDto>(cancellationToken: ct);
            if (rel is null) return new UpdateCheckResult { Error = "无法解析 Release 响应" };

            var remote = ParseTag(rel.TagName);
            if (remote is null) return new UpdateCheckResult { Error = $"无法解析版本标签：{rel.TagName}" };

            var asset = rel.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, _assetName, StringComparison.OrdinalIgnoreCase));
            if (asset is null)
            {
                var avail = rel.Assets.Count == 0
                    ? "（Release 无任何资产）"
                    : string.Join(", ", rel.Assets.Select(a => a.Name));
                return new UpdateCheckResult { Error = $"Release 未找到资产 {_assetName}，可用：{avail}" };
            }

            return new UpdateCheckResult
            {
                HasUpdate = CompareVersion(remote, currentVersion) > 0,
                Update = new UpdateInfo
                {
                    Version = remote,
                    ReleaseUrl = rel.HtmlUrl,
                    ReleaseNotes = rel.Body ?? "",
                    AssetName = asset.Name,
                    DownloadUrl = asset.BrowserDownloadUrl,
                    SizeBytes = asset.Size
                }
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { Error = ex.Message };
        }
    }

    /// <summary>下载资产到目标路径（覆盖）。返回错误信息或 null。</summary>
    public async Task<string?> DownloadAsync(string url, string destPath, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await src.CopyToAsync(dst, ct);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>解析 v1.2.3 / V1.2.3 / 1.2.3 → Version；无法解析返回 null。</summary>
    public static Version? ParseTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>只比较 Major.Minor.Build；缺失 Build 视为 0（避免 Revision=-1 影响比较）。</summary>
    public static int CompareVersion(Version a, Version b)
    {
        int c = a.Major.CompareTo(b.Major); if (c != 0) return c;
        c = a.Minor.CompareTo(b.Minor); if (c != 0) return c;
        return Norm(a.Build).CompareTo(Norm(b.Build));

        static int Norm(int v) => v < 0 ? 0 : v;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
