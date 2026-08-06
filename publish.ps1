# publish.ps1 —— 在 PowerShell 执行
# 产物：publish\SiYuan-AI-Expand.exe（单文件、自包含、win-x64）
$ErrorActionPreference = 'Stop'

# 切到脚本所在目录，保证 src/SiYuanSync.App 与 publish 输出路径相对解析稳定
Set-Location -LiteralPath $PSScriptRoot

# 单文件自包含发布：内嵌原生库、启用压缩，输出到 ./publish
dotnet publish src/SiYuanSync.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish

$exe = Join-Path $PSScriptRoot 'publish\SiYuan-AI-Expand.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "未找到预期产物：$exe（dotnet publish 可能未成功）"
}

Write-Host ""
Write-Host "产物：publish\SiYuan-AI-Expand.exe"
Write-Host "下一步：以管理员身份运行 install.ps1 安装服务。"
