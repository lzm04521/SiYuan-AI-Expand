# SiYuan-AI-Expand

## 项目简介

SiYuan-AI-Expand 是一个 Windows **托盘程序**，把多个项目的 `doc/` 目录下的 Markdown 与 HTML 报告文档**单向、增量**同步到思源笔记，形成统一的 AI 知识库。

起因：使用 Claude Code 处理项目问题时，方案/诊断文档（`.md`）会生成到各项目的 `doc/` 目录，分散在各仓库里查阅与归档不便（含 AI 工具导出的 `.html`/`.htm` 报告，本地转 Markdown 后同步）。本工具按配置周期扫描这些目录，保留子目录结构映射为思源文档树，内容变化（SHA256）才推送，未变不重发。

程序以**普通用户**身份运行（无需管理员权限），常驻系统托盘；双击托盘图标或菜单"打开管理页"用默认浏览器打开 Web 配置页。

## 功能特性

- **托盘常驻**：托盘菜单提供 打开管理页 / 立即同步 / 退出。
- **系统设置（Web）**：开机自启开关 + 关于（版本、仓库地址、检查更新）统一在 Web 管理页「系统」Tab。
- **开机自启**：写注册表 `HKCU\...\Run`，免管理员、免 UAC。
- **自动升级**：检查 GitHub Releases 新版本，下载并替换整个安装目录（含主 exe + 升级程序 + 内嵌 Web 资源），自动重启。
- **多项目配置**：每个项目独立配置名称、本地 `doc/` 目录、目标笔记本、目标父文档路径（hpath）。
- **周期增量同步**：默认每 10 分钟扫描，按文件正文 SHA256 内容 hash 判断变更，未变跳过。
- **HTML 报告同步**：`.html`/`.htm`（AI 工具导出的报告）自动转 Markdown 后同步；剥除 script/样式/注释与包裹标签，保留标题/段落/表格/列表/代码块等语义结构。
- **同步日志**：Web 管理页「日志」Tab 浏览历史各轮同步记录，支持按项目与日期筛选，点开轮次查看文件级明细（新建 / 更新 / 跳过 / 失败）。
- **单向、只增不删**：本地新增→建文档；内容更新→覆盖同名文档正文；本地删除→思源文档保留。
- **保留子目录结构**：本地子目录天然映射为思源子文档树。
- **Web 配置页**：思源连接、项目 CRUD、父目录初始化、立即同步、同步状态一览，全部浏览器操作。
- **双运行模式**：托盘模式（默认，双击 exe）与 `--console` 交互调试。
- **安全**：默认 loopback 免认证；非 loopback 绑定强制密码 + session；修改请求校验 CSRF；请求体大小限制；敏感字段脱敏。
- **配置 Web 即时生效**：通过 Web 配置页修改的业务字段（token、项目、周期等）下一轮同步即采用新值，无需重启。

## 架构

分 **Core**（纯逻辑、无宿主依赖、可独立单测）与 **App**（宿主：托盘 + Worker + Web）+ **Updater**（独立升级程序）三层。

