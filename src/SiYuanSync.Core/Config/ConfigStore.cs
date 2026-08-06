using System.Text;
using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Config;

public sealed class ConfigCorruptException : Exception
{
    public ConfigCorruptException(string message) : base(message) { }
}

public sealed class ConfigValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }
    public ConfigValidationException(IReadOnlyList<string> errors)
        : base("配置校验失败：" + string.Join("; ", errors)) => Errors = errors;
}

public sealed class ConfigStore
{
    private readonly string _path;
    private readonly string _tmpPath;
    public ConfigStore(string path)
    {
        _path = path;
        _tmpPath = path + ".tmp";
    }

    public AppConfig LoadOrInit()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 残留 .tmp 视为上次替换中断：提升为主文件
        if (File.Exists(_tmpPath))
        {
            File.Copy(_tmpPath, _path, overwrite: true);
            File.Delete(_tmpPath);
        }

        if (!File.Exists(_path))
        {
            var fresh = new AppConfig();
            WriteAtomic(fresh);
            return fresh;
        }

        string json;
        try { json = File.ReadAllText(_path, Encoding.UTF8); }
        catch (Exception e) { throw new ConfigCorruptException($"读取 config.json 失败：{e.Message}"); }

        AppConfig cfg;
        try { cfg = ConfigSerializer.Deserialize(json); }
        catch (Exception e) { throw new ConfigCorruptException($"config.json JSON 非法：{e.Message}"); }

        var errs = ConfigValidator.Validate(cfg);
        if (errs.Count > 0)
            throw new ConfigCorruptException("config.json 校验失败：" + string.Join("; ", errs));

        return cfg;
    }

    public void Save(AppConfig cfg)
    {
        var errs = ConfigValidator.Validate(cfg);
        if (errs.Count > 0) throw new ConfigValidationException(errs);
        WriteAtomic(cfg);
    }

    private void WriteAtomic(AppConfig cfg)
    {
        var json = ConfigSerializer.Serialize(cfg);
        // 残留旧 tmp 丢弃
        if (File.Exists(_tmpPath)) File.Delete(_tmpPath);

        using (var fs = new FileStream(_tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            sw.Write(json);
            sw.Flush();
            fs.Flush(flushToDisk: true);
        }
        if (File.Exists(_path)) File.Replace(_tmpPath, _path, null);
        else File.Move(_tmpPath, _path);
    }
}
