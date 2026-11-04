# Deploy-JccDev.ps1 — 全量编译 + 部署 jcc 到 jcc.d/dev
# 用途: 全量编译所有域后，拷贝主项目 + gh 插件的所有产物（exe/dll/deps.json/runtimeconfig）到 jcc.d/dev/
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

Write-Host "==> 拷贝 gh 插件产物 ($ghOutDir -> $deployDir)..." -ForegroundColor Cyan
if (Test-Path $ghOutDir) {
    $ghFiles = Get-ChildItem $ghOutDir -File | Where-Object {
        $_.Name -like 'jcc-gh.*'
    }
    $copiedGh = 0
    foreach ($f in $ghFiles) {
        Copy-Item $f.FullName $deployDir -Force
        $copiedGh++
    }
    Write-Host "  gh 插件: $copiedGh 个文件" -ForegroundColor Green
} else {
    Write-Host "  gh 输出目录不存在，跳过" -ForegroundColor Yellow
}

Write-Host "==> 验证部署..." -ForegroundColor Cyan
$jccExe = "$deployDir/jcc.exe"
$ghDll = "$deployDir/jcc-gh.dll"
$depsJson = "$deployDir/jcc.deps.json"
$missing = @()
if (-not (Test-Path $jccExe)) { $missing += "jcc.exe" }
if (-not (Test-Path $ghDll)) { $missing += "jcc-gh.dll" }
if (-not (Test-Path $depsJson)) { $missing += "jcc.deps.json" }
if ($missing.Count -gt 0) {
    Write-Host "  缺失文件: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "  验证通过" -ForegroundColor Green
Write-Host "==> 部署完成: $deployDir" -ForegroundColor Green
exit 0
