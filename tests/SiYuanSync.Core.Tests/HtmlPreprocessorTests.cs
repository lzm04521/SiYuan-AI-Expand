using SiYuanSync.Core.Sync;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class HtmlPreprocessorTests
{
    [Fact]
    public void Full_document_extracts_body_and_skips_head()
    {
        var html = "<html><head><title>页面标题X</title><meta charset=\"utf-8\"></head>"
                 + "<body><h1>报告标题</h1><p>结论甲乙</p></body></html>";
        var md = HtmlPreprocessor.ToMarkdown(html);
        Assert.StartsWith("#", md.TrimStart());
        Assert.Contains("报告标题", md);
        Assert.Contains("结论甲乙", md);
        Assert.DoesNotContain("页面标题X", md);
    }

    [Fact]
    public void Script_style_and_comment_removed()
    {
        var html = "<body><script>alert(1)</script><style>p{color:red}</style>"
                 + "<!--内部注释Y--><p>正文</p></body>";
        var md = HtmlPreprocessor.ToMarkdown(html);
        Assert.Contains("正文", md);
        Assert.DoesNotContain("alert", md);
        Assert.DoesNotContain("color:red", md);
        Assert.DoesNotContain("内部注释Y", md);
    }

    [Fact]
    public void Wrapper_tags_unwrapped_content_kept()
    {
        var html = "<body><div><section><span>块文本</span></section></div></body>";
        var md = HtmlPreprocessor.ToMarkdown(html);
        Assert.Contains("块文本", md);
    }

    [Fact]
    public void Table_converts_to_markdown_table()
    {
        var html = "<body><table><tr><th>列A</th><th>列B</th></tr>"
                 + "<tr><td>1</td><td>2</td></tr></table></body>";
        var md = HtmlPreprocessor.ToMarkdown(html);
        Assert.Contains("|", md);
        Assert.Contains("列A", md);
        Assert.Contains("列B", md);
    }

    [Fact]
    public void Code_block_kept()
    {
        var html = "<body><pre><code>var x = 1;</code></pre></body>";
        var md = HtmlPreprocessor.ToMarkdown(html);
        Assert.Contains("var x = 1;", md);
    }

    [Fact]
    public void Fragment_without_document_structure_still_converts()
    {
        var md = HtmlPreprocessor.ToMarkdown("<p>片段内容</p>");
        Assert.Contains("片段内容", md);
    }
}
