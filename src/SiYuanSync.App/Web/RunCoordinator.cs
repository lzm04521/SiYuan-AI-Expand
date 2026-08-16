using Microsoft.Extensions.Logging;
using SiYuanSync.App.Siyuan;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Sync;

namespace SiYuanSync.App.Web;

/// <summary>
/// 立即同步入口的并发守卫：用 SemaphoreSlim(1,1) 保证全局至多一轮同步在跑。
/// 立即触发把 RunAsync 放到 Task.Run 后台执行，自身立即返回，避免 HTTP 请求被长耗时阻塞。
/// 开启 siyuan.autoStartOnSync 时，同步前先确保思源在运行（未运行则隐藏启动并轮询就绪）。
/// </summary>
public sealed class RunCoordinator
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SyncEngine _engine;
    private readonly ConfigStore _config;
    private readonly SiyuanAutoStartService _siyuanAutoStart;
    private readonly ILogger<RunCoordinator> _logger;

    public bool IsRunning => _lock.CurrentCount == 0;

    public RunCoordinator(SyncEngine engine, ConfigStore config, SiyuanAutoStartService siyuanAutoStart,
        ILogger<RunCoordinator> logger)
    { _engine = engine; _config = config; _siyuanAutoStart = siyuanAutoStart; _logger = logger; }

    /// <summary>
    /// 尝试获取执行权。成功 → 后台启动 RunAsync 并在完成时释放信号量；返回 true。
    /// 已在进行 → 返回 false（不阻塞）。
    /// </summary>
    public async Task<bool> TryStartAsync(CancellationToken ct)
    {
        if (!await _lock.WaitAsync(0, ct)) return false;
        _ = Task.Run(async () =>
        {
            try
            {
                // 思源自动拉起（开关开启且未运行时）：就绪才继续，未就绪跳过本轮（等待期间持锁，
                // 下一轮定时触发会因 IsRunning 跳过，与长耗时同步一致）
                var snap = _config.GetSnapshot();
                if (snap.Siyuan.AutoStartOnSync &&
                    !await _siyuanAutoStart.EnsureRunningAsync(snap.Siyuan, CancellationToken.None))
                {
                    _logger.LogWarning("思源未就绪，跳过本轮同步");
                    return;
                }
                await _engine.RunAsync(CancellationToken.None);
            }
            finally { _lock.Release(); }
        }, CancellationToken.None);
        return true;
    }
}
