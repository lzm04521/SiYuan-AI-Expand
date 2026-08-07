# SiYuan-AI-Expand

## 项目简介

SiYuan-AI-Expand 是一个 Windows 常驻服务，把多个项目的 `doc/` 目录下的 Markdown 文档**单向、增量**同步到思源笔记，形成统一的 AI 知识库。

起因：使用 Claude Code 处理项目问题时，方案/诊断文档（`.md`）会生成到各项目的 `doc/` 目录，分散在各仓库里查阅与归档不便。本工具按配置周期扫描这些目录，保留子目录结构映射为思源文档树，内容变化（SHA256）才推送，未变不重发。

## 功能特性

- **多项目配置**：每个项目独立配置名称、本地 `doc/` 目录、目标笔记本、目标父文档路径（hpath）。
- **周期增量同步**：默认每 10 分钟扫描，按文件正文 SHA256 内容 hash 判断变更，未变跳过。
- **单向、只增不删**：本地新增→建文档；内容更新→覆盖同名文档正文；本地删除→思源文档保留。
- **保留子目录结构**：本地子目录天然映射为思源子文档树。
- **Web 配置页**：思源连接、项目 CRUD、父目录初始化、立即同步、同步状态一览，全部浏览器操作。
- **双运行模式**：Windows 服务（开机自启 + 失败自动重启）与 `--console` 交互调试。
- **安全**：默认 loopback 免认证；非 loopback 绑定强制密码 + session；修改请求校验 CSRF；请求体大小限制；敏感字段脱敏。
- **配置 Web 即时生效**：通过 Web 配置页修改的业务字段（token、项目、周期等）下一轮同步即采用新值，无需重启。

## 架构

分 **Core**（纯逻辑、无宿主依赖、可独立单测）与 **App**（宿主：Worker + Web）两层。

```
SiYuan-AI-Expand.exe（单文件自包含）
├── TimedSyncService  (BackgroundService)  PeriodicTimer + SemaphoreSlim 防重入
├── WebConfigHost     (Kestrel)            静态页 + REST 端点，复用 SyncEngine 提供"立即同步"
└── 共享 SyncEngine / ConfigStore / StateStore
        ├── SyncEngine    纯逻辑核心，无状态；输入项目配置 → 扫描与 upsert
        ├── SiyuanClient  封装思源 kernel HTTP API（Token 认证 + 重试包装）
        ├── ConfigStore   进程内配置为运行权威，启动时加载 config.json，Web 保存时更新内存并原子写回
        └── StateStore    SQLite（WAL），存内容 hash、同步历史、错误明细
```

**关键组件职责**

| 组件 | 所在 | 职责 |
|---|---|---|
| `SyncEngine` | Core | 整轮同步编排：取配置快照 → 逐项目扫描 → upsert → 写状态 |
| `ConfigStore` | Core | 配置内存权威；`ReaderWriterLockSlim` 保护读写；原子写回 `config.json` |
| `StateStore` | Core | SQLite 状态库（WAL + busy_timeout）；记录 hash、sync_run、错误明细 |
| `SiyuanClient` / `RetryingSiyuanClient` | Core | 思源 kernel API 封装；重试包装区分瞬时错误（超时/5xx）与认证错误（401/403） |
| `TimedSyncService` | App | `BackgroundService` + `PeriodicTimer`，周期触发 `SyncEngine.RunAsync` |
| `RunCoordinator` | App | 立即同步入口的并发守卫，`SemaphoreSlim(1,1)` 保证全局至多一轮 |
| `WebConfigHost` 及 Endpoints | App | Kestrel 托管静态页 + REST 端点；认证中间件、CSRF、请求体限制 |

**数据目录**：`%ProgramData%\SiYuan-AI-Expand\`（即 `C:\ProgramData\SiYuan-AI-Expand\`，首次启动自动创建）

| 文件 | 用途 |
|---|---|
| `config.json` | 主配置（思源连接、Web 凭据、项目列表、同步周期） |
| `state.db` | SQLite 状态库（文件 hash、sync_run 历史，WAL 模式） |
| `logs\app-*.log` | Serilog 滚动日志，按日 + 10MB，保留 15 份 |

## 快速开始

### 前置条件

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10/11、Windows Server 2016 及以上（x64） |
| 构建 | .NET 10 SDK（仅构建时需要；运行时无需安装，单文件自包含） |
| 思源笔记 | 一套可访问的思源实例（本机或局域网），并具备管理员 API Token |
| 网络 | 同步主机能访问思源 HTTP 端口（默认 `127.0.0.1:6806`） |
| 权限 | 安装/卸载脚本需管理员权限 |

### 步骤

```powershell
# 1. 发布单文件自包含 exe（普通用户即可）
.\publish.ps1

