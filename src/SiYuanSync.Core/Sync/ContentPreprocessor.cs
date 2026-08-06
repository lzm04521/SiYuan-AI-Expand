using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SiYuanSync.Core.Sync;

public sealed record ProcessedContent(string Title, string BodyMd);

public static class ContentPreprocessor
{
    private static readonly Regex LeadingH1 = new(@"^#\s+(.+?)\s*$", RegexOptions.Compiled);

    public static ProcessedContent Process(string raw)
    {
        // 找第一行（跳过绝对开头的换行？设计：仅当它是首行且为一级标题）
        var nlIdx = raw.IndexOf('\n');
        var firstLine = nlIdx < 0 ? raw : raw[..nlIdx];
        var m = LeadingH1.Match(firstLine);
        if (m.Success)
        {
            var title = m.Groups[1].Value.Trim();
            var body = nlIdx < 0 ? "" : raw[(nlIdx + 1)..];
            // 去掉 body 开头的一个换行（首行后的空行）使正文不重复
            return new ProcessedContent(title, body);
        }
        return new ProcessedContent("", raw);
    }

    public static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
