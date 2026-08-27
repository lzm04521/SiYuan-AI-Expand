# AGENTS.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 协作与范围

- 除代码、命令、路径和专有名词外，回复使用简体中文。
- 本项目使用 Git 托管；修改前先运行 `git status --short`，保护用户已有未提交修改。
- 优先用 CodeGraph（`.codegraph/` 已建索引）查定义、引用、调用链与符号信息；不可用时退回文本搜索与文件读取。
- 只做与目标直接相关的最小改动，遵循项目既有命名与结构。

## 变更流程

- 修改代码、配置、发布脚本或新增文件前，先在 `doc/` 新建或更新实施方案，说明目标、范围、步骤、风险和验证方式；`doc/` 目录忽略代码托管（不提交）。
- 实施方案需用户确认后再执行。
- doc 命名方式为 `日期-标题.md`，例如 `20260101-A方案.md`（本仓库既有文档均用 `yyyyMMdd-类型-标题.md` 形式，如 `20260822-设计文档-HTML报告同步.md`，保持一致）。
- 只读分析、问题解答、现有文档评审不需要创建实施文档。
- 新增文件、模块、类、函数或 helper 前，必须先产出 Dedupe Ticket，包含：Intent signature、Queries、Top matches、Decision、Rationale。

## 构建与运行

