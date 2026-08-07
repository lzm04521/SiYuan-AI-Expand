#Requires -Version 5.1
<#
.SYNOPSIS
    安装 SiYuan-AI-Expand 为 Windows 服务（NSSM）或计划任务。
.DESCRIPTION
    优化点：
    - 自动 admin 提权（UAC），非管理员时重启自身。
    - 禁用当前控制台 QuickEdit 模式，避免运行期间鼠标点击/选区暂停脚本。
    - 复制 publish\SiYuan-AI-Expand.exe 到安装目录（默认 E:\Software\FreeInstall\SiyuanExpand，记忆上次）。
    - 将 Web 端口写入 config.json（默认 61122，记忆上次；端口须在服务启动前写入才生效）。
    - 自动检测 NSSM（需在 PATH 中）：有则用 NSSM 注册服务；无则可选计划任务。
    - $ConfirmPreference='None' + 关键操作显式 -Force，避免中途确认弹窗。
.PARAMETER InstallDir
    安装目录。省略则读取上次安装目录或使用默认值并提示确认。
.PARAMETER Port
    Web 配置端口（1-65535）。省略则读取上次端口或使用默认值 61122 并提示确认。
.PARAMETER UseTaskScheduler
    当本机没有 NSSM 时直接采用计划任务（默认会提示）。
.PARAMETER Elevated
    内部参数：标识本次为提权后子进程，结尾暂停窗口以便查看结果。
.EXAMPLE
    .\install.ps1
.EXAMPLE
    .\install.ps1 -InstallDir 'D:\Apps\SiyuanExpand' -Port 61122
#>
param(
    [string]$InstallDir,
    [ValidateRange(1, 65535)][int]$Port,
    [switch]$UseTaskScheduler,
    [switch]$Elevated
)

# --- 常量 ---
$ServiceName    = 'SiYuan-AI-Expand'
$TaskName       = $ServiceName
$DefaultInstall = 'E:\Software\FreeInstall\SiyuanExpand'
$DefaultPort    = 61122
$DataDir        = Join-Path $env:ProgramData $ServiceName
$ConfigPath     = Join-Path $DataDir 'config.json'
$RecordPath     = Join-Path $DataDir 'install-record.json'

# --- 0. 禁用控制台 QuickEdit（避免鼠标点击/选区暂停脚本；失败不阻断） ---
function Disable-ConsoleQuickEdit {
    $src = @'
using System;
using System.Runtime.InteropServices;
public static class QuickEditDisabler {
    const int STD_INPUT_HANDLE = -10;
    const uint ENABLE_QUICK_EDIT = 0x0040;
    const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr h, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleMode(IntPtr h, uint mode);

    public static void Disable() {
        try {
            var h = GetStdHandle(STD_INPUT_HANDLE);
            uint mode;
            if (!GetConsoleMode(h, out mode)) return;
            mode &= ~ENABLE_QUICK_EDIT;
            mode |= ENABLE_EXTENDED_FLAGS;
            SetConsoleMode(h, mode);
        } catch { }
    }
}
'@
    try {
        if (-not ('QuickEditDisabler' -as [type])) {
            Add-Type -TypeDefinition $src -Language CSharp -ErrorAction Stop
        }
        [QuickEditDisabler]::Disable()
    } catch {
        # 禁用失败不阻断安装
    }
}
Disable-ConsoleQuickEdit

# --- 1. 自动 admin 提权 ---
function Test-Admin {
    $p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Test-Admin)) {
    $relaunchArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Elevated')
    if ($InstallDir) { $relaunchArgs += '-InstallDir', $InstallDir }
    if ($PSBoundParameters.ContainsKey('Port')) { $relaunchArgs += '-Port', $Port }
    if ($UseTaskScheduler) { $relaunchArgs += '-UseTaskScheduler' }
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $relaunchArgs
    } catch {
        throw "用户取消 UAC 提权或提权失败：$($_.Exception.Message)"
    }
    exit
}

