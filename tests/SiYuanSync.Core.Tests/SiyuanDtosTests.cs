using System.Text.Json;
using SiYuanSync.Core.Models;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class SiyuanDtosTests
{
    [Fact]
    public void Notebook_parses_id_and_name()
    {
        var json = """{"id":"20210808180117-6v0mkxr","name":"AI","closed":false}""";
        using var doc = JsonDocument.Parse(json);
        var nb = Notebook.FromJson(doc.RootElement);
        Assert.Equal("20210808180117-6v0mkxr", nb.Id);
        Assert.Equal("AI", nb.Name);
    }

    [Fact]
    public void BlockChild_parses_id_and_subtype()
    {
        var json = """{"id":"blk-1","type":"NodeParagraph","subType":"p"}""";
        using var doc = JsonDocument.Parse(json);
        var b = BlockChild.FromJson(doc.RootElement);
        Assert.Equal("blk-1", b.Id);
    }
}