# 2. 安装（脚本自动 admin 提权；优先用 NSSM 注册服务，本机无 NSSM 时改用计划任务；
#    默认安装目录 E:\Software\FreeInstall\SiyuanExpand，Web 端口 61122，均可回车沿用上次/默认）
.\install.ps1

# 3. 浏览器访问 http://127.0.0.1:61122/ 完成首次配置
```

**首次配置流程（Web 配置页）：**

1. 填写思源 `serverUrl`（如 `http://127.0.0.1:6806`）与 `token`（思源管理员 API Token），点击「测试连接」验证。
2. 设置默认笔记本。
3. 在项目列表新增同步项目：本地 `doc/` 目录、目标笔记本、目标父文档路径（hpath，如 `/JPT`）。
4. 点击项目行的「同步创建父目录」按钮，在思源中初始化父文档（不自动建，需显式触发）。
5. 点击「立即同步」，本地 `.md` 同步为思源文档。

> 控制台调试：在 `publish\` 目录运行 `.\SiYuan-AI-Expand.exe --console`，实时输出 INFO 日志。注意控制台账户需对 `%ProgramData%\SiYuan-AI-Expand\` 具备写权限（详见部署说明）。

## 配置

### config.json 结构（精简示例）

完整字段语义与校验规则见设计文档 `doc/20260806-设计文档-思源笔记doc自动同步工具.md` 第 5 节。

```jsonc
{
  "siyuan": {
    "serverUrl": "http://127.0.0.1:6806",
    "token": "xxxxxx",
    "defaultNotebook": "AI"
  },
  "sync": {
    "intervalMinutes": 10,
    "runOnStart": true
  },
  "web": {
    "port": 61122,
    "bind": "127.0.0.1",
    "password": ""
  },
  "projects": [
    {
      "name": "JPT",
      "enabled": true,
      "docPath": "D:\\work\\JPT\\doc",
      "notebook": "AI",
      "parentPath": "/JPT"
    }
  ]
}
```

- `notebook` 填笔记本**名字**（运行时通过 `lsNotebooks` 解析为 ID）；缺省用 `defaultNotebook`。
- `parentPath` 是思源 hpath 格式，`/` 开头。
- 项目间 `(notebook, parentPath)` 组合必须唯一；不同项目的 `docPath` 不得相同或互为父子目录。

### Web 配置页

浏览器访问 `http://127.0.0.1:61122/`，提供：思源连接（含测试连接）、同步设置（周期/启动即同步）、项目 CRUD、父目录初始化、立即全部同步、同步状态（成功/跳过/失败计数 + 文件级错误明细）。

### 配置变更生效语义

| 变更方式 | 生效时机 |
|---|---|
| Web 配置页修改（推荐） | **即时生效**（token / 笔记本 / 项目 / 周期 / 密码等业务字段，下一轮同步或下一请求采用新值） |
| 直接编辑 `config.json` | 需**重启服务**才重新加载（运行中以内存为权威，不监听文件） |
| `web.bind` / `web.port` | **必须重启**（Kestrel 套接字在启动时绑定） |

Web 修改时 `ConfigStore` 先更新内存权威副本、再原子写回 `config.json`，保证进程重启后状态一致。

## 同步规则

### hpath 映射

本地文件相对路径 → 思源 hpath：`hpath = parentPath + 相对路径（去 .md，分隔符转 /）`

例：`docPath` 为 `D:\work\JPT\doc`，`parentPath=/JPT`，本地 `feat-login\方案.md` → 思源 `/JPT/feat-login/方案`。子目录天然映射为思源子文档树。

### 身份按 hpath，不持久化 docID 映射

每次按 hpath 用 `getIDsByHPath` 查找：未命中→创建；命中→保留 docID 更新正文（取子块→逐个删除→prependBlock 插入新正文）。docID 仅记录展示，不作为身份依据，下一轮始终按 hpath 重新查。

### 增量

按文件正文 SHA256 内容 hash 判断：与 `state.db` 上次记录相同→跳过；不同/无记录→推送。思源操作成功后才写回 hash；状态库写入失败不计成功，下一轮重新校验。

### 首行一级标题剥离

文档标题由 hpath 末段（=本地文件名）设置。为避免与正文首个一级标题重复显示，同步前剥离 md 首个 `# 一级标题` 行（仅当它是首行且为一级标题），其余正文原样同步。

### 只增不删

本地删除的 `.md`：思源文档保留（不调 `removeDocByID`），`state.db` 标记。

## 部署与运维

详见 `doc/部署说明.md`。要点：

