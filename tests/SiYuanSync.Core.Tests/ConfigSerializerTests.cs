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
            Siyuan = { ServerUrl = "http://host:6806", Token = "tok", DefaultNotebook = "AI",
                       AutoStartOnSync = true, ExePath = @"C:\Apps\SiYuan\SiYuan.exe" },
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
        Assert.True(back.Siyuan.AutoStartOnSync);
        Assert.Equal(@"C:\Apps\SiYuan\SiYuan.exe", back.Siyuan.ExePath);
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

    [Fact]
    public void Legacy_json_without_autostart_fields_gets_defaults()
    {
        // 升级前的 config.json 无 autoStartOnSync/exePath 字段：反序列化取默认值（关闭 + 自动搜索）
        const string legacy = """
            {"Siyuan":{"ServerUrl":"http://127.0.0.1:6806","Token":"t","DefaultNotebook":""},
             "Sync":{"IntervalMinutes":10,"RunOnStart":true},
             "Web":{"Port":61122,"Bind":"127.0.0.1","Password":""},
             "Mcp":{"Enabled":false},"Projects":[]}
            """;
        var back = ConfigSerializer.Deserialize(legacy);

        Assert.False(back.Siyuan.AutoStartOnSync);
        Assert.Equal("", back.Siyuan.ExePath);
        Assert.True(back.Sync.RunOnStart); // 既有字段仍正常读取
    }

    [Fact]
    public void Old_config_without_new_fields_defaults_to_disabled()
    {
        // 模拟旧版 config.json（ConfigSerializer 写出为 PascalCase）：项目节点无 SettleMinutes/IncludePattern/ExcludePattern/DeleteSync
        var json = @"{""Siyuan"":{""ServerUrl"":""http://127.0.0.1:6806"",""Token"":""t""},
""Sync"":{""IntervalMinutes"":10,""RunOnStart"":true},
""Web"":{""Port"":61122,""Bind"":""127.0.0.1""},
""Projects"":[{""Name"":""P"",""Enabled"":true,""DocPath"":""C:\\d1"",""Notebook"":""AI"",""ParentPath"":""/A""}]}";
        var back = ConfigSerializer.Deserialize(json);
        var p = Assert.Single(back.Projects);
        Assert.Null(p.SettleMinutes);
        Assert.Equal("", p.IncludePattern);
        Assert.Equal("", p.ExcludePattern);
        Assert.False(p.DeleteSync);
    }

    [Fact]
    public void Roundtrip_preserves_new_project_fields()
    {
        var cfg = new AppConfig();
        cfg.Projects.Add(new ProjectConfig { Name = "P", DocPath = @"C:\d1", Notebook = "NB", ParentPath = "/A",
            SettleMinutes = 5, IncludePattern = @"^a", ExcludePattern = @"\.tmp$", DeleteSync = true });
        var back = ConfigSerializer.Deserialize(ConfigSerializer.Serialize(cfg));
        Assert.Equal(5, back.Projects[0].SettleMinutes);
        Assert.Equal(@"^a", back.Projects[0].IncludePattern);
        Assert.Equal(@"\.tmp$", back.Projects[0].ExcludePattern);
        Assert.True(back.Projects[0].DeleteSync);
    }
}
