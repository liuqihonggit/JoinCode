# Deploy-JccDev.ps1 — 全量编译 + 部署 jcc 到 jcc.d/dev
# 用途: 全量编译所有域后，拷贝主项目 + gh 插件 + rg 插件的所有产物（exe/dll/deps.json/runtimeconfig）到 jcc.d/dev/
#       三个 exe 共享同一套依赖 dll，部署到同一目录确保引用一致
# 用法: pwsh non_deliverables_tools/scripts/Deploy-JccDev.ps1 [-SkipBuild] [-Configuration Debug]
# 退出码: 0=成功; 1=编译或部署失败
# ADR: 0132
param(
    [switch]$SkipBuild,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$deployDir = "C:/Users/54076/bin/jcc.d/dev"
$mainOutDir = "$repoRoot/artifacts/bin/JoinCode/$Configuration/net10.0"
$ghOutDir = "$repoRoot/artifacts/bin/gh/$Configuration/net10.0"
$rgOutDir = "$repoRoot/artifacts/bin/rg/$Configuration/net10.0"

if (-not $SkipBuild) {
    Write-Host "==> 全量编译..." -ForegroundColor Cyan
    & "$repoRoot/non_deliverables_tools/scripts/Build-AllDomains.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Host "编译失败" -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $mainOutDir)) {
    Write-Host "主项目输出目录不存在: $mainOutDir" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $deployDir)) {
    New-Item -ItemType Directory -Force -Path $deployDir | Out-Null
}

Write-Host "==> 拷贝主项目产物 ($mainOutDir -> $deployDir)..." -ForegroundColor Cyan
$mainFiles = Get-ChildItem $mainOutDir -File | Where-Object {
    $_.Extension -in '.dll', '.exe', '.json', '.pdb', '.xml'
}
$copiedMain = 0
foreach ($f in $mainFiles) {
    Copy-Item $f.FullName $deployDir -Force
    $copiedMain++
}
Write-Host "  主项目: $copiedMain 个文件" -ForegroundColor Green

# 辅助函数: 拷贝项目产物(exe/dll/json/pdb/xml)到部署目录
function Copy-ProjectArtifacts {
    param([string]$SrcDir, [string]$DstDir, [string]$Label)
    if (-not (Test-Path $SrcDir)) {
        Write-Host "  $Label 输出目录不存在，跳过" -ForegroundColor Yellow
        return 0
    }
    $files = Get-ChildItem $SrcDir -File | Where-Object {
        $_.Extension -in '.dll', '.exe', '.json', '.pdb', '.xml'
    }
    $copied = 0
    foreach ($f in $files) {
        Copy-Item $f.FullName $DstDir -Force
        $copied++
    }
    Write-Host "  ${Label}: $copied 个文件" -ForegroundColor Green
    return $copied
}

Write-Host "==> 拷贝 gh 插件产物 ($ghOutDir -> $deployDir)..." -ForegroundColor Cyan
Copy-ProjectArtifacts -SrcDir $ghOutDir -DstDir $deployDir -Label "gh 插件"

Write-Host "==> 拷贝 rg 插件产物 ($rgOutDir -> $deployDir)..." -ForegroundColor Cyan
Copy-ProjectArtifacts -SrcDir $rgOutDir -DstDir $deployDir -Label "rg 插件"

Write-Host "==> 验证部署..." -ForegroundColor Cyan
$jccExe = "$deployDir/jcc.exe"
$ghDll = "$deployDir/jcc-gh.dll"
$rgExe = "$deployDir/jcc-rg.exe"
$depsJson = "$deployDir/jcc.deps.json"
$missing = @()
if (-not (Test-Path $jccExe)) { $missing += "jcc.exe" }
if (-not (Test-Path $ghDll)) { $missing += "jcc-gh.dll" }
if (-not (Test-Path $rgExe)) { $missing += "jcc-rg.exe" }
if (-not (Test-Path $depsJson)) { $missing += "jcc.deps.json" }
if ($missing.Count -gt 0) {
    Write-Host "  缺失文件: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "  验证通过" -ForegroundColor Green
Write-Host "==> 部署完成: $deployDir" -ForegroundColor Green
exit 0