- **发布**：`.\publish.ps1` → 产物 `publish\SiYuan-AI-Expand.exe`（单文件自包含，内嵌运行时与 Web 静态资源）。
- **安装/卸载**：`.\install.ps1` / `.\uninstall.ps1`（均自动 admin 提权）。安装时把 `publish\` 产物复制到安装目录（默认 `E:\Software\FreeInstall\SiyuanExpand`，记忆上次），创建 `%ProgramData%\SiYuan-AI-Expand\` 并设 ACL（仅 `Administrators` + `SYSTEM` 完全控制，禁用继承），把 Web 端口写入 `config.json`（默认 `61122`）。优先用 NSSM 注册服务（配 `AppExit Restart` + `sc.exe failure` 双层失败恢复）；本机无 NSSM 时改用计划任务（SYSTEM 身份、开机启动、异常退出 1 分钟后重启）。两种方式都会在 `%ProgramData%\SiYuan-AI-Expand\install-record.json` 记录安装目录/端口/方式，便于重装时自动沿用。卸载同时清理服务与计划任务，保留数据目录与安装目录便于回滚。
- **权限**：服务默认以 `LocalSystem` 运行，自动满足数据目录写权限。改用域/本地账户需手动授予该账户对数据目录的写权限。
- **非 loopback HTTP 风险**：`web.bind=0.0.0.0` 时纯 HTTP 无 TLS，凭据（思源 token、Web 密码、session cookie）明文传输。仅在受信任局域网开放，或通过反向代理提供 TLS 终结。合法 bind 取值：`127.0.0.1`、`localhost`、`0.0.0.0`、`::1`。
- **故障排查**：Windows 事件查看器（来源 `SiYuan-AI-Expand`，Error 级，事件 ID 1=配置损坏 / 2=启动异常）→ `%ProgramData%\SiYuan-AI-Expand\logs\app-*.log` → `--console` 实时输出。

## 开发

### 构建与测试

```bash
dotnet build
dotnet test              # 运行 xUnit 单元测试（Core 层）
dotnet test tests/SiYuanSync.Core.Tests
```

Core 层 81 项单元测试覆盖：首次/增量/删除同步、父目录缺失跳过、思源不可达、认证失败停止、重试分类、取消与停止、配置并发写入与快照隔离、路径安全、token 未配置整轮跳过、项目间冲突拒绝、SQLite 并发等。

### 项目结构

```
SiYuan-AI-Expand.sln
src/
  SiYuanSync.Core/                  # 核心逻辑（可单测，无宿主依赖）
    Models/                         # 配置/项目/状态 数据模型
    Config/                         # ConfigStore / ConfigValidator / ConfigSerializer / TokenMasking
    Siyuan/                         # ISiyuanClient / SiyuanClient / RetryingSiyuanClient
    State/                          # StateStore（SQLite WAL） / StateSchema
    Sync/                           # SyncEngine / ProjectSync / DocUpsert / DocScanner / PathNormalizer / ContentPreprocessor
  SiYuanSync.App/                   # 宿主
    Program.cs                      # --console / 服务 双模式入口
    Hosting/                        # HostBuilder
    Worker/                         # TimedSyncService（BackgroundService）
    Web/                            # WebHostBuilder / Endpoints / RunCoordinator / 认证与 CSRF 中间件
tests/
  SiYuanSync.Core.Tests/            # xUnit 单元测试
```

### 开发方式

- TDD：Core 层先行，`ISiyuanClient` 便于 mock，不依赖真实思源实例。
- 思源集成测试为可选（需本地思源实例，CI 不跑）。
- 本仓库使用 git worktree 工作流：特性分支在 `.claude/worktrees/<branch>` 下开发，完成后合回 main。

## 限制与后续

**当前限制：**

- **仅同步正文文本**：图片/附件资源不同步（md 内嵌图片不上传到思源 assets），后续扩展。
- **单向、本地权威**：不做双向同步、合并或冲突处理；本地是唯一权威源。
- **不自动建笔记本**：笔记本由用户在思源中创建。
- **最低思源版本待实测**：依赖 `prependBlock`、`renameDocByID`、`getIDsByHPath`、`getChildBlocks`、`deleteBlock` 等内核 API，建议从主流稳定版（v3.x）起测，确认后更新部署说明。

**后续可扩展（见设计文档第 15 节与 backlog）：**

- 图片/附件资源同步（`/api/asset/upload` + 链接改写）。
- 文档删除策略可配（镜像删除 / 归档目录）。
- 每项目独立周期、文件监听即时同步。
- 多思源实例 / 多工作空间。
- `RunCoordinator` 当前内部以 `CancellationToken.None` 启动后台同步，stop-token 透传与更细粒度的取消控制为后续改进项。

## 许可证

待定（MIT 或按仓库实际声明为准）。
