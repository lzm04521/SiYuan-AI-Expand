namespace SiYuanSync.Core.Models;

public sealed class ProjectConfig
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string DocPath { get; set; } = "";
    public string Notebook { get; set; } = "";
    public string ParentPath { get; set; } = "";
}
