#Requires -Version 5.1
<#
.SYNOPSIS
    卸载 SiYuan-AI-Expand（停止并删除服务或计划任务），保留数据目录与安装目录便于回滚。
.DESCRIPTION
    优化点：
    - 自动 admin 提权（UAC）。
    - 禁用当前控制台 QuickEdit 模式，避免运行期间鼠标点击/选区暂停脚本。
    - 同时尝试删除服务与计划任务，兼容历史 NSSM / sc.exe / 计划任务三种装法。
    - $ConfirmPreference='None' + 显式 -Confirm:$false，避免中途确认弹窗。
.PARAMETER Elevated
    内部参数：标识本次为提权后子进程，结尾暂停窗口以便查看结果。
#>
param([switch]$Elevated)

# --- 常量 ---
$ServiceName = 'SiYuan-AI-Expand'
$TaskName    = $ServiceName
$DataDir     = Join-Path $env:ProgramData $ServiceName

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
    } catch { }
}
Disable-ConsoleQuickEdit

# --- 1. 自动 admin 提权 ---
function Test-Admin {
    $p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Test-Admin)) {
    $relaunchArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Elevated')
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
    Write-Host '卸载失败：' -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.InvocationInfo -and $_.InvocationInfo.PositionMessage) {
        Write-Host "  位置：$($_.InvocationInfo.PositionMessage)" -ForegroundColor DarkGray
    }
    if ($Elevated) { Read-Host "`n按回车关闭窗口" }
    exit 1
}

# 卸载逐项清理，单项失败不应中断后续清理
$ErrorActionPreference = 'Continue'
$ConfirmPreference     = 'None'

# --- 2. 删除计划任务（若存在） ---
$task = $null
try { $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop } catch { }
if ($task) {
    Write-Host "停止并删除计划任务 $TaskName ..."
    try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch { }
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
    } catch {
        Write-Warning "删除计划任务失败：$($_.Exception.Message)"
    }
}

# --- 3. 停止并删除服务（NSSM 注册的也是标准 Windows 服务，sc.exe 通用） ---
$svc = $null
try { $svc = Get-Service -Name $ServiceName -ErrorAction Stop } catch { }
if ($svc) {
    Write-Host "停止服务 $ServiceName ..."
    try { sc.exe stop $ServiceName | Out-Null } catch { }
    # 给 SCM 一点时间完成 stop pending（避免 delete 时仍处于停止过渡态）
    Start-Sleep -Seconds 2
    Write-Host "删除服务 $ServiceName ..."
    try { sc.exe delete $ServiceName | Out-Null } catch { }
    # NSSM 兜底（极少情况下 sc.exe delete 留残影）
    if (Get-Command nssm.exe -ErrorAction SilentlyContinue) {
        try { & nssm.exe remove $ServiceName confirm 2>&1 | Out-Null } catch { }
    }
    # 等待 SCM 完成删除
    for ($i = 0; $i -lt 20; $i++) {
        $still = $null
        try { $still = Get-Service -Name $ServiceName -ErrorAction Stop } catch { }
        if (-not $still) { break }
        Start-Sleep -Milliseconds 500
    }
}

# --- 4. 结果输出 ---
Write-Host ''
Write-Host '卸载完成。' -ForegroundColor Green
Write-Host "服务/计划任务 SiYuan-AI-Expand 已停止并删除。"
Write-Host "数据目录 $DataDir 已保留（config.json / state.db / logs / install-record.json）。"
Write-Host "安装目录（exe）同样保留，便于回滚或排查；如需彻底清理请手动删除。"

# --- 提权窗口结尾暂停以便查看结果 ---
if ($Elevated) { Read-Host "`n按回车关闭窗口" }
