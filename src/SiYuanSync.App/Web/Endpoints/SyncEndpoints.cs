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

        // 同步日志：历史轮次列表（按 runId 分组），支持项目筛选与日期范围（本地日期，含当天）。
        // 多取一轮仅用于判断 hasMore，"加载更多"按 offset 递增。
        app.MapGet("/api/sync/history", (string? project, string? from, string? to, int? limit, int? offset) =>
        {
            DateTime? fromUtc = ParseLocalDateToUtc(from, includeDay: false);
            DateTime? toUtc = ParseLocalDateToUtc(to, includeDay: true);
            if ((from != null && fromUtc is null) || (to != null && toUtc is null))
                return Results.Json(new { message = "日期格式须为 yyyy-MM-dd" }, statusCode: 400);

            int lim = Math.Clamp(limit ?? 20, 1, 100);
            int off = Math.Max(offset ?? 0, 0);
            var rows = state.ListSyncRuns(lim + 1, off,
                string.IsNullOrEmpty(project) ? null : project, fromUtc, toUtc);

            var grouped = rows.GroupBy(r => r.RunId).ToList();
            var hasMore = grouped.Count > lim;
            var runs = grouped.Take(lim).Select(g => new
            {
                runId = g.Key,
                startedAt = g.First().StartedAt,
                projects = g.Select(r => new
                {
                    project = r.ProjectName,
                    startedAt = r.StartedAt,
                    finishedAt = r.FinishedAt,
                    success = r.SuccessCount,
                    skipped = r.SkippedCount,
                    failed = r.FailedCount,
                    status = r.Status.ToString(),
                    error = r.Error
                }).ToList()
            }).ToList();

            return Results.Json(new { runs, hasMore });
        });

        // 同步日志：某轮全量文件明细（含成功，区分新建/更新）。
        app.MapGet("/api/sync/history/{runId}/details", (string runId) =>
        {
            var details = state.GetFileDetails(runId);
            return Results.Json(new
            {
                runId,
                details = details.Select(d => new
                {
                    project = d.ProjectName,
                    relPath = d.RelPath,
                    outcome = d.Outcome.ToString(),
                    error = d.Error
                }).ToList()
            });
        });
    }

    /// <summary>本地日期 yyyy-MM-dd → UTC 时间点；includeDay=true 时取次日零点（作开区间上界，覆盖当天）。</summary>
    private static DateTime? ParseLocalDateToUtc(string? s, bool includeDay)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (!DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
            return null;
        if (includeDay) d = d.AddDays(1);
        return DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime();
    }
}
