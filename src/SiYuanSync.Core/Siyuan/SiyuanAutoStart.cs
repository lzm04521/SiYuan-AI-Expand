namespace SiYuanSync.Core.Siyuan;

/// <summary>
/// "确保思源在运行"的时序逻辑：先探测 → 未运行则启动 → 固定等待（给内核启动留时间）→ 轮询等就绪。
/// 探测与启动通过委托注入（App 层提供实现），本类只负责决策与节奏，可单测。
/// </summary>
public sealed class SiyuanAutoStart
{
    public const int DefaultMaxPolls = 5;
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);
    /// <summary>拉起思源进程后、第一次探测前的固定等待：内核初始化（加载工作空间/笔记本）需要时间，过早探测到端口已通就放行会导致同步失败。</summary>
    public static readonly TimeSpan DefaultPostLaunchDelay = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _interval;
    private readonly TimeSpan _postLaunchDelay;

    public SiyuanAutoStart(TimeSpan? pollInterval = null, TimeSpan? postLaunchDelay = null)
    { _interval = pollInterval ?? DefaultPollInterval; _postLaunchDelay = postLaunchDelay ?? DefaultPostLaunchDelay; }

    /// <summary>
    /// 探测思源可用 → true 直接返回；不可用 → launch() 后先固定等待 <paramref name="postLaunchDelay"/>
    /// 再每 <paramref name="interval"/> 轮询一次，最多 <paramref name="maxPolls"/> 次，
    /// 任一次成功 → true；全部失败 → false。launch 抛出的异常向上传播（由调用方记录日志并判失败）。
    /// </summary>
    public async Task<bool> EnsureRunningAsync(
        Func<CancellationToken, Task<bool>> probe,
        Action launch,
        CancellationToken ct = default,
        int maxPolls = DefaultMaxPolls)
    {
        if (await probe(ct)) return true;

        launch();

        // 思源进程已拉起但内核尚在启动：先固定等待，再开始就绪轮询
        await Task.Delay(_postLaunchDelay, ct);

        for (int i = 0; i < maxPolls; i++)
        {
            if (await probe(ct)) return true;
            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { throw; }
        }
        return false;
    }
}
