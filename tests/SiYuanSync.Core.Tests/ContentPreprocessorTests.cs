using SiYuanSync.Core.Sync;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class ContentPreprocessorTests
{
    [Fact]
    public void Leading_h1_stripped_into_title_and_body()
    {
        var raw = "# 登录方案\n\n正文内容\n";
        var p = ContentPreprocessor.Process(raw);
        Assert.Equal("登录方案", p.Title);
        Assert.DoesNotContain("# 登录方案", p.BodyMd);
        Assert.Contains("正文内容", p.BodyMd);
    }

    [Fact]
    public void No_leading_h1_keeps_title_empty()
    {
        var raw = "正文无标题";
        var p = ContentPreprocessor.Process(raw);
        Assert.Equal("", p.Title);
        Assert.Equal("正文无标题", p.BodyMd);
    }

    [Fact]
    public void H2_not_stripped()
    {
        var raw = "## 二级\n内容";
        var p = ContentPreprocessor.Process(raw);
        Assert.Equal("", p.Title);
        Assert.Contains("## 二级", p.BodyMd);
    }

    [Fact]
    public void H1_not_first_line_not_stripped()
    {
        var raw = "\n# 标题\n内容";
        var p = ContentPreprocessor.Process(raw);
        Assert.Equal("", p.Title); // 首行是空行，不算
        Assert.Contains("# 标题", p.BodyMd);
    }

    [Fact]
    public void Hash_is_deterministic_and_sensitive()
    {
        var h1 = ContentPreprocessor.ComputeHash("body");
        var h2 = ContentPreprocessor.ComputeHash("body");
        var h3 = ContentPreprocessor.ComputeHash("changed");
        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);
        Assert.Equal(64, h1.Length); // SHA256 hex
    }
}
