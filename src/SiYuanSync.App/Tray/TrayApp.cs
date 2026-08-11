using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SiYuanSync.App.Web;

namespace SiYuanSync.App.Tray;

/// <summary>
/// 托盘宿主：NotifyIcon + 右键菜单 + 协同 IHost 生命周期。
/// 双击 / 菜单"打开管理页" → 默认浏览器打开 Web 配置页；
/// "立即同步"直接复用 RunCoordinator（同进程，不绕 HTTP）；
/// "设置..."打开 WinForms 设置窗口（开机自启 + 关于 + 检查更新）。
/// </summary>
public sealed class TrayApp : IDisposable
{
    private readonly IHost _host;
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu;
    private readonly string _webUrl;

    public TrayApp(IHost host, string webUrl)
    {
        _host = host;
        _webUrl = webUrl;

        _menu = new ContextMenuStrip();
        _menu.Items.Add("打开管理页", null, (_, _) => OpenWeb());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("立即同步", null, async (_, _) => await SyncNowAsync());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Exit());

        _notify = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "SiYuan-AI-Expand",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notify.DoubleClick += (_, _) => OpenWeb();
    }

    /// <summary>
    /// 从嵌入的 app-icon.ico 加载托盘图标。.ico 多尺寸，系统按托盘 DPI 选最佳。
    /// 失败回退系统图标。Icon 拥有 handle，随 NotifyIcon.Dispose 释放。
    /// </summary>
    private Icon LoadIcon()
    {
        try
        {
            var asm = typeof(TrayApp).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.Contains("app-icon.ico", StringComparison.OrdinalIgnoreCase));
            if (name is null) return SystemIcons.Application;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return SystemIcons.Application;
            return new Icon(stream);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void OpenWeb()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_webUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开管理页失败：{ex.Message}\n\n管理页地址：{_webUrl}",
                "SiYuan-AI-Expand", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task SyncNowAsync()
    {
        try
        {
            var runner = _host.Services.GetRequiredService<RunCoordinator>();
            if (runner.IsRunning)
            {
                Balloon("同步进行中", "上一轮仍在执行，已跳过本次触发。");
                return;
            }
            var started = await runner.TryStartAsync(CancellationToken.None);
            Balloon(started ? "已触发同步" : "同步进行中",
                started ? "后台执行中，完成后在 Web 状态页查看结果。" : "上一轮仍在执行，已跳过本次触发。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"触发同步失败：{ex.Message}", "SiYuan-AI-Expand",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Balloon(string title, string body)
    {
        _notify.BalloonTipTitle = title;
        _notify.BalloonTipText = body;
        _notify.ShowBalloonTip(2500);
    }

    public void Exit()
    {
        // 先隐藏图标避免任务栏残留"幽灵"图标
        _notify.Visible = false;
        Application.Exit();
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _menu.Dispose();
    }
}
