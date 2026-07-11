[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputDir = "",

    [int]$MaxCpuCount = 0
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$mainProject = "$rootDir\src\JoinCode\JoinCode.csproj"
$dreamProject = "$rootDir\plugins\Dream\Dream.csproj"

if (-not $OutputDir) {
    $OutputDir = "$rootDir\publish\$Runtime"
}

$cpuArg = if ($MaxCpuCount -gt 0) { "-m:$MaxCpuCount" } else { "" }

Write-Host "=== JoinCode NativeAOT Publish ===" -ForegroundColor Cyan
Write-Host "  Runtime : $Runtime" -ForegroundColor Gray
Write-Host "  Output  : $OutputDir" -ForegroundColor Gray
Write-Host "  MaxCPU  : $(if ($MaxCpuCount -gt 0) { $MaxCpuCount } else { 'auto' })" -ForegroundColor Gray
Write-Host ""

$commonPublishProps = @(
    "-p:SelfContained=true",
    "-p:TrimMode=full",
    "-p:InvariantGlobalization=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:GenerateDocumentationFile=false",
    "-p:PublishReleaseWithDebugInfo=false"
)

Write-Host "=== Step 1: Publish jcc (NativeAOT) ===" -ForegroundColor Cyan
$jccArgs = @(
    $mainProject,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $OutputDir,
    "--nologo"
) + $commonPublishProps
if ($cpuArg) { $jccArgs += $cpuArg }
& dotnet publish @jccArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: publish jcc" -ForegroundColor Red
    exit 1
}

Write-Host "=== Step 2: Publish dream (NativeAOT) ===" -ForegroundColor Cyan
$dreamOutputDir = Join-Path $OutputDir "plugins"
$dreamArgs = @(
    $dreamProject,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $dreamOutputDir,
    "--nologo"
) + $commonPublishProps
if ($cpuArg) { $dreamArgs += $cpuArg }
& dotnet publish @dreamArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: publish dream" -ForegroundColor Red
    exit 1
}

Write-Host "=== Step 3: Clean debug artifacts ===" -ForegroundColor Cyan
$removedCount = 0
Get-ChildItem $OutputDir -Include "*.pdb","*.xml" -Recurse | ForEach-Object {
    Remove-Item $_.FullName -Force
    $removedCount++
}
if ($removedCount -gt 0) {
    Write-Host "  Removed $removedCount debug files (pdb/xml)" -ForegroundColor Gray
}

$exeName = if ($Runtime.StartsWith("win-")) { "jcc.exe" } else { "jcc" }
$dreamExeName = if ($Runtime.StartsWith("win-")) { "dream.exe" } else { "dream" }
$exePath = Join-Path $OutputDir $exeName
$dreamExePath = Join-Path $dreamOutputDir $dreamExeName

$allOk = $true

if (Test-Path $exePath) {
    $fileInfo = Get-Item $exePath
    $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
    Write-Host "  jcc    : $exePath ($sizeMB MB)" -ForegroundColor Green
} else {
    Write-Host "  WARNING: jcc not found at $exePath" -ForegroundColor Yellow
    $allOk = $false
}

if (Test-Path $dreamExePath) {
    $fileInfo = Get-Item $dreamExePath
    $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
    Write-Host "  dream   : $dreamExePath ($sizeMB MB)" -ForegroundColor Green
} else {
    Write-Host "  WARNING: dream not found at $dreamExePath" -ForegroundColor Yellow
    $allOk = $false
}

$totalSizeMB = [math]::Round((Get-ChildItem $OutputDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
$fileCount = (Get-ChildItem $OutputDir -File -Recurse).Count

Write-Host ""
if ($allOk) {
    Write-Host "=== Publish completed successfully ===" -ForegroundColor Green
} else {
    Write-Host "=== Publish completed with warnings ===" -ForegroundColor Yellow
}
Write-Host "  Total   : $totalSizeMB MB ($fileCount files)" -ForegroundColor Gray
Write-Host ""
Write-Host "  To use globally, add to PATH:" -ForegroundColor Yellow
Write-Host "    `$env:PATH += `";$OutputDir`"" -ForegroundColor White