- 构建：`dotnet build`（.NET 10 SDK，`Directory.Build.props` 集中配置 TargetFramework net10.0 / Nullable enable / `TreatWarningsAsErrors=true` / 版本号）。
- 运行测试：`dotnet test`（全部）；`dotnet test tests/SiYuanSync.Core.Tests`（仅测试项目）；单类过滤 `dotnet test tests/SiYuanSync.Core.Tests --filter "FullyQualifiedName~DocScannerTests"`。
- 发布：`.\publish.ps1` → 产物在 `publish\`（主 exe + Updater exe + 升级包 zip，单文件自包含）。
- 运行形态：托盘模式（安装目录双击 `SiYuan-AI-Expand.exe`）；调试用 `--console` 实时输出 INFO 日志。Web 管理页默认 `http://127.0.0.1:61122/`。
- 运行时数据目录 `%LocalAppData%\SiYuan-AI-Expand\`（config.json / state.db / logs / update），首次启动自动创建。

## 测试

- 框架：xUnit（2.9.0），测试项目 `tests/SiYuanSync.Core.Tests`（引用 Core，`InternalsVisibleTo` 放行 internal）。
- 开发方式 TDD：Core 层先行，通过 `ISiyuanClient` mock 思源 API，不依赖真实思源实例；思源集成测试为可选（需本地思源实例，CI 不跑）。
- 覆盖面：首次/增量/删除同步、父目录缺失跳过、思源不可达、认证失败停止、重试分类、取消与停止、配置并发写入与快照隔离、路径安全、token 未配置整轮跳过、项目间冲突拒绝、SQLite 并发、HTML 预处理等。
- `TreatWarningsAsErrors=true`：测试与业务代码同等受警告即错误约束。

## 宿主进程

- `SiYuan-AI-Expand.exe`：唯一常驻进程（Windows 托盘）。内含：NotifyIcon 托盘（`Tray/TrayApp.cs`）、周期同步 Worker（`Worker/TimedSyncService.cs`）、Kestrel Web 管理页与 MCP 端点（`Web/`、`Mcp/`，共用同一端口，默认 127.0.0.1:61122）。
- `SiYuan-AI-Expand-Updater.exe`：独立升级程序，仅在升级时由主程序拉起（等主进程退出 → 解压覆盖安装目录 → 重启主程序），非常驻。

## 高层架构

分 **Core**（纯逻辑、无宿主依赖、可独立单测）与 **App**（宿主：托盘 + Worker + Web）与 **Updater**（独立升级程序）三层。

**Core（`src/SiYuanSync.Core/`）**

- 同步管线（`Sync/`）：`DocScanner` 扫描 `docPath` 下 `.md`/`.html`/`.htm` 并检测跨后缀 hpath 冲突（去后缀后同名冲突报错跳过）→ `HtmlPreprocessor`（AngleSharp 抽 body + ReverseMarkdown 转 Markdown，剥 script/样式/注释与包裹标签）→ `ContentPreprocessor`（剥离 md 首行一级标题）→ `DocUpsert` 按 hpath upsert → `ProjectSync` 单项目编排 → `SyncEngine` 整轮编排（取配置快照 → 逐项目 → 写状态）。`PathNormalizer` 负责本地相对路径 → 思源 hpath 映射（`hpath = parentPath + 相对路径去后缀、分隔符转 /`）。
- 身份按 hpath，不持久化 docID：每轮 `getIDsByHPath` 查找，未命中→创建；命中→保留 docID，取子块逐个删除后 `prependBlock` 插入新正文。
- 增量按文件正文 SHA256：与 state.db 记录相同跳过；思源操作成功才写回 hash。
- 单向、只增不删：本地删除的文件思源文档保留，仅 state.db 标记。
- 思源客户端（`Siyuan/`）：`ISiyuanClient` / `SiyuanClient`（kernel HTTP API + Token 认证）/ `RetryingSiyuanClient`（重试包装，区分瞬时错误 `SiyuanTransientException`（超时/5xx）与认证错误 `SiyuanAuthException`（401/403，不重试））/ `SiyuanAutoStart`（同步前确保思源运行：探测 → 启动 → 固定等待 60s → 30s 间隔轮询最多 5 次）。
- 配置（`Config/`）：`ConfigStore` 进程内内存权威 + `ReaderWriterLockSlim` 保护，Web 保存时更新内存并原子写回 config.json；`ConfigValidator` / `ConfigSerializer` / `TokenMasking`（脱敏）。
- 状态（`State/`）：`StateStore` SQLite（WAL + busy_timeout），存内容 hash、sync_run 历史、文件级错误明细。
- 依赖：AngleSharp 1.5.2、ReverseMarkdown 6.2.1、Microsoft.Data.Sqlite 9.0.0（本地内嵌状态库，无外部数据库）。

**App（`src/SiYuanSync.App/`）**

- `Program.cs` 托盘 / `--console` 双模式入口（`[STAThread]`）；`Hosting/HostBuilder.cs` 组装。
- `Worker/TimedSyncService.cs`：`BackgroundService` + `PeriodicTimer` 周期触发 `SyncEngine.RunAsync`，`SemaphoreSlim` 防重入；`Web/RunCoordinator.cs` 保证"立即同步"与周期同步全局至多一轮。
- `Web/`：Kestrel 托管静态页（`wwwroot/`）+ REST 端点（`Endpoints/`：Config / Project / Siyuan / Sync / System），认证中间件（loopback 免认证，非 loopback 强制密码 + session + CSRF + 请求体限制 + 登录限速）。
- `Mcp/McpEndpoints.cs`：MCP Streamable HTTP 端点（`POST /mcp`，JSON-RPC 2.0，协议 2025-06-18），与 Web 管理台共用端口，来源硬限制本机，供 Claude Desktop / Cursor 等 AI 客户端接入。
- `Update/UpdateChecker.cs`：查 GitHub Releases + 语义版本比对（与 `Directory.Build.props` 的 `<Version>` 比）+ 资产下载。
- `Autostart/AutostartService.cs`：读写 `HKCU\...\Run` 注册表开机自启。
- `Siyuan/SiyuanAutoStartService.cs`：为 `SiyuanAutoStart` 提供 App 层的探测/拉起实现。

**Updater（`src/SiYuanSync.Updater/`）**：`--apply --pid --dir --zip` 命令行驱动，等主进程退出后解压覆盖安装目录（跳过运行中的自身）并重启主程序。

## 核心框架类型

- .NET 10（App/Updater 为 net10.0-windows），`Nullable` + `ImplicitUsings` + `TreatWarningsAsErrors` 全局开启（见 `Directory.Build.props`）。
- 宿主模型：`Microsoft.Extensions.Hosting` 的 `BackgroundService`；Kestrel + 最小 API 路由（`IEndpointRouteBuilder`）；WinForms `NotifyIcon` 托盘。
- 关键接口与异常体系：`ISiyuanClient`（单测 mock 点）、`IStateStore`、`SiyuanTransientException` / `SiyuanAuthException` / `SiyuanOperationException`（`McpToolException` 在 App 层）。
- 配置模型（`Models/`）：`AppConfig`（siyuan / sync / web / projects）+ `SiyuanConfig` / `SyncConfig` / `WebConfig` / `ProjectConfig` / `McpConfig`。

## 配置与集成

- 主配置 `config.json`（`%LocalAppData%\SiYuan-AI-Expand\`）：`siyuan`（serverUrl / token / defaultNotebook）、`sync`（intervalMinutes / runOnStart）、`web`（port / bind / password）、`projects[]`（name / enabled / docPath / notebook / parentPath / sortMode）。完整字段语义见 `doc/20260806-设计文档-思源笔记doc自动同步工具.md` 第 5 节。
- 生效语义：Web 配置页修改即时生效（内存权威，下一轮同步/下一请求采用新值）；直接编辑 config.json 需重启；`web.bind` / `web.port` 必须重启（Kestrel 启动时绑定）。
- 外部集成：思源笔记 kernel HTTP API（默认 `127.0.0.1:6806`，Token 认证，依赖 `prependBlock` / `renameDocByID` / `getIDsByHPath` / `getChildBlocks` / `deleteBlock` 等内核 API）；GitHub Releases（升级检查与 CI 发布）。
- 发布链路：`.\publish.ps1` → `git tag vX.Y.Z` → `git push origin vX.Y.Z` → GitHub Actions（`.github/workflows/release.yml`）自动构建 win-x64 产物并创建 Release；升级包资产名 `SiYuan-AI-Expand-<version>-win-x64.zip`。
- 本地 SQLite `state.db` 为应用内嵌状态库，非外部托管数据库。

## 版本控制

- 本项目使用 Git 托管；修改前运行 `git status --short`，保护用户未提交修改。
- 不自动 commit、push、重置、清理或切换分支；发布打 tag 按上文发布链路走。
- 本仓库使用 git worktree 工作流：特性分支在 `.claude/worktrees/<branch>` 下开发，完成后合回 main。
