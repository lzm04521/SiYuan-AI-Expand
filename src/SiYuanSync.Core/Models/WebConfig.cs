namespace SiYuanSync.Core.Models;

public sealed class WebConfig
{
    public int Port { get; set; } = 6807;
    public string Bind { get; set; } = "127.0.0.1";
    public string Password { get; set; } = "";
}
