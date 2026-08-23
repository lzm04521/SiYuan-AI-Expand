using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ReverseMarkdown;

namespace SiYuanSync.Core.Sync;

/// <summary>HTML 报告 → Markdown：AngleSharp 抽取 body 正文，ReverseMarkdown v6 转换。
/// script/style/注释与常见包裹标签（span/div 等）由转换管道剥除；
/// 输出交 ContentPreprocessor 复用首行 H1 剥离与标题回退，与 md 管线行为一致。</summary>
public static class HtmlPreprocessor
{
    // Converter 无共享可变状态，且同步全局串行（RunCoordinator 信号量），单例安全
    private static readonly Converter Converter = CreateConverter();

    public static string ToMarkdown(string rawHtml)
    {
        var doc = new HtmlParser().ParseDocument(rawHtml);
        // HTML5 解析总会构建 html/body 结构，Body 兜底防御；
        // INode 无 InnerHtml（仅 IElement 有），非元素兜底取 TextContent
        INode root = doc.Body ?? doc.DocumentElement ?? (INode)doc;
        return Converter.Convert(root is IElement element ? element.InnerHtml : root.TextContent);
    }

    private static Converter CreateConverter()
    {
        // 完全限定 ReverseMarkdown.Config：项目内存在同名命名空间 SiYuanSync.Core.Config，简单名会被解析为命名空间。
        // 用 GithubFlavored=true（默认 writer 上的 GFM 风格）而非 Flavor=MarkdownFlavor.GitHub：
        // 后者是 round-trip 保守 writer，对 h1/table 等构造直接保留原始 HTML，产不出首行 "# 标题" 的干净 Markdown
        var config = new ReverseMarkdown.Config
        {
            GithubFlavored = true,
            Formatting = { RemoveComments = true },
        };
        config.Preprocess
            .RemoveScripts()
            .RemoveStyles()
            .Unwrap("span, font, div, section, article, header, footer, nav");
        return new Converter(config);
    }
}