# 提权窗口下任何未捕获异常先显示再暂停，避免窗口闪退看不到错误
trap {
    Write-Host ''
    Write-Host '安装失败：' -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.InvocationInfo -and $_.InvocationInfo.PositionMessage) {
        Write-Host "  位置：$($_.InvocationInfo.PositionMessage)" -ForegroundColor DarkGray
    }
    if ($Elevated) { Read-Host "`n按回车关闭窗口" }
    exit 1
}

$ErrorActionPreference = 'Stop'
$ConfirmPreference     = 'None'   # 避免 Remove/Copy 等 cmdlet 弹确认
Set-Location -LiteralPath $PSScriptRoot

# --- 2. 定位源产物（先 Test-Path 再用，避免 Stop 模式下抛晦涩错误） ---
$publishDir = Join-Path $PSScriptRoot 'publish'
$srcExe     = Join-Path $publishDir 'SiYuan-AI-Expand.exe'
if (-not (Test-Path -LiteralPath $srcExe)) {
    throw "未找到 $srcExe。请先在同目录运行 publish.ps1 生成产物。"
}

# --- 辅助：读写安装记录（记忆上次安装目录 / 端口 / 方式） ---
function Read-Record {
    if (-not (Test-Path -LiteralPath $RecordPath)) { return $null }
    try { return Get-Content -LiteralPath $RecordPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { return $null }
}
function Write-Record {
    param([string]$Dir, [int]$P, [string]$Mode)
    try {
        New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
        $obj = [pscustomobject]@{
            installDir  = $Dir
            port        = $P
            mode        = $Mode
            installedAt = (Get-Date).ToString('o')
        }
        $json = $obj | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($RecordPath, $json, [System.Text.UTF8Encoding]::new($false))
    } catch {
        Write-Warning "无法写入安装记录 $RecordPath：$($_.Exception.Message)"
    }
}

# --- 3. 检测上次安装目录（安装记录 → 已注册服务 binPath → 默认） ---
function Get-LastInstallDir {
    # a. 安装记录（最可靠：覆盖 NSSM / sc.exe / 计划任务三种方式）
    $rec = Read-Record
    if ($rec -and $rec.installDir -and (Test-Path -LiteralPath $rec.installDir)) {
        return [string]$rec.installDir
    }
    # b. 已注册服务 binPath（仅对 sc.exe 直装的历史服务有效；
    #    NSSM 注册的服务 binPath 指向 nssm.exe，靠文件名匹配过滤掉）
    try {
        $svc = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction Stop
        if ($svc -and $svc.PathName) {
            $m = [regex]::Match($svc.PathName, '"?([^"]*\\SiYuan-AI-Expand\.exe)"?')
            if ($m.Success) {
                $exePath = $m.Groups[1].Value
                if (Test-Path -LiteralPath $exePath) { return (Split-Path -Parent $exePath) }
            }
        }
    } catch { }
    return $null
}

# --- 4. 检测上次端口（config.json → 记录 → 默认） ---
function Get-LastPort {
    if (Test-Path -LiteralPath $ConfigPath) {
        try {
            $cfg = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($cfg.web.port -gt 0) { return [int]$cfg.web.port }
        } catch { }
    }
    $rec = Read-Record
    if ($rec -and $rec.port -gt 0) { return [int]$rec.port }
    return $null
}

# --- 5. 交互确认安装目录 ---
if ([string]::IsNullOrEmpty($InstallDir)) {
    $lastDir = Get-LastInstallDir
    $suggested = if ($lastDir) { $lastDir } else { $DefaultInstall }
    $input = Read-Host "安装目录（回车=$suggested）"
    $InstallDir = if ([string]::IsNullOrWhiteSpace($input)) { $suggested } else { $input.Trim() }
}
$InstallDir = $InstallDir.TrimEnd('\', '/')
$destExe    = Join-Path $InstallDir 'SiYuan-AI-Expand.exe'

# --- 6. 交互确认端口 ---
if (-not $PSBoundParameters.ContainsKey('Port')) {
    $lastPort = Get-LastPort
    $suggestedPort = if ($lastPort) { $lastPort } else { $DefaultPort }
    $input = Read-Host "Web 配置端口（回车=$suggestedPort）"
    if ([string]::IsNullOrWhiteSpace($input)) {
        $Port = $suggestedPort
    } else {
        $parsed = 0
        if (-not [int]::TryParse($input.Trim(), [ref]$parsed) -or $parsed -lt 1 -or $parsed -gt 65535) {
            throw "非法端口：'$input'，须为 1-65535 的整数"
        }
        $Port = $parsed
    }
}

Write-Host ''
Write-Host "==> 安装目录：$InstallDir"
Write-Host "==> Web 端口：$Port"
Write-Host ''

# --- 7. 复制产物到安装目录 ---
if ($destExe -match '\s') {
    Write-Warning "安装路径含空格（$destExe）。NSSM/计划任务可正常处理，但建议部署到无空格路径。"
}
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Write-Host "复制产物到 $InstallDir ..."
Copy-Item -Path (Join-Path $publishDir '*') -Destination $InstallDir -Recurse -Force
if (-not (Test-Path -LiteralPath $destExe)) {
    throw "复制后仍未找到 $destExe"
}

# --- 8. 数据目录 + ACL（仅 Administrators + SYSTEM 完全控制，禁用继承） ---
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
$acl = Get-Acl $DataDir
$acl.SetAccessRuleProtection($true, $false)   # $false=不保留继承条目
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
    'BUILTIN\Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
    'NT AUTHORITY\SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -Path $DataDir -AclObject $acl

# --- 9. 写 config.json 的 web.port（保留已有字段；须在服务启动前写入才生效） ---
# 注意：端口绑定在 Kestrel 启动时完成，服务首次启动前必须把 port 写入 config.json，
# 否则程序 LoadOrInit 会用 C# 默认值 6807 生成配置。
function Set-ConfigPort {
    param([int]$P)
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        # 首次安装：生成默认配置（与 AppConfig 默认值一致，仅 port 用用户值）
        $cfg = [pscustomobject]@{
            siyuan   = [pscustomobject]@{ serverUrl = 'http://127.0.0.1:6806'; token = ''; defaultNotebook = '' }
            sync     = [pscustomobject]@{ intervalMinutes = 10; runOnStart = $true }
            web      = [pscustomobject]@{ port = $P; bind = '127.0.0.1'; password = '' }
            projects = @()
        }
    } else {
        # 已有配置：必须能解析；解析失败则报错，避免覆盖用户配置（与 ConfigStore 拒绝损坏配置一致）
        try {
            $cfg = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch {
            throw "config.json 解析失败，已中止写入端口以免覆盖现有配置。请先修复或备份删除 $ConfigPath。原因：$($_.Exception.Message)"
        }
        if ($null -eq $cfg.web) {
            try { $cfg.PSObject.Properties.Remove('web') } catch { }
            $cfg | Add-Member -NotePropertyName web -NotePropertyValue ([pscustomobject]@{ port = $P; bind = '127.0.0.1'; password = '' })
        } else {
            $cfg.web.port = $P
        }
    }
    $json = $cfg | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($ConfigPath, $json, [System.Text.UTF8Encoding]::new($false))
}
Set-ConfigPort -P $Port

# --- 10. 清理同名旧服务 / 计划任务（便于重装；切换安装方式也覆盖） ---
function Remove-ExistingRun {
    $svc = $null
    try { $svc = Get-Service -Name $ServiceName -ErrorAction Stop } catch { }
    if ($svc) {
        Write-Host "检测到已存在的服务 $ServiceName，先停止并删除..."
        try { sc.exe stop $ServiceName | Out-Null } catch { }
        Start-Sleep -Seconds 2
        try { sc.exe delete $ServiceName | Out-Null } catch { }
        for ($i = 0; $i -lt 20; $i++) {
            $still = $null
            try { $still = Get-Service -Name $ServiceName -ErrorAction Stop } catch { }
            if (-not $still) { break }
            Start-Sleep -Milliseconds 500
        }
    }
    $task = $null
    try { $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop } catch { }
    if ($task) {
        Write-Host "检测到已存在的计划任务 $TaskName，先停止并删除..."
        try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch { }
        try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue } catch { }
    }
}
Remove-ExistingRun

# --- 11. 选择运行方式：NSSM / 计划任务 ---
function Test-Nssm { [bool](Get-Command nssm.exe -ErrorAction SilentlyContinue) }

if (Test-Nssm) {
    $mode = 'nssm'
    Write-Host '检测到 NSSM，使用 NSSM 注册服务。'
} elseif ($UseTaskScheduler) {
    $mode = 'task'
    Write-Host '本机没有 NSSM，按参数采用计划任务。'
} else {
    $ans = Read-Host "本机没有NSSM，可以自行安装NSSM后再试，或采用计划任务，输入Y采用计划任务"
    if ($ans -and $ans.Trim() -imatch '^y') {
        $mode = 'task'
    } else {
        Write-Host '已取消。请将 nssm.exe 加入 PATH 后重试。'
        if ($Elevated) { Read-Host "`n按回车关闭窗口" }
        exit 0
    }
}

# --- 12. 安装并启动 ---
if ($mode -eq 'nssm') {
    Write-Host 'NSSM 安装服务...'
    & nssm.exe install $ServiceName "$destExe" 2>&1 | Out-Null
    & nssm.exe set $ServiceName AppDirectory "$InstallDir" 2>&1 | Out-Null
    & nssm.exe set $ServiceName AppExit Default Restart 2>&1 | Out-Null
    & nssm.exe set $ServiceName AppRestartDelay 5000 2>&1 | Out-Null
    # SCM 层失败恢复兜底（与 NSSM 自身重启互不冲突）
    sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
    sc.exe config $ServiceName start= auto | Out-Null
    $svc = $null
    try { $svc = Get-Service -Name $ServiceName -ErrorAction Stop } catch { }
    if (-not $svc) { throw "NSSM 注册服务失败（未在服务列表中发现 $ServiceName）" }
    sc.exe start $ServiceName | Out-Null
}
elseif ($mode -eq 'task') {
    Write-Host '注册计划任务（SYSTEM 身份，开机启动，异常退出 1 分钟后重启）...'
    # 计划任务以普通后台进程运行，须用 --console 跳过 UseWindowsService；服务/计划任务共用同一数据目录
    $action    = New-ScheduledTaskAction -Execute "$destExe" -Argument '--console' -WorkingDirectory "$InstallDir"
    $trigger   = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $TaskName
    $task = $null
    try { $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop } catch { }
    if (-not $task) { throw "计划任务注册失败：未发现 $TaskName" }
}

# 启动后状态确认（服务模式）
if ($mode -eq 'nssm') {
    Start-Sleep -Seconds 2
    $svc = $null
    try { $svc = Get-Service -Name $ServiceName -ErrorAction Stop } catch { }
    if (-not $svc -or $svc.Status -ne 'Running') {
        Write-Warning "服务当前状态：$($svc.Status)。若未运行，请查事件查看器（来源 SiYuan-AI-Expand）与 $DataDir\logs。"
    }
}

# --- 13. 写安装记录 ---
Write-Record -Dir $InstallDir -P $Port -Mode $mode

# --- 14. 结果输出 ---
Write-Host ''
Write-Host '安装完成。' -ForegroundColor Green
Write-Host "  运行方式：$(if ($mode -eq 'task') {'计划任务'} else {'Windows 服务（NSSM）'}) $ServiceName"
Write-Host "  安装目录：$InstallDir"
Write-Host "  数据目录：$DataDir（Administrators + SYSTEM 完全控制，已禁用继承）"
Write-Host "  Web 配置页：http://127.0.0.1:$Port/（首次未设密码时直接访问）"
if ($mode -eq 'task') {
    Write-Host '  注：计划任务以 --console 模式运行；开机自启；进程异常退出约 1 分钟后由任务计划程序重启。'
}

# --- 提权窗口结尾暂停以便查看结果 ---
if ($Elevated) { Read-Host "`n按回车关闭窗口" }
