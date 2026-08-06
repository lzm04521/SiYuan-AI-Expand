# install.ps1 —— 需管理员权限运行
# 安装 SiYuan-AI-Expand 为 Windows 服务（自动启动 + 失败自动重启）
# 前置：已在本目录运行 publish.ps1，产物位于 .\publish\SiYuan-AI-Expand.exe
$ErrorActionPreference = 'Stop'

# --- 0. 管理员权限自检（避免半途中途失败导致残留） ---
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "需以管理员身份运行 install.ps1（sc.exe 与 ACL 操作要求）"
}

# --- 1. 解析产物路径（先 Test-Path 再 Resolve-Path，避免 Stop 模式下 Resolve-Path 抛晦涩错误） ---
$exeCandidate = "$PSScriptRoot\publish\SiYuan-AI-Expand.exe"
if (-not (Test-Path -LiteralPath $exeCandidate)) {
    throw "未找到 .\publish\SiYuan-AI-Expand.exe，请先在同目录运行 publish.ps1"
}
$exe = Resolve-Path $exeCandidate

# --- 2. 创建数据目录并设 ACL（仅 Administrators + SYSTEM 完全控制，禁用继承） ---
$dataDir = Join-Path ${env:ProgramData} 'SiYuan-AI-Expand'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

$acl = Get-Acl $dataDir
# 禁用继承并移除继承的 ACE（$false 表示不保留继承条目）
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
    'BUILTIN\Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
    'NT AUTHORITY\SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -Path $dataDir -AclObject $acl

# --- 3. 创建服务（自动启动） ---
# sc.exe 语法：每个参数后的等号必须带一个空格，例如 "binPath= <value>"
# sc.exe binPath 对含空格路径解析有历史怪癖，提前告警
if ($exe -match '\s') {
    Write-Warning "安装路径含空格（$exe），sc.exe binPath 可能解析失败，建议部署到无空格路径。"
}
sc.exe create SiYuan-AI-Expand binPath= "$exe" start= auto | Out-Null

# --- 4. 配置失败恢复策略：1 天内重启计数归零；首次失败等 5s 重启，第二次 10s，第三次起 30s ---
sc.exe failure SiYuan-AI-Expand reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

# --- 5. 启动服务 ---
sc.exe start SiYuan-AI-Expand | Out-Null

Write-Host ""
Write-Host "服务 SiYuan-AI-Expand 已安装并启动。"
Write-Host "数据目录：$dataDir（Administrators + SYSTEM 完全控制，已禁用继承）"
Write-Host "首次配置："
Write-Host "  - Web 配置页：http://127.0.0.1:6807/  （首次未设密码时直接访问；登录后可设密码）"
Write-Host "  - 或本地调试：在 publish 目录运行 SiYuan-AI-Expand.exe --console"
