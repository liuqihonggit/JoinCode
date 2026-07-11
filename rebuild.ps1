[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SkipAudit,
    [switch]$CleanAll
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot

Write-Host "=== Step 0: Shutdown MSBuild Build Server ===" -ForegroundColor Cyan
dotnet build-server shutdown 2>&1 | Out-Null

if ($CleanAll) {
    Write-Host "=== Step 1: 清理 ===" -ForegroundColor Cyan

    # 清理本地 nuget 缓存（旧的分析器 DLL）
    $nugetDir = Join-Path $rootDir ".nuget\packages"
    if (Test-Path $nugetDir) {
        Write-Host "  清理 nuget/ 目录..." -ForegroundColor Yellow
        Remove-Item $nugetDir -Recurse -Force
        Write-Host "  nuget/ 已清理" -ForegroundColor Green
    }

    # 用 dotnet clean 清理编译输出（比递归删除 obj/bin 快得多）
    Write-Host "  dotnet clean..." -ForegroundColor Yellow
    dotnet clean "$rootDir\JoinCode.slnx" -c $Configuration --nologo 2>&1 | Out-Null
    Write-Host "  clean 完成" -ForegroundColor Green
}

Write-Host "=== Step 2: Build solution ===" -ForegroundColor Cyan
$env:MSBUILDDISABLENODEREUSE = "1"
dotnet build "$rootDir\JoinCode.slnx" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: build" -ForegroundColor Red
    exit 1
}

if (-not $SkipTests) {
    Write-Host "=== Step 3: Run tests ===" -ForegroundColor Cyan
    dotnet test "$rootDir\JoinCode.slnx" -c $Configuration --no-build --nologo --filter "Category!=Integration"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: tests" -ForegroundColor Red
        exit 1
    }
}

if (-not $SkipAudit) {
    Write-Host "=== Step 4: Build CLI + Audit ===" -ForegroundColor Cyan
    dotnet build "$rootDir\tools\JccAuditCli\JccAuditCli.csproj" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: CLI build failed, skipping audit" -ForegroundColor Yellow
    } else {
        $auditOutput = Join-Path $rootDir "audit-full.json"
        dotnet run --project "$rootDir\tools\JccAuditCli\JccAuditCli.csproj" -c $Configuration --no-build -- `
            "$rootDir\JoinCode.slnx" --skip-tests --format json --output $auditOutput
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Audit completed: $auditOutput" -ForegroundColor Green
        } else {
            Write-Host "Audit completed with diagnostics (exit code $LASTEXITCODE)" -ForegroundColor Yellow
        }
    }
}

Write-Host "=== All done ===" -ForegroundColor Green
