# Task 25 报告：发布脚本、安装/卸载脚本与部署文档

## 状态
完成。4 个交付物全部产出，publish.ps1 实跑通过，install/uninstall 语法解析通过，已提交。

## 交付物

| 文件 | 路径（绝对） | 行数 | 状态 |
|---|---|---|---|
| publish.ps1 | `D:/GitHub/SiYuan-AI-Expand/.claude/worktrees/siyuan-doc-auto-sync/publish.ps1` | 20 | 已提交 |
| install.ps1 | `D:/GitHub/SiYuan-AI-Expand/.claude/worktrees/siyuan-doc-auto-sync/install.ps1` | 46 | 已提交 |
| uninstall.ps1 | `D:/GitHub/SiYuan-AI-Expand/.claude/worktrees/siyuan-doc-auto-sync/uninstall.ps1` | 25 | 已提交 |
| 部署说明.md | `D:/GitHub/SiYuan-AI-Expand/.claude/worktrees/siyuan-doc-auto-sync/doc/部署说明.md` | 204 | 已提交 |

## Commit
- `3e9d9a0 chore: publish, install/uninstall scripts and deployment guide`
- 4 文件，295 行新增
- 分支：`main`（worktree `worktree-siyuan-doc-auto-sync`）

## 验证

### 1. publish.ps1 实跑（关键证据）
```
dotnet publish src/SiYuanSync.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish
```
- 退出码 0
- 产物 `publish\SiYuan-AI-Expand.exe`：**51,728,004 字节（约 49 MB）**，时间戳 2026/8/7 1:37:19
- 同目录另有 `SiYuan-AI-Expand.pdb`、`SiYuanSync.Core.pdb`（调试符号，正常）
- 单文件自包含（含 .NET 运行时 + 原生库 + Web 静态资源 + 压缩）

### 2. install.ps1 / uninstall.ps1 语法解析
通过 `[System.Management.Automation.Language.Parser]::ParseFile` 全文件解析：
```
[publish.ps1]  OK (syntax parsed cleanly)
[install.ps1]  OK (syntax parsed cleanly)
[uninstall.ps1] OK (syntax parsed cleanly)
```

### 3. 编码处理（重要发现）
首次 Write 产出的 ps1 文件为 **UTF-8 无 BOM**，Windows PowerShell 5.1（`powershell.exe`）在中文 Windows 上按系统 ANSI（GBK）解码，导致中文字符错乱 → 解析器把中文当作破坏字符串终止符的字符，全部脚本 Parse FAIL。
**已修复**：用 `[System.Text.UTF8Encoding]($true)` 重新写回，三个 ps1 现均为 UTF-8 with BOM，PowerShell 5.1 与 7 均可正确解析。
**后续注意**：本项目所有含非 ASCII 字符的 .ps1 文件应统一保存为 UTF-8 with BOM。

### 4. install/uninstall 实际安装/卸载服务
- **未执行**：需管理员权限，本环境不便安全提权。按任务约束，仅做语法 + 内容审阅，不实际装服务。

### 5. doc 覆盖检查（brief 第 14 节要点 → 章节）

| brief 要点 | 文档章节 | 覆盖 |
|---|---|---|
| 前置（self-contained 无需 .NET、思源实例可访问、admin token） | §1 前置条件 | ✓ |
| 发布（./publish.ps1） | §2 发布 | ✓ |
| 安装/卸载 | §3 安装、§4 卸载 | ✓ |
| 首次配置（--console → 127.0.0.1:6807 → token/默认笔记本/项目 → 父目录 → 立即同步） | §5 首次配置 | ✓ |
| 数据目录与权限（LocalSystem 写 ProgramData；换账户手动授权） | §6 数据目录与权限 | ✓ |
| 配置变更生效（Web 即时；手改 config.json 重启；bind/port 重启） | §7 配置变更生效语义 | ✓ |
| 非 loopback 绑定 HTTP 凭据风险 | §8 非 loopback 绑定的安全风险 | ✓ |
| 最低思源版本（实测后填） | §9 最低思源版本（待实测填写） | ✓ |
| 故障排查（事件查看器→SiYuan-AI-Expand；--console；logs/app-*.log） | §10 故障排查 | ✓ |

## gitignore 与提交情况
`.gitignore` 实际内容：
```
bin/  obj/  TestResults/  publish/  *.log
```
- `publish/` **已忽略**（正确，发布产物不进版本库）→ publish.exe 未提交
- `doc/` **未忽略**（与任务描述里"doc/ is gitignored"不符，实际未被忽略）→ `doc/部署说明.md` **正常入库**
- 4 个交付物（3 个 ps1 + 1 个 md）全部成功 git add + commit

## 关键实现选择

### publish.ps1
- brief 命令一字不改；额外加 `Set-Location $PSScriptRoot`（保证相对路径稳定）与产物存在性 `Test-Path` 检查（早失败优于晚失败）。
- 不修改 dotnet 命令本身。

### install.ps1
- brief 流程全保留：`New-Item` → `Get-Acl/SetAccessRuleProtection($true,$false)` → 两条 `AddAccessRule`（Administrators/SYSTEM，FullControl，ContainerInherit+ObjectInherit）→ `Set-Acl` → `sc.exe create binPath= "..." start= auto` → `sc.exe failure reset= 86400 actions= restart/5000/restart/10000/restart/30000` → `sc.exe start`。
- 额外加管理员权限自检（避免运行到一半才在 sc.exe 处失败留下空目录）。
- 额外加 publish exe 存在性检查（给出友好错误，否则 Resolve-Path 抛出的错误晦涩）。

