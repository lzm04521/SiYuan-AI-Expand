using SiYuanSync.Core.Models;
using SiYuanSync.Core.Sync;

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

        // 项目名唯一
        var dupNames = cfg.Projects.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key)).ToList();
        foreach (var g in dupNames)
            errors.Add($"项目 name 重复：'{g.Key}'");

        // sortMode：空=不干预；非空须在思源合法范围 0-14（15=跟随文档树 等价于不设置，无意义故不允许）
        foreach (var p in cfg.Projects)
        {
            if (p.SortMode is int sm && (sm < 0 || sm > 14))
                errors.Add($"项目 '{p.Name}' sortMode 越界：{sm}，须为 0-14（3=更新时间降序，10=创建时间降序）或留空");
        }

        // parentPath：空允许（MCP add_project 的中间态）；非空必须是规范 hpath（与思源 getIDsByHPath 精确匹配一致）
        foreach (var p in cfg.Projects)
        {
            if (string.IsNullOrWhiteSpace(p.ParentPath)) continue;
            try
            {
                var norm = PathNormalizer.NormalizeParentPath(p.ParentPath);
                if (norm != p.ParentPath)
                    errors.Add($"项目 '{p.Name}' parentPath 不规范：'{p.ParentPath}'，应为 '{norm}'");
            }
            catch (PathNormalizerException e)
            { errors.Add($"项目 '{p.Name}' parentPath 非法：{e.Message}"); }
        }

        // (notebook, parentPath) 唯一；notebook 空白回退 defaultNotebook
        string DefaultNb() => string.IsNullOrWhiteSpace(cfg.Siyuan.DefaultNotebook) ? "" : cfg.Siyuan.DefaultNotebook.Trim();
        var seen = new Dictionary<(string nb, string path), string>(); // key → 首个项目名
        foreach (var p in cfg.Projects)
        {
            var nb = string.IsNullOrWhiteSpace(p.Notebook) ? DefaultNb() : p.Notebook.Trim();
            var path = (p.ParentPath ?? "").Trim().TrimEnd('/');
            var key = (nb, path);
            if (seen.TryGetValue(key, out var prev))
                errors.Add($"项目 '{prev}' 与 '{p.Name}' 指向同一思源父路径 (notebook={nb}, parentPath={path})");
            else
                seen[key] = p.Name;
        }

        // docPath 不重叠（规范化后相同或父子）
        var normalized = cfg.Projects
            .Where(p => !string.IsNullOrWhiteSpace(p.DocPath))
            .Select(p => (name: p.Name, full: NormalizeDir(p.DocPath)))
            .ToList();
        for (int i = 0; i < normalized.Count; i++)
            for (int j = i + 1; j < normalized.Count; j++)
            {
                var a = normalized[i]; var b = normalized[j];
                // 双向检测父子关系，覆盖列表顺序无关的嵌套（深路径在前或在前均识别）
                if (IsSameOrAncestor(a.full, b.full) || IsSameOrAncestor(b.full, a.full))
                    errors.Add($"项目 '{a.name}' 与 '{b.name}' 的 docPath 重叠：'{a.full}' ↔ '{b.full}'");
            }

        return errors;
    }

    private static string NormalizeDir(string path)
    {
        try { return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path.Trim(); }
    }

    private static bool IsSameOrAncestor(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        var aSeg = a.EndsWith(Path.DirectorySeparatorChar) ? a : a + Path.DirectorySeparatorChar;
        return b.StartsWith(aSeg, StringComparison.OrdinalIgnoreCase);
    }
}
