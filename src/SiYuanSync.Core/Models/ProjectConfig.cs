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
}