### uninstall.ps1
- brief 流程全保留：`$ErrorActionPreference='SilentlyContinue'` → `sc.exe stop` → `sc.exe delete` → 提示数据目录保留。
- 额外加管理员自检与 stop 后 2s 等待（避免 delete 时仍在 stop-pending 过渡态导致 sc.exe 报错）。

### doc/部署说明.md
- 11 节，结构化表格 + 速查命令块；§11 为端到端首次部署速查（publish → install → Web 配置 → console 调试 → uninstall）。

## 关注点与遗留风险

1. **install/uninstall 未做服务级端到端实测**：按约束未提权安装服务。语法与逻辑审阅无误，但 `sc.exe create` 实际行为、服务启动、LocalSystem 写 ProgramData、失败重启策略等需管理员环境实际验证（任务 Step 5 列为"端到端手动验证"，需人工执行）。
2. **`binPath=` 路径含空格的潜在问题**：脚本用 `binPath= "$exe"`，`Resolve-Path` 返回完整路径。若用户把仓库放在含空格的路径下（如 `C:\Program Files\xxx`），sc.exe 的 binPath 解析有历史怪癖。建议部署到无空格路径；本任务用例（`C:\ProgramData\...` 子目录或仓库目录）通常无空格。
3. **PowerShell 解析的 BOM 依赖**：本任务提交的 3 个 ps1 已是 UTF-8 with BOM。后续若有人编辑并保存为无 BOM，PowerShell 5.1 会再次解析失败。可在 CI 加 ps1 BOM/编码校验（非本任务范围）。
4. **dotnet 不在 fresh PowerShell PATH**：本机测试时需手动 `C:\Program Files\dotnet;` 前置 PATH。生产用户从安装了 .NET SDK 的环境运行 publish.ps1 时 dotnet 默认在 PATH，无需额外处理；如需更鲁棒，可在 publish.ps1 加 dotnet 路径探测（未加，避免偏离 brief 命令）。
5. **最低思源版本仍为占位**：brief 明确要求"实测后填写"，部署文档 §9 已说明依赖的具体内核 API 并标注待实测，未留代码级 TODO。
6. **doc/ 可入库**：与任务描述里"doc/ is gitignored"不符，实际 .gitignore 未含 doc/，部署说明.md 已正常提交。无需特别处理。

## 总结
4 个交付物全部产出并通过可达成范围内的验证（publish 实跑 + 产物存在；install/uninstall 语法解析；文档 brief 要点全覆盖）。1 个 commit 入库。剩余端到端服务级验证（需管理员）由人工 Step 5 完成。

---

## Review 修复（fix(deploy): admin-check visibility, path-check order, space-path warning）

### 背景
Task 25 review 发现 3 个 Important + 若干 Minor 问题，均集中在 install.ps1 / uninstall.ps1 的健壮性。

### 修复内容

#### Important 1：uninstall.ps1 admin-check 不可见
- **问题**：`$ErrorActionPreference = 'SilentlyContinue'` 在 admin-check 之前设置，非管理员运行时 `Write-Error` 被静默吞掉，用户无任何输出。
- **修复**：删除脚本头部的 `$ErrorActionPreference = 'SilentlyContinue'`，仅保留 admin-check 之后（sc.exe stop 之前）的那一行 → admin-check 在默认 ErrorActionPreference=Continue 下正常显示。

#### Important 2：install.ps1 Test-Path 不可达
- **问题**：`Resolve-Path` 在 `Test-Path` 之前执行；ErrorActionPreference=Stop 模式下若产物不存在，Resolve-Path 直接抛晦涩错误，友好 throw 不可达。
- **修复**：先 `Test-Path $exeCandidate` → 不存在则 throw 友好消息 → 存在再 `Resolve-Path`。

#### Important 3：install.ps1 路径含空格时 sc.exe binPath 静默失败
- **问题**：`sc.exe create ... binPath= "$exe"` 在 `$exe` 含空格时可能解析失败。
- **修复**：sc.exe create 之前加 `if ($exe -match '\s') { Write-Warning "安装路径含空格（$exe），sc.exe binPath 可能解析失败，建议部署到无空格路径。" }`；doc 中已有相关提示保持不变。

#### Minor：重复行 & ProgramData 插值
- uninstall.ps1：删除头部重复的 `$ErrorActionPreference` 行（保留 admin-check 后那一行）。
- uninstall.ps1：`$env:ProgramData\SiYuan-AI-Expand` → `${env:ProgramData}\SiYuan-AI-Expand`（defensive interpolation，避免 `\` 紧跟变量名的歧义）。
- install.ps1：`Join-Path $env:ProgramData` → `Join-Path ${env:ProgramData}`（同样 defensive）。

### 编码与解析验证
- 三个 ps1 均保持 **UTF-8 with BOM**（239,187,191），Edit 未丢失 BOM。
- PowerShell Parser 解析全部通过：`[uninstall.ps1] OK (0 errors)` / `[install.ps1] OK (0 errors)` / `[publish.ps1] OK (0 errors)`。

### 涉及文件
| 文件 | 改动 |
|---|---|
| `uninstall.ps1` | 删 1 行（早期 SilentlyContinue）；ProgramData 插值改为 `${env:ProgramData}` |
| `install.ps1` | Test-Path/Resolve-Path 顺序调整；加空格路径 Write-Warning；ProgramData 插值改为 `${env:ProgramData}` |
| `publish.ps1` | 无改动（本次未涉及） |
