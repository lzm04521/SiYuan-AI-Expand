using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class ConfigValidatorTests
{
    private static AppConfig Valid() => new();

    [Fact]
    public void Default_loopback_config_is_valid()
    {
        Assert.Empty(ConfigValidator.Validate(Valid()));
    }

    [Fact]
    public void Invalid_bind_rejected()
    {
        var cfg = Valid();
        cfg.Web.Bind = "8.8.8.8";
        var errs = ConfigValidator.Validate(cfg);
        Assert.Contains(errs, e => e.Contains("bind", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Port_out_of_range_rejected(int port)
    {
        var cfg = Valid();
        cfg.Web.Port = port;
        Assert.NotEmpty(ConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Extranet_bind_without_password_rejected()
    {
        var cfg = Valid();
        cfg.Web.Bind = "0.0.0.0";
        cfg.Web.Password = "   ";
        var errs = ConfigValidator.Validate(cfg);
        Assert.Contains(errs, e => e.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extranet_bind_with_password_ok()
    {
        var cfg = Valid();
        cfg.Web.Bind = "0.0.0.0";
        cfg.Web.Password = "secret";
        Assert.Empty(ConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Interval_below_one_rejected()
    {
        var cfg = Valid();
        cfg.Sync.IntervalMinutes = 0;
        Assert.NotEmpty(ConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Invalid_serverUrl_rejected()
    {
        var cfg = Valid();
        cfg.Siyuan.ServerUrl = "not a url";
        Assert.NotEmpty(ConfigValidator.Validate(cfg));
    }
}
