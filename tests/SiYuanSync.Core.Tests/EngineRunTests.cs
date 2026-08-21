using Microsoft.Extensions.Logging.Abstractions;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.State;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.Sync;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class EngineRunTests : IDisposable
{
    private readonly string _dir;
    private readonly string _cfgPath;
    private readonly string _dbPath;
    public EngineRunTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sye-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cfgPath = Path.Combine(_dir, "config.json");
        _dbPath = Path.Combine(_dir, "state.db");
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private ConfigStore NewStore(AppConfig? seed = null)
    {
        var s = new ConfigStore(_cfgPath);
        s.Initialize();
        if (seed is not null) s.Update(_ => { _.Siyuan = seed.Siyuan; _.Sync = seed.Sync; _.Projects = seed.Projects; });
        return s;
    }

    private sealed class FakeClient : ISiyuanClient
    {
        public List<Notebook> Notebooks = new();
        public Dictionary<string, string[]> ByHPath = new();
        public bool TokenRequired = true; // 模拟 token 有无时不发
        public Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Notebook>>(Notebooks);
        public Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string n, string h, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(ByHPath.TryGetValue(h, out var v) ? v : Array.Empty<string>());
        public Task<string> CreateDocWithMdAsync(string n, string h, string m, CancellationToken ct) => Task.FromResult("doc");
        public Task RenameDocByIdAsync(string id, string t, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveDocByIdAsync(string id, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string d, CancellationToken ct) => Task.FromResult<IReadOnlyList<BlockChild>>(Array.Empty<BlockChild>());
        public Task DeleteBlockAsync(string b, CancellationToken ct) => Task.CompletedTask;
        public Task PrependBlockAsync(string p, string m, CancellationToken ct) => Task.CompletedTask;
        public Task SetDocSortModeAsync(string d, int s, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Empty_token_short_circuits_no_http()
    {
        var cfg = new AppConfig { Siyuan = { Token = "" }, Projects = { new() { Name = "P", Enabled = true, DocPath = Path.Combine(_dir, "doc"), Notebook = "AI", ParentPath = "/P" } } };
        var store = NewStore(cfg);
        using var state = new StateStore(_dbPath);

        var engine = new SyncEngine(store, state, _ => new FakeClient(), NullLogger<SyncEngine>.Instance);
        var result = await engine.RunAsync(default);

        Assert.All(result.Projects, p => Assert.Equal(RunStatus.Failed, p.Status));
        Assert.All(result.Projects, p => Assert.Contains("token", p.Error!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Disabled_projects_are_skipped()
    {
        // 各项目使用独立 docPath，规避 Task 6 docPath 重叠校验
        var onDir = Path.Combine(_dir, "on"); Directory.CreateDirectory(onDir);
        File.WriteAllText(Path.Combine(onDir, "a.md"), "# A\n");
        var offDir = Path.Combine(_dir, "off"); Directory.CreateDirectory(offDir);
        var cfg = new AppConfig { Siyuan = { Token = "tok" }, Projects = {
            new() { Name = "ON", Enabled = true, DocPath = onDir, Notebook = "AI", ParentPath = "/ON" },
            new() { Name = "OFF", Enabled = false, DocPath = offDir, Notebook = "AI", ParentPath = "/OFF" } } };
        var store = NewStore(cfg);
        using var state = new StateStore(_dbPath);
        var fake = new FakeClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/ON"] = new[] { "p" } } };

        var engine = new SyncEngine(store, state, _ => fake, NullLogger<SyncEngine>.Instance);
        var result = await engine.RunAsync(default);

        Assert.DoesNotContain(result.Projects, p => p.ProjectName == "OFF");
    }

    [Fact]
    public async Task Cancellation_does_not_mark_success()
    {
        var docDir = Path.Combine(_dir, "doc"); Directory.CreateDirectory(docDir);
        File.WriteAllText(Path.Combine(docDir, "a.md"), "# A\n");
        var cfg = new AppConfig { Siyuan = { Token = "tok" }, Projects = { new() { Name = "P", Enabled = true, DocPath = docDir, Notebook = "AI", ParentPath = "/P" } } };
        var store = NewStore(cfg);
        using var state = new StateStore(_dbPath);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 进入即取消
        var engine = new SyncEngine(store, state, _ => new FakeClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/P"] = new[] { "p" } } }, NullLogger<SyncEngine>.Instance);

        var result = await engine.RunAsync(cts.Token);
        Assert.All(result.Projects, p => Assert.NotEqual(RunStatus.Success, p.Status));
    }

    [Fact]
    public async Task All_projects_share_same_runId_and_persisted()
    {
        // 各项目使用独立 docPath，规避 Task 6 docPath 重叠校验
        var paDir = Path.Combine(_dir, "pa"); Directory.CreateDirectory(paDir);
        File.WriteAllText(Path.Combine(paDir, "a.md"), "# A\n");
        var pbDir = Path.Combine(_dir, "pb"); Directory.CreateDirectory(pbDir);
        File.WriteAllText(Path.Combine(pbDir, "b.md"), "# B\n");
        var cfg = new AppConfig { Siyuan = { Token = "tok" }, Projects = {
            new() { Name = "PA", Enabled = true, DocPath = paDir, Notebook = "AI", ParentPath = "/PA" },
            new() { Name = "PB", Enabled = true, DocPath = pbDir, Notebook = "AI", ParentPath = "/PB" } } };
        var store = NewStore(cfg);
        using var state = new StateStore(_dbPath);

        var engine = new SyncEngine(store, state, _ => new FakeClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/PA"] = new[] { "p" }, ["/PB"] = new[] { "p" } } }, NullLogger<SyncEngine>.Instance);
        var result = await engine.RunAsync(default);

        Assert.Equal(2, result.Projects.Count);
        var latest = state.GetLatestRunByRunId();
        Assert.Equal(2, latest.Count);
        Assert.All(latest, r => Assert.Equal(result.RunId, r.RunId));
    }
}
