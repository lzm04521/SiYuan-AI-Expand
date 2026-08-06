using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Config;

public static class ConfigValidator
{
    private static readonly HashSet<string> LoopbackBinds =
        new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "::1" };

    public static IReadOnlyList<string> Validate(AppConfig cfg)
    {
        var errors = new List<string>();

        // Web.bind
        var allowedBinds = new[] { "127.0.0.1", "localhost", "0.0.0.0", "::1" };
        if (!allowedBinds.Contains(cfg.Web.Bind, StringComparer.OrdinalIgnoreCase))
            errors.Add($"web.bind 非法：'{cfg.Web.Bind}'，仅允许 127.0.0.1/localhost/0.0.0.0/::1");

        // Web.port
        if (cfg.Web.Port < 1 || cfg.Web.Port > 65535)
            errors.Add($"web.port 越界：{cfg.Web.Port}，须在 1-65535");

        // 非 loopback 必须有密码
        bool isLoopback = LoopbackBinds.Contains(cfg.Web.Bind);
        if (!isLoopback && string.IsNullOrWhiteSpace(cfg.Web.Password))
            errors.Add("web.bind 为非 loopback 时 web.password 不得为空");

        // Sync.interval
        if (cfg.Sync.IntervalMinutes < 1)
            errors.Add($"sync.intervalMinutes 须 ≥ 1，当前 {cfg.Sync.IntervalMinutes}");

        // Siyuan.serverUrl
        if (!Uri.IsWellFormedUriString(cfg.Siyuan.ServerUrl, UriKind.Absolute))
            errors.Add($"siyuan.serverUrl 非法绝对 URL：'{cfg.Siyuan.ServerUrl}'");

        return errors;
    }
}
