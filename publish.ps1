# publish.ps1 —— 在 PowerShell 执行
# 产物（publish\）：
#   SiYuan-AI-Expand.exe                    单文件自包含，托盘主程序（含 Web 配置页 + 内嵌静态资源）
#   SiYuan-AI-Expand-Updater.exe            单文件自包含，升级程序（由主程序自动调用，无需手动运行）
#   SiYuan-AI-Expand-<version>-win-x64.zip  升级用压缩包（含两个 exe；文件名带版本号便于辨认）
# -Version 可选：CI 由 tag 注入；未传时回退读 Directory.Build.props（本地手动发布）
param([string]$Version)

$ErrorActionPreference = 'Stop'

# 切到脚本所在目录，保证 src 与 publish 输出路径相对解析稳定
Set-Location -LiteralPath $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "未找到 dotnet，请确保已安装 .NET 10 SDK 并加入 PATH。"
}

# 版本号：注入到程序集（-p:Version）与升级包命名，保证产物版本与 tag 一致
$props = Join-Path $PSScriptRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $props)) { throw "未找到 Directory.Build.props：$props" }
if (-not $Version) {
    $propsXml = [xml](Get-Content -LiteralPath $props -Raw)
    $Version = $propsXml.Project.PropertyGroup.Version
    if (-not $Version) { throw "未能从 Directory.Build.props 解析 <Version>。" }
}
$Version = $Version.Trim().TrimStart('v', 'V')
Write-Host "==> 版本号：$Version"

function Publish-Project {
    param([string]$Project, [string]$ExeName)
    Write-Host "==> 发布 $Project ..."
    dotnet publish $Project -c Release -r win-x64 --self-contained `
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
      -p:EnableCompressionInSingleFile=true -p:Version=$Version -o publish
    $exe = Join-Path $PSScriptRoot "publish\$ExeName"
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "未找到预期产物：$exe（dotnet publish 可能未成功）"
    }
}

Publish-Project -Project 'src/SiYuanSync.App'     -ExeName 'SiYuan-AI-Expand.exe'
Publish-Project -Project 'src/SiYuanSync.Updater' -ExeName 'SiYuan-AI-Expand-Updater.exe'

# 清理调试符号（生产发布不需要）
Get-ChildItem -Path 'publish' -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

# 打包升级用 zip（解压后为两个 exe，无目录前缀，便于 Updater 覆盖安装目录）
# 资产名带版本号：SiYuan-AI-Expand-<version>-win-x64.zip
# UpdateChecker 同时兼容此命名与旧固定名 SiYuan-AI-Expand-win-x64.zip
$zipName = "SiYuan-AI-Expand-$Version-win-x64.zip"
$zip = Join-Path $PSScriptRoot "publish\$zipName"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
$exes = Get-ChildItem -Path 'publish' -Filter '*.exe' -File | Select-Object -ExpandProperty FullName
if (-not $exes -or @($exes).Count -lt 2) {
    throw "publish 目录下未找到两个 exe（预期 SiYuan-AI-Expand.exe + SiYuan-AI-Expand-Updater.exe）"
}
Compress-Archive -Path $exes -DestinationPath $zip

Write-Host ""
Write-Host "产物（publish\）："
Write-Host "  SiYuan-AI-Expand.exe                    托盘主程序（双击运行；Web 管理页 → 系统 Tab → 开机自启）"
Write-Host "  SiYuan-AI-Expand-Updater.exe            升级程序（由主程序自动调用，无需手动运行）"
Write-Host "  $zipName  升级包（带版本号，上传 GitHub Release 时用此文件）"
Write-Host ""
Write-Host "日常使用：双击 SiYuan-AI-Expand.exe → 托盘图标 → 打开管理页（Web）配置与开机自启"
Write-Host "发布新版本：git tag vX.Y.Z + push（CI 自动以 tag 版本构建并发布 Release，无需手改 Directory.Build.props）"
