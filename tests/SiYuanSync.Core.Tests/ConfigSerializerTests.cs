using System.Text.Json;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class ConfigSerializerTests
{
    [Fact]
    public void Roundtrip_preserves_all_fields()
    {
        var cfg = new AppConfig
        {
            Siyuan = { ServerUrl = "http://host:6806", Token = "tok", DefaultNotebook = "AI" },
            Sync = { IntervalMinutes = 7, RunOnStart = false },
            Web = { Port = 7000, Bind = "0.0.0.0", Password = "pw" },
            Projects =
            {
                new ProjectConfig { Name = "JPT", Enabled = true, DocPath = @"D:\work\JPT\doc",
                                    Notebook = "AI", ParentPath = "/JPT" }
            }
        };
        var json = ConfigSerializer.Serialize(cfg);
        var back = ConfigSerializer.Deserialize(json);

        Assert.Equal("http://host:6806", back.Siyuan.ServerUrl);
        Assert.Equal("tok", back.Siyuan.Token);
        Assert.Equal(7, back.Sync.IntervalMinutes);
        Assert.False(back.Sync.RunOnStart);
        Assert.Equal("0.0.0.0", back.Web.Bind);
        Assert.Equal("pw", back.Web.Password);
        Assert.Single(back.Projects);
        Assert.Equal("/JPT", back.Projects[0].ParentPath);
    }

    [Fact]
    public void Deserialize_invalid_json_throws()
    {
        Assert.ThrowsAny<JsonException>(() => ConfigSerializer.Deserialize("{ not json"));
    }
}
