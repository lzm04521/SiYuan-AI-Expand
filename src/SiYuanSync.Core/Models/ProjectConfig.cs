namespace SiYuanSync.Core.Models;

public sealed class ProjectConfig
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string DocPath { get; set; } = "";
    public string Notebook { get; set; } = "";
    public string ParentPath { get; set; } = "";

    /// <summary>同步完成后对思源父文档设置的子文档排序方式；null=不干预。取值同思源 sortMode：0-14（3=更新时间降序，10=创建时间降序），需思源 ≥ v3.8.1。</summary>
    public int? SortMode { get; set; }

    /// <summary>静默期（分钟）：文件最后修改距今不足该值则本轮跳过；null 或 0 = 不启用。</summary>
    public int? SettleMinutes { get; set; }

    /// <summary>包含正则（匹配 docPath 相对路径，/ 分隔）：非空时仅匹配文件参与同步；空 = 不限制。</summary>
    public string IncludePattern { get; set; } = "";

    /// <summary>排除正则（匹配 docPath 相对路径，/ 分隔）：匹配文件跳过同步；空 = 不排除。</summary>
    public string ExcludePattern { get; set; } = "";

    /// <summary>删除同步：本地文件消失后删除思源端对应文档（可从思源文件历史恢复）；false = 保持只增不删。</summary>
    public bool DeleteSync { get; set; } = false;
}
