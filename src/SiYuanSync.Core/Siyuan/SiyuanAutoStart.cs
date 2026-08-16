namespace SiYuanSync.Core.Siyuan;

/// <summary>
/// "确保思源在运行"的时序逻辑：先探测 → 未运行则启动 → 轮询等就绪。
/// 探测与启动通过委托注入（App 层提供实现），本类只负责决策与节奏，可单测。
/// </summary>
public sealed class SiyuanAutoStart
{
    public const int DefaultMaxPolls = 5;
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _interval;

    public SiyuanAutoStart(TimeSpan? pollInterval = null) => _interval = pollInterval ?? DefaultPollInterval;

    /// <summary>
    /// 探测思源可用 → true 直接返回；不可用 → launch() 后每 <paramref name="interval"/> 轮询一次，
    /// 最多 <paramref name="maxPolls"/> 次，任一次成功 → true；全部失败 → false。
    /// launch 抛出的异常向上传播（由调用方记录日志并判失败）。
    /// </summary>
    public async Task<bool> EnsureRunningAsync(
        Func<CancellationToken, Task<bool>> probe,
        Action launch,
        CancellationToken ct = default,
        int maxPolls = DefaultMaxPolls)
    {
        if (await probe(ct)) return true;

        launch();

        for (int i = 0; i < maxPolls; i++)
        {
            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { throw; }
            if (await probe(ct)) return true;
        }
        return false;
    }
}
