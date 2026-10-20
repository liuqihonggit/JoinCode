#requires -version 5.1
<#
.SYNOPSIS
  DSH 参考物捕获编排：启动 dsh web（隔离）→ CDP 捕获 → 击杀，全流程单次调用完成。

.DESCRIPTION
  产出写入 -OutDir（默认工具目录下 reference/）：
    dsh-web-{W}x{H}.png / dsh-dom-{W}x{H}.json / dsh-tokens.json / dsh-meta.json
  隔离约定：DSH_HOME 指向 .dsh-home/（gitignored）；--no-open 不打扰桌面；结束无残留进程。

.PARAMETER Viewport
  视口，格式 WxH，默认 1440x900。

.PARAMETER OutDir
  产出目录，默认 <工具目录>\reference。

.PARAMETER Script
  捕获脚本路径，默认 capture/capture-dsh.mjs；可传 capture/capture-states-dsh.mjs 等。

.PARAMETER DshHomeDir
  DSH_HOME 目录，默认 <工具目录>\.dsh-home；状态捕获可传独立 home 避免互相污染。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File capture/capture-all.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File capture/capture-all.ps1 -Viewport 1024x768
  powershell -NoProfile -ExecutionPolicy Bypass -File capture/capture-all.ps1 -Script capture/capture-states-dsh.mjs -DshHomeDir .dsh-home-states
#>
[CmdletBinding()]
param(
    [string]$Viewport = "1440x900",
    [string]$OutDir = "",
    [string]$Script = "",
    [string]$DshHomeDir = ""
)

$ErrorActionPreference = "Stop"
$ToolRoot = Split-Path -Parent $PSScriptRoot
$NodeModules = Join-Path $ToolRoot "node_modules"
$DshEntry = Join-Path $NodeModules "@deepseek-ai\dsh\lib\bin.js"
if (-not $Script) { $Script = Join-Path $PSScriptRoot "capture-dsh.mjs" }
$CaptureScript = if ([System.IO.Path]::IsPathRooted($Script)) { $Script } else { Join-Path $ToolRoot $Script }
$TmpDir = Join-Path $ToolRoot ".tmp"
$DshHome = if (-not $DshHomeDir) { Join-Path $ToolRoot ".dsh-home" }
    elseif ([System.IO.Path]::IsPathRooted($DshHomeDir)) { $DshHomeDir }
    else { Join-Path $ToolRoot $DshHomeDir }
if (-not $OutDir) { $OutDir = Join-Path $ToolRoot "reference" }

New-Item -ItemType Directory -Force -Path $TmpDir, $DshHome, $OutDir | Out-Null
$env:DSH_HOME = $DshHome

# 依赖就绪检查（首次自动安装，版本锁定在 package.json）
if (-not (Test-Path -LiteralPath $DshEntry)) {
    Write-Output "node_modules 缺失，执行 npm install（版本锁定见 package.json）..."
    Push-Location $ToolRoot
    try { npm install --no-audit --no-fund --loglevel=error } finally { Pop-Location }
    if (-not (Test-Path -LiteralPath $DshEntry)) { Write-Error "npm install 后仍找不到 dsh 入口: $DshEntry"; exit 1 }
}

$outLog = Join-Path $TmpDir "web-out.log"
$errLog = Join-Path $TmpDir "web-err.log"
Remove-Item -LiteralPath $outLog, $errLog -ErrorAction SilentlyContinue

Write-Output "启动 dsh web（隔离 DSH_HOME=$DshHome, --no-open）..."
$proc = Start-Process -FilePath "node" -ArgumentList $DshEntry, "web", "--no-open" `
    -WorkingDirectory $ToolRoot -RedirectStandardOutput $outLog -RedirectStandardError $errLog `
    -PassThru -NoNewWindow
Write-Output "node PID: $($proc.Id)"

try {
    $url = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 2
        if ($proc.HasExited) {
            Write-Output "dsh 进程提前退出（exit=$($proc.ExitCode)）。日志尾部："
            Get-Content $outLog -ErrorAction SilentlyContinue | Select-Object -Last 10
            Get-Content $errLog -ErrorAction SilentlyContinue | Select-Object -Last 10
            exit 1
        }
        $content = Get-Content $outLog -Raw -ErrorAction SilentlyContinue
        if ($content -match "(http://127\.0\.0\.1:3080/\?token=\S+)") { $url = $Matches[1]; break }
    }
    if (-not $url) {
        Write-Output "等待 120s 未取得 URL。日志尾部："
        Get-Content $outLog -ErrorAction SilentlyContinue | Select-Object -Last 10
        Get-Content $errLog -ErrorAction SilentlyContinue | Select-Object -Last 10
        exit 1
    }
    Write-Output "URL acquired (token hidden)"

    node $CaptureScript $url $OutDir $Viewport
    $captureExit = $LASTEXITCODE
    Write-Output "capture exit code: $captureExit"
} finally {
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*dsh_parity*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

Get-ChildItem $OutDir -ErrorAction SilentlyContinue | Select-Object Name, Length
if ($null -ne $captureExit) { exit $captureExit } else { exit 1 }
