using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class ProjectUniquenessTests
{
    private static AppConfig WithProjects(params ProjectConfig[] projects) => new() { Projects = projects.ToList() };

    private static ProjectConfig P(string name, string doc, string nb, string parent) =>
        new() { Name = name, DocPath = doc, Notebook = nb, ParentPath = parent };

    [Fact]
    public void Distinct_projects_ok()
    {
        var cfg = WithProjects(
            P("A", @"D:\a\doc", "AI", "/A"),
            P("B", @"D:\b\doc", "AI", "/B"));
        Assert.Empty(ConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Same_notebook_parentPath_pair_conflicts()
    {
        var cfg = WithProjects(
            P("A", @"D:\a\doc", "AI", "/JPT"),
            P("B", @"D:\b\doc", "AI", "/JPT"));
        var errs = ConfigValidator.Validate(cfg);
        Assert.Contains(errs, e => e.Contains("A") && e.Contains("B") && e.Contains("/JPT"));
    }

    [Fact]
    public void Same_docPath_conflicts()
    {
        var cfg = WithProjects(
            P("A", @"D:\work\doc", "AI", "/A"),
            P("B", @"D:\work\doc", "AI", "/B"));
        Assert.NotEmpty(ConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Nested_docPath_conflicts()
    {
        var cfg = WithProjects(
            P("A", @"D:\work", "AI", "/A"),
            P("B", @"D:\work\doc", "AI", "/B"));
        var errs = ConfigValidator.Validate(cfg);
        Assert.NotEmpty(errs);
    }

    [Fact]
    public void Nested_docPath_conflicts_reversed()
    {
        // 深路径列在前：原单向 IsSameOrAncestor(deeper, shallower) 返回 false 会漏判
        var cfg = WithProjects(
            P("A", @"D:\work\doc", "AI", "/A"),
            P("B", @"D:\work", "AI", "/B"));
        Assert.NotEmpty(ConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Empty_notebook_falls_back_to_default_then_checked()
    {
        var cfg = WithProjects(
            P("A", @"D:\a\doc", "", "/X"),
            P("B", @"D:\b\doc", "", "/X"));
        cfg.Siyuan.DefaultNotebook = "AI";
        // 两者都回退到 (AI, /X) → 冲突
        Assert.NotEmpty(ConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Duplicate_project_name_rejected()
    {
        var cfg = WithProjects(
            P("JPT", @"D:\a\doc", "AI", "/A"),
            P("JPT", @"D:\b\doc", "AI", "/B"));
        Assert.Contains(ConfigValidator.Validate(cfg), e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParentPath_without_leading_slash_rejected_with_hint()
    {
        var cfg = WithProjects(P("A", @"D:\a\doc", "AI", "杰普特"));
        Assert.Contains(ConfigValidator.Validate(cfg),
            e => e.Contains("parentPath 不规范") && e.Contains("'/杰普特'"));
    }

    [Fact]
    public void ParentPath_trailing_slash_rejected()
    {
        var cfg = WithProjects(P("A", @"D:\a\doc", "AI", "/A/"));
        Assert.Contains(ConfigValidator.Validate(cfg), e => e.Contains("parentPath 不规范"));
    }

    [Fact]
    public void Empty_parentPath_allowed_for_mcp_intermediate_state()
    {
        var cfg = WithProjects(P("A", @"D:\a\doc", "AI", ""));
        Assert.Empty(ConfigValidator.Validate(cfg));
    }
}
