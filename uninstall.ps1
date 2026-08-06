# uninstall.ps1 —— 需管理员权限
# 停止并删除 Windows 服务；保留数据目录以便回滚或排查
$ErrorActionPreference = 'SilentlyContinue'

# --- 0. 管理员权限自检（sc.exe stop/delete 需要管理员） ---
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "需以管理员身份运行 uninstall.ps1（sc.exe 操作要求）"
    exit 1
}

# --- 1. 停止服务（若已停止或不存在则忽略错误） ---
$ErrorActionPreference = 'SilentlyContinue'
sc.exe stop SiYuan-AI-Expand

# 给 SCM 一点时间完成 stop pending（避免 delete 时仍处于停止过渡态）
Start-Sleep -Seconds 2

# --- 2. 删除服务 ---
sc.exe delete SiYuan-AI-Expand

Write-Host ""
Write-Host "服务 SiYuan-AI-Expand 已停止并删除。"
Write-Host "数据目录 $env:ProgramData\SiYuan-AI-Expand 已保留（含 config.json / state.db / logs）。"
Write-Host "如需彻底清理，请手动删除该目录。"
