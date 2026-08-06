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
}
