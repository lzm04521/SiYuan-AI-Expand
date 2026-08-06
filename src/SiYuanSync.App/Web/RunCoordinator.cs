using SiYuanSync.Core.Sync;

namespace SiYuanSync.App.Web;

/// <summary>
/// 立即同步入口的并发守卫：用 SemaphoreSlim(1,1) 保证全局至多一轮同步在跑。
/// 立即触发把 RunAsync 放到 Task.Run 后台执行，自身立即返回，避免 HTTP 请求被长耗时阻塞。
/// </summary>
public sealed class RunCoordinator
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SyncEngine _engine;

    public bool IsRunning => _lock.CurrentCount == 0;

    public RunCoordinator(SyncEngine engine) => _engine = engine;

    /// <summary>
    /// 尝试获取执行权。成功 → 后台启动 RunAsync 并在完成时释放信号量；返回 true。
    /// 已在进行 → 返回 false（不阻塞）。
    /// </summary>
    public async Task<bool> TryStartAsync(CancellationToken ct)
    {
        if (!await _lock.WaitAsync(0, ct)) return false;
        _ = Task.Run(async () =>
        {
            try { await _engine.RunAsync(CancellationToken.None); }
            finally { _lock.Release(); }
        }, CancellationToken.None);
        return true;
    }
}
