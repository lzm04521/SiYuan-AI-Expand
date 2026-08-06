using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Config;

public static class TokenMasking
{
    public const string MaskedPlaceholder = "********";

    public static bool IsMasked(string value) => value == MaskedPlaceholder;

    /// <summary>展示用脱敏副本：Token 非空时替换为占位。</summary>
    public static AppConfig MaskedCopy(AppConfig src)
    {
        var copy = DeepCopy(src);
        if (!string.IsNullOrEmpty(copy.Siyuan.Token))
            copy.Siyuan.Token = MaskedPlaceholder;
        return copy;
    }

    /// <summary>若新值是占位或空，保留原 token。</summary>
    public static string PreserveOriginalIfMasked(string newValue, string original) =>
        string.IsNullOrEmpty(newValue) || IsMasked(newValue) ? original : newValue;

    public static AppConfig DeepCopy(AppConfig src)
    {
        // 复用序列化做深拷贝，确保 Projects 等引用类型独立
        return ConfigSerializer.Deserialize(ConfigSerializer.Serialize(src));
    }
}