```
SiYuan-AI-Expand.exe（单文件自包含，托盘主程序）
├── NotifyIcon 托盘 + 菜单（打开管理页 / 立即同步 / 退出）
├── TimedSyncService  (BackgroundService)  PeriodicTimer + SemaphoreSlim 防重入
├── WebConfigHost     (Kestrel)            静态页 + REST 端点，复用 SyncEngine 提供"立即同步"
└── 共享 SyncEngine / ConfigStore / StateStore
        ├── SyncEngine       纯逻辑核心，无状态；输入项目配置 → 扫描与 upsert
        ├── SiyuanClient     封装思源 kernel HTTP API（Token 认证 + 重试包装）
        ├── ConfigStore      进程内配置为运行权威，启动时加载 config.json，Web 保存时更新内存并原子写回
        ├── StateStore       SQLite（WAL），存内容 hash、同步历史、错误明细
        ├── UpdateChecker    查 GitHub Releases + 版本比对 + 资产下载
        └── AutostartService 读写 HKCU\...\Run 注册开机自启

SiYuan-AI-Expand-Updater.exe（独立升级程序，由主程序在升级时拉起）
  等主进程退出 → 解压升级包 → 覆盖安装目录（跳过运行中的 Updater.exe 自身）→ 重启主程序
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
| `TrayApp` | App | NotifyIcon + 右键菜单；双击打开 Web 管理页；菜单触发立即同步 / 退出 |
| `UpdateChecker` | App | 查询 GitHub Releases，语义版本比对，下载升级包 |
| `AutostartService` | App | 读写注册表开机自启项 |
| `Updater` | Updater | 独立升级程序：等待主进程退出 → 解压覆盖 → 重启 |

**数据目录**：`%LocalAppData%\SiYuan-AI-Expand\`（即 `C:\Users\<用户>\AppData\Local\SiYuan-AI-Expand\`，首次启动自动创建）

| 文件 | 用途 |
|---|---|
| `config.json` | 主配置（思源连接、Web 凭据、项目列表、同步周期） |
| `state.db` | SQLite 状态库（文件 hash、sync_run 历史，WAL 模式） |
| `logs\app-*.log` | Serilog 滚动日志，按日 + 10MB，保留 15 份 |
| `update\` | 升级包临时下载目录 |

## 快速开始

### 前置条件

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10/11、Windows Server 2016 及以上（x64） |
| 构建 | .NET 10 SDK（仅构建时需要；运行时无需安装，单文件自包含） |
| 思源笔记 | 一套可访问的思源实例（本机或局域网），并具备管理员 API Token |
| 网络 | 同步主机能访问思源 HTTP 端口（默认 `127.0.0.1:6806`） |

### 步骤

```powershell
# 1. 发布（产物在 publish\）
.\publish.ps1

# 2. 把 publish\ 下两个 exe 放到任意安装目录（如 D:\Apps\SiyuanExpand\）
#    SiYuan-AI-Expand.exe + SiYuan-AI-Expand-Updater.exe（必须在同一目录）

# 3. 双击 SiYuan-AI-Expand.exe，系统托盘出现图标

# 4. 托盘菜单 → 打开管理页，浏览器访问 http://127.0.0.1:61122/ 完成首次配置

# 5. （可选）管理页 → 系统 Tab → 勾选"开机自启"
```

**首次配置流程（Web 配置页）：**

1. 填写思源 `serverUrl`（如 `http://127.0.0.1:6806`）与 `token`（思源管理员 API Token），点击「测试连接」验证。
2. 设置默认笔记本。
3. 在项目列表新增同步项目：本地 `doc/` 目录、目标笔记本、目标父文档路径（hpath，如 `/JPT`）。
4. 点击项目行的「同步创建父目录」按钮，在思源中初始化父文档（不自动建，需显式触发）。
5. 点击「立即同步」，本地 `.md` 同步为思源文档。

> 控制台调试：在安装目录运行 `.\SiYuan-AI-Expand.exe --console`，实时输出 INFO 日志。

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
      "parentPath": "/JPT",
      "sortMode": 3
    }
  ]
}
```

- `notebook` 填笔记本**名字**（运行时通过 `lsNotebooks` 解析为 ID）；缺省用 `defaultNotebook`。
- `parentPath` 是思源 hpath 格式，`/` 开头。
- `sortMode` 可选：同步完成后对思源父文档设置子文档排序方式（等价于在思源中右键父文档 → 排序）。`3` = 更新时间降序（最近同步的排最前），`10` = 创建时间降序，`2/9` = 对应升序，`4/5` = 文件名升/降序，`6` = 自定义；留空/缺省 = 不调整。需思源 ≥ v3.8.1，低版本仅记录告警不影响同步。
- 多个项目可指向同一 `(notebook, parentPath)`（同步到思源同一父文档下共存）；此时各项目若有同名相对路径文件，会映射到同一思源 hpath 互相覆盖，需自行规避。不同项目的 `docPath` 不得相同或互为父子目录。

### Web 配置页

浏览器访问 `http://127.0.0.1:61122/`，提供：思源连接（含测试连接）、同步设置（周期/启动即同步）、项目 CRUD、父目录初始化、立即全部同步、同步状态（成功/跳过/失败计数 + 文件级错误明细）、同步日志（历史轮次 + 筛选 + 明细）。

### 配置变更生效语义

