using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.State;

namespace SiYuanSync.App.Web.Endpoints;

public static class SyncEndpoints
{
    public static void Map(IEndpointRouteBuilder app, RunCoordinator runner, IStateStore state)
    {
        // 立即触发一轮同步：RunCoordinator 内部以 SemaphoreSlim(1,1) 防重入。
        // 已在跑 → 409；启动成功 → {started:true}；同步在后台执行，本接口立即返回。
        app.MapPost("/api/sync/run", async () =>
        {
            if (runner.IsRunning)
                return Results.Json(new { started = false, running = true }, statusCode: 409);
            var started = await runner.TryStartAsync(CancellationToken.None);
            return started
                ? Results.Json(new { started = true })
                : Results.Json(new { started = false, running = true }, statusCode: 409);
        });

        // 状态聚合：取最近一轮 run_id 的项目级摘要，再补该轮下失败/跳过文件的明细。
        // 成功文件只计数，不列出（设计 9.2）。
        app.MapGet("/api/status", () =>
        {
            var runs = state.GetLatestRunByRunId();
            if (runs.Count == 0)
                return Results.Json(new { runId = (string?)null, projects = Array.Empty<object>(), details = Array.Empty<object>() });

            var runId = runs[0].RunId;
            var details = state.GetFailedOrSkipped(runId);

            var projects = runs.Select(r => new
            {
                project = r.ProjectName,
                startedAt = r.StartedAt,
                finishedAt = r.FinishedAt,
                success = r.SuccessCount,
                skipped = r.SkippedCount,
                failed = r.FailedCount,
                status = r.Status.ToString(),
                error = r.Error
            }).ToList();

            var fileDetails = details.Select(d => new
            {
                project = d.ProjectName,
                relPath = d.RelPath,
                outcome = d.Outcome.ToString(),
                error = d.Error
            }).ToList();

            return Results.Json(new { runId, projects, details = fileDetails });
        });
    }
}
