using System.Diagnostics;
using SiYuanSync.Core.Siyuan;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class SiyuanAutoStartTests
{
    private static SiyuanAutoStart New() => new(
        pollInterval: TimeSpan.FromMilliseconds(1),
        postLaunchDelay: TimeSpan.FromMilliseconds(1));

    [Fact]
    public async Task Already_running_skips_launch()
    {
        int launches = 0, probes = 0;
        var ok = await New().EnsureRunningAsync(
            probe: _ => { probes++; return Task.FromResult(true); },
            launch: () => launches++,
            maxPolls: 5);

        Assert.True(ok);
        Assert.Equal(0, launches);
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task Not_running_launches_then_polls_until_ready()
    {
        int launches = 0, probes = 0;
        // 探测序列：初始不可用 → 启动后第 3 次轮询才就绪
        var ok = await New().EnsureRunningAsync(
            probe: _ => { probes++; return Task.FromResult(probes >= 4); },
            launch: () => launches++,
            maxPolls: 5);

        Assert.True(ok);
        Assert.Equal(1, launches);
        Assert.Equal(4, probes); // 1 次初始 + 3 次轮询
    }

    [Fact]
    public async Task Not_ready_after_all_polls_returns_false()
    {
        int launches = 0, probes = 0;
        var ok = await New().EnsureRunningAsync(
            probe: _ => { probes++; return Task.FromResult(false); },
            launch: () => launches++,
            maxPolls: 5);

        Assert.False(ok);
        Assert.Equal(1, launches);
        Assert.Equal(6, probes); // 1 次初始 + 5 次轮询
    }

    [Fact]
    public async Task Launch_exception_propagates()
    {
        // 启动失败（如找不到 exe）必须向上抛，由调用方记录日志并判失败，不得静默当作已就绪
        await Assert.ThrowsAsync<InvalidOperationException>(() => New().EnsureRunningAsync(
            probe: _ => Task.FromResult(false),
            launch: () => throw new InvalidOperationException("未找到思源 exe"),
            maxPolls: 5));
    }

    [Fact]
    public async Task Cancellation_during_polling_propagates()
    {
        using var cts = new CancellationTokenSource(10);
        // 探测一直不可用 + 轮询间隔长于取消时限 → Task.Delay 取消
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SiyuanAutoStart(
            pollInterval: TimeSpan.FromSeconds(30)).EnsureRunningAsync(
            probe: _ => Task.FromResult(false),
            launch: () => { },
            cts.Token,
            maxPolls: 5));
    }

    [Fact]
    public async Task First_poll_happens_only_after_post_launch_delay()
    {
        // 拉起后必须先等满 postLaunchDelay 才做第一次就绪探测：
        // 探测序列固定为 初始失败 → 拉起 → 等待 → 首次轮询成功，总耗时即等待耗时
        var sw = Stopwatch.StartNew();
        int probes = 0;
        var ok = await new SiyuanAutoStart(
            pollInterval: TimeSpan.FromMilliseconds(1),
            postLaunchDelay: TimeSpan.FromMilliseconds(300)).EnsureRunningAsync(
            probe: _ => Task.FromResult(++probes > 1),
            launch: () => { },
            maxPolls: 5);

        Assert.True(ok);
        Assert.True(sw.ElapsedMilliseconds >= 300,
            $"拉起后未等满 postLaunchDelay 即放行，实际 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Defaults_are_60s_settle_then_5_polls_every_30_seconds()
    {
        // 注：本文件内 SiYuanAutoStart 的非限定静态成员访问会触发 CS0103（new 表达式正常），
        // 疑似 net10.0 编译器怪癖，故此测试用全限定名
        Assert.Equal(5, SiYuanSync.Core.Siyuan.SiyuanAutoStart.DefaultMaxPolls);
        Assert.Equal(TimeSpan.FromSeconds(30), SiYuanSync.Core.Siyuan.SiyuanAutoStart.DefaultPollInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), SiYuanSync.Core.Siyuan.SiyuanAutoStart.DefaultPostLaunchDelay);
    }
}