| 变更方式 | 生效时机 |
|---|---|
| Web 配置页修改（推荐） | **即时生效**（token / 笔记本 / 项目 / 周期 / 密码等业务字段，下一轮同步或下一请求采用新值） |
| 直接编辑 `config.json` | 需**重启程序**才重新加载（运行中以内存为权威，不监听文件） |
| `web.bind` / `web.port` | **必须重启**（Kestrel 套接字在启动时绑定） |

Web 修改时 `ConfigStore` 先更新内存权威副本、再原子写回 `config.json`，保证进程重启后状态一致。

### 开机自启

Web 管理页 → 系统 Tab → "开机自启"勾选，写入注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名为 `SiYuan-AI-Expand`。以当前用户身份自启，登录后即拉起，无需管理员权限。

## 升级

程序内置从 GitHub Releases 检查并应用升级的能力（仓库 `https://github.com/lzm04521/SiYuan-AI-Expand`）：

1. Web 管理页 → 系统 Tab → 检查更新，查询 `releases/latest`，与本地版本（`Directory.Build.props` 的 `<Version>`）做语义版本比对。
2. 发现新版本 → 确认下载升级包到 `%LocalAppData%\SiYuan-AI-Expand\update\` → 主程序退出 → `SiYuan-AI-Expand-Updater.exe` 解压覆盖安装目录 → 重启主程序。
3. 升级包资产名约定 `SiYuan-AI-Expand-<version>-win-x64.zip`（含两个 exe，无目录前缀，`<version>` 来自 `Directory.Build.props`）；`UpdateChecker` 兼容旧固定名 `SiYuan-AI-Expand-win-x64.zip`。GitHub tag 用 `vX.Y.Z`。
4. 跳过说明：升级时 `Updater.exe` 自身正在运行无法覆盖，会被跳过（其 apply 逻辑稳定，几乎不需要更新）。

发布新版本流程：

```powershell
.\publish.ps1                                  # 产出 publish\SiYuan-AI-Expand-<version>-win-x64.zip
git tag v0.2.1
git push origin v0.2.1
# push tag 后 GitHub Actions（.github/workflows/release.yml）自动构建 win-x64 产物并创建 Release
```

发布后，已部署的旧版本在 Web 管理页 → 系统 Tab → 检查更新 即可发现并升级。

## 同步规则

### hpath 映射

本地文件相对路径 → 思源 hpath：`hpath = parentPath + 相对路径（去 .md，分隔符转 /）`

例：`docPath` 为 `D:\work\JPT\doc`，`parentPath=/JPT`，本地 `feat-login\方案.md` → 思源 `/JPT/feat-login/方案`。子目录天然映射为思源子文档树。`.html`/`.htm` 同样剥后缀映射（`报告.html` → `/JPT/报告`）；同目录 `foo.md` 与 `foo.html` 会映射同一 hpath，按冲突报错跳过。

### 身份按 hpath，不持久化 docID 映射

每次按 hpath 用 `getIDsByHPath` 查找：未命中→创建；命中→保留 docID 更新正文（取子块→逐个删除→prependBlock 插入新正文）。docID 仅记录展示，不作为身份依据，下一轮始终按 hpath 重新查。

### 增量

按文件正文 SHA256 内容 hash 判断：与 `state.db` 上次记录相同→跳过；不同/无记录→推送。思源操作成功后才写回 hash；状态库写入失败不计成功，下一轮重新校验。

### 首行一级标题剥离

文档标题由 hpath 末段（=本地文件名）设置。为避免与正文首个一级标题重复显示，同步前剥离 md 首个 `# 一级标题` 行（仅当它是首行且为一级标题），其余正文原样同步。

### 只增不删

本地删除的 `.md`：思源文档保留（不调 `removeDocByID`），`state.db` 标记。

## 部署与运维

