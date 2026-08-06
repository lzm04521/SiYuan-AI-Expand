using Microsoft.Extensions.Logging;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.State;

namespace SiYuanSync.Core.Sync;

public sealed class SyncEngine
{
    private readonly ConfigStore _config;
    private readonly IStateStore _state;
    private readonly Func<SiyuanConnectionConfig, ISiyuanClient> _clientFactory;
    private readonly ILogger<SyncEngine> _logger;

    public SyncEngine(ConfigStore config, IStateStore state,
        Func<SiyuanConnectionConfig, ISiyuanClient> clientFactory, ILogger<SyncEngine> logger)
    { _config = config; _state = state; _clientFactory = clientFactory; _logger = logger; }

    public async Task<SyncRunResult> RunAsync(CancellationToken ct)
    {
        // 入口取一次配置快照，全程使用
        var snapshot = _config.GetSnapshot();
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTime.UtcNow;
        var results = new List<ProjectRunResult>();

        var enabledProjects = snapshot.Projects.Where(p => p.Enabled).ToList();

        // 整轮预检：token 空/空白 → 每个项目直接 Failed，不发起任何 HTTP
        if (string.IsNullOrWhiteSpace(snapshot.Siyuan.Token))
        {
            _logger.LogWarning("未配置思源 token，整轮跳过 {Count} 个项目", enabledProjects.Count);
            foreach (var p in enabledProjects)
                results.Add(new ProjectRunResult(p.Name, RunStatus.Failed, 0, 0, 0,
                    Array.Empty<FileResult>(), "未配置思源 token（请在 Web 配置页设置）"));
            await PersistRun(runId, startedAt, results);
            return new SyncRunResult(runId, startedAt, DateTime.UtcNow, results);
        }

        // 用当前快照的 serverUrl/token 构造 client（工厂是重试包装的唯一权威：
        // 生产工厂已应用 RetryingSiyuanClient，测试注入裸 FakeClient，此处不再二次包装）
        var conn = new SiyuanConnectionConfig(snapshot.Siyuan.ServerUrl, snapshot.Siyuan.Token);
        ISiyuanClient siyuan = _clientFactory(conn);

        bool authFailed = false;
        int cancelledFromIndex = -1;
        for (int i = 0; i < enabledProjects.Count; i++)
        {
            var p = enabledProjects[i];

            // 同一实例上一项目已鉴权失败：剩余项目直接 Failed 跳过
            if (authFailed)
            {
                results.Add(new ProjectRunResult(p.Name, RunStatus.Failed, 0, 0, 0,
                    Array.Empty<FileResult>(), "上一项目鉴权失败，已停止本实例后续调用"));
                continue;
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                var pr = await ProjectSync.RunAsync(p, siyuan, _state, _logger, ct);
                results.Add(pr);
                // ProjectSync 内部把鉴权失败转成 Failed + Error 文案；同步检测以传播到剩余项目
                if (pr.Status == RunStatus.Failed && pr.Error is not null && pr.Error.Contains("鉴权失败"))
                    authFailed = true;
            }
            catch (SiyuanAuthException e)
            {
                results.Add(new ProjectRunResult(p.Name, RunStatus.Failed, 0, 0, 0,
                    Array.Empty<FileResult>(), $"鉴权失败：{e.Message}"));
                authFailed = true;
            }
            catch (OperationCanceledException)
            {
                // 按尾部语义注记：捕获取消 → 当前项目标 Cancelled → 剩余未开始项目标 Cancelled
                // → 完成已收集结果 → 持久化 → 返回（不重抛）
                results.Add(new ProjectRunResult(p.Name, RunStatus.Cancelled, 0, 0, 0,
                    Array.Empty<FileResult>(), "本轮被取消"));
                cancelledFromIndex = i + 1;
                break;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "项目 {Name} 同步异常", p.Name);
                results.Add(new ProjectRunResult(p.Name, RunStatus.Failed, 0, 0, 0,
                    Array.Empty<FileResult>(), e.Message));
            }
        }

        // 取消后剩余未开始的项目同样标 Cancelled
        if (cancelledFromIndex >= 0)
        {
            for (int i = cancelledFromIndex; i < enabledProjects.Count; i++)
            {
                var p = enabledProjects[i];
                results.Add(new ProjectRunResult(p.Name, RunStatus.Cancelled, 0, 0, 0,
                    Array.Empty<FileResult>(), "本轮被取消"));
            }
        }

        await PersistRun(runId, startedAt, results);
        return new SyncRunResult(runId, startedAt, DateTime.UtcNow, results);
    }

    private async Task PersistRun(string runId, DateTime startedAt, IReadOnlyList<ProjectRunResult> results)
    {
        var finishedAt = DateTime.UtcNow;
        foreach (var pr in results)
        {
            try
            {
                _state.RecordSyncRun(new SyncRunRecord(runId, startedAt, finishedAt,
                    pr.ProjectName, pr.Success, pr.Skipped, pr.Failed, pr.Status, pr.Error));
            }
            catch (Exception e) { _logger.LogError(e, "写入 sync_run 失败：{Project}", pr.ProjectName); }

            // 文件级明细：失败/跳过文件供前端展开查看（成功文件只计数，但仍写入便于审计与排查）
            try { _state.RecordFileDetails(runId, pr.ProjectName, pr.Files); }
            catch (Exception e) { _logger.LogError(e, "写入 file_run_detail 失败：{Project}", pr.ProjectName); }
        }
        await Task.CompletedTask;
    }
}