- **发布**：`.\publish.ps1` → 产物 `publish\`（主 exe + Updater exe + 升级包 zip，单文件自包含，内嵌运行时与 Web 静态资源）。
- **安装**：把两个 exe 放到任意目录，双击主 exe 即可；无需管理员权限。
- **卸载**：托盘退出 → 取消设置里的开机自启 → 删除安装目录与 `%LocalAppData%\SiYuan-AI-Expand\`。

### 从旧版（服务模式）迁移

0.2.0 起改为托盘程序，不再使用 Windows 服务 / 计划任务。若此前用 `install.ps1` 装过服务，需先卸载（管理员 PowerShell）：

```powershell
sc.exe stop SiYuan-AI-Expand
sc.exe delete SiYuan-AI-Expand
# NSSM 注册的服务同样用 sc.exe delete；计划任务则：Unregister-ScheduledTask -TaskName SiYuan-AI-Expand -Confirm:$false
```

旧数据目录在 `%ProgramData%\SiYuan-AI-Expand\`（config.json / state.db）。如需保留原配置，手动复制到 `%LocalAppData%\SiYuan-AI-Expand\`；否则新托盘程序首次启动时会按默认值重新生成。
- **非 loopback HTTP 风险**：`web.bind=0.0.0.0` 时纯 HTTP 无 TLS，凭据明文传输。仅在受信任局域网开放，或通过反向代理提供 TLS 终结。合法 bind 取值：`127.0.0.1`、`localhost`、`0.0.0.0`、`::1`。
- **故障排查**：Windows 事件查看器（来源 `SiYuan-AI-Expand`，Error 级，事件 ID 1=配置损坏 / 2=启动异常）→ `%LocalAppData%\SiYuan-AI-Expand\logs\app-*.log` → `--console` 实时输出。

## 开发

### 构建与测试

```bash
dotnet build
dotnet test              # 运行 xUnit 单元测试（Core 层，138 项）
dotnet test tests/SiYuanSync.Core.Tests
```

Core 层 138 项单元测试覆盖：首次/增量/删除同步、父目录缺失跳过、思源不可达、认证失败停止、重试分类、取消与停止、配置并发写入与快照隔离、路径安全、token 未配置整轮跳过、项目间冲突拒绝、SQLite 并发、数据目录与派生路径、默认配置等。

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
    Paths/                          # AppPaths（数据目录 LocalAppData）
  SiYuanSync.App/                   # 托盘宿主
    Program.cs                      # 托盘 / --console 双模式入口（[STAThread]）
    Hosting/                        # HostBuilder
    Worker/                         # TimedSyncService（BackgroundService）
    Web/                            # WebHostBuilder / Endpoints / RunCoordinator / 认证与 CSRF 中间件
    Tray/TrayApp.cs                 # NotifyIcon 托盘 + 菜单
    Autostart/AutostartService.cs   # 注册表开机自启
    Update/UpdateChecker.cs         # GitHub Releases 检查 + 版本比对 + 下载
  SiYuanSync.Updater/               # 独立升级程序（--apply --pid --dir --zip）
tests/
  SiYuanSync.Core.Tests/            # xUnit 单元测试
```

### 开发方式

- TDD：Core 层先行，`ISiyuanClient` 便于 mock，不依赖真实思源实例。
- 思源集成测试为可选（需本地思源实例，CI 不跑）。
- 本仓库使用 git worktree 工作流：特性分支在 `.claude/worktrees/<branch>` 下开发，完成后合回 main。

## 限制与后续

**当前限制：**

- **仅同步正文文本**：图片/附件资源不同步（md/HTML 内嵌图片均不上传到思源 assets），后续扩展。
- **HTML 转换保真度依赖 ReverseMarkdown**：复杂表格（colspan/rowspan 等跨行列合并）、深度嵌套结构可能降级；CSS 样式与布局不保留；`<meta charset>` 非 UTF-8 的页面按 UTF-8 读取会乱码。
- **单向、本地权威**：不做双向同步、合并或冲突处理；本地是唯一权威源。
- **不自动建笔记本**：笔记本由用户在思源中创建。
- **升级跳过 Updater.exe 自身**：运行中的 exe 文件锁无法覆盖，由下次主程序启动后处理（apply 逻辑稳定，基本无需更新）。
- **仅 Windows**：WinForms 托盘 + Web 管理页为 Windows 专用。
- **最低思源版本待实测**：依赖 `prependBlock`、`renameDocByID`、`getIDsByHPath`、`getChildBlocks`、`deleteBlock` 等内核 API。

**后续可扩展：**

- 图片/附件资源同步（`/api/asset/upload` + 链接改写）。
- 文档删除策略可配（镜像删除 / 归档目录）。
- 每项目独立周期、文件监听即时同步。
- 升级进度条与 SHA256 校验。
- 自定义托盘图标（当前用系统图标）。

## 许可证

待定（MIT 或按仓库实际声明为准）。
