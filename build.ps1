<#
.SYNOPSIS
  编译脚本 - 支持Fast/Full模式、组件独立编译、串行/并行、计时

.PARAMETER Mode
   Fast   - 启用自定义分析器,禁用内置NET分析器,日常迭代用 (全量~38s)
   Full   - 启用全部分析器(含内置),CI/提交前用 (全量~56s)

.PARAMETER Fast
  等价于 -Mode Fast (向后兼容)

.PARAMETER Serial
  串行编译 /m:1

.PARAMETER MaxCpuCount
  并行度控制

.PARAMETER NoShutdown
  不关闭MSBuild Server,加速增量编译

.PARAMETER GeneratorsOnly
  仅编译生成器项目 (generators/generators.slnx)

.PARAMETER ComponentsOnly
  仅编译组件项目 (components/components.slnx)

.PARAMETER Component
  仅编译指定组件 (如 Dream, Mcp, CodeIndex, FlexLayout, Browser)

.PARAMETER KillAll
  强制终止所有 MSBuild Server 进程 (比 shutdown 更彻底,文件锁时使用)

.PARAMETER CI
  CI模式: 跳过MSBuild Server管理、分离restore/build步骤、输出TRX日志

.PARAMETER Timing
  输出计时报告

.EXAMPLE
  .\build.ps1                              # Full模式 (提交前)
  .\build.ps1 -Fast                        # 日常开发 - 最快
  .\build.ps1 -Fast -NoShutdown            # 最快增量 (保留MSBuild Server, 改哪编哪)
  .\build.ps1 -Fast -MaxCpuCount 4         # 限制并行度
  .\build.ps1 -Fast -KillAll               # 清理所有MSBuild进程后编译
  .\build.ps1 -GeneratorsOnly              # 仅编译生成器
  .\build.ps1 -ComponentsOnly              # 仅编译组件
  .\build.ps1 -Component Dream             # 仅编译 Dream 组件
#>
[CmdletBinding()]
param(
    [ValidateSet('Fast', 'Full')]
    [string]$Mode,
    [switch]$Fast,
    [string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$Serial,
    [int]$MaxCpuCount = 0,
    [switch]$NoShutdown,
    [switch]$GeneratorsOnly,
    [switch]$ComponentsOnly,
    [string]$Component,
    [string]$Subsystem,
    [switch]$KillAll,
    [switch]$CI,
    [switch]$Timing
)

# -Fast 开关向后兼容
if (-not $Mode) { $Mode = if ($Fast) { 'Fast' } else { 'Full' } }

# CI 模式默认 Release，本地开发默认 Debug
if ($CI -and -not $PSBoundParameters.ContainsKey('Configuration')) {
    $Configuration = 'Release'
}

$ErrorActionPreference = 'Stop'
$rootDir = $PSScriptRoot
$slnx = "$rootDir\JoinCode.slnx"
$compSlnx = "$rootDir\components\components.slnx"
$genSlnx = "$rootDir\generators\generators.slnx"

# ── 模式 → MSBuild 属性 ──
$modeProps = if ($Mode -eq 'Fast') {
    '/p:JccAnalyzers=true', '/p:EnableNETAnalyzers=false', '/p:AnalysisLevel=none', '/p:EmitCompilerGeneratedFiles=false'
} else {
    '/p:JccAnalyzers=true', '/p:EnableNETAnalyzers=true', '/p:AnalysisLevel=latest', '/p:EmitCompilerGeneratedFiles=true'
}

# ── 并行度 ──
$parallelArg = if ($Serial) { '/m:1' } elseif ($MaxCpuCount -gt 0) { "/m:$MaxCpuCount" } else { '' }

# ── 通用 MSBuild 参数 ──
$commonBuildArgs = @('/nodeReuse:false')

# ── 显示配置 ──
Write-Host ''
if ($GeneratorsOnly) {
    Write-Host "  Mode=GeneratorsOnly  Config=$Configuration" -ForegroundColor Cyan
} else {
    Write-Host "  Mode=$Mode  Config=$Configuration  Serial=$Serial  NoShutdown=$NoShutdown" -ForegroundColor Cyan
}
Write-Host ''

# ── Step 0: MSBuild Server ──
if ($CI) {
    Write-Host '=== Step 0: Skip (CI mode) ===' -ForegroundColor DarkGray
} else {
# 检测残留MSBuild进程数量
$msbuildCount = (Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object {
    $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
    $cmd -and $cmd -match 'MSBuild\.dll'
}).Count

if ($KillAll -or ($msbuildCount -gt 5 -and -not $NoShutdown)) {
    $action = if ($KillAll) { 'KillAll' } else { "Auto-cleanup ($msbuildCount processes detected)" }
    Write-Host "=== Step 0: Kill processes [$action] ===" -ForegroundColor Yellow

    # 杀 MSBuild Server 进程
    $msbuildProcs = Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object {
        $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
        $cmd -and $cmd -match 'MSBuild\.dll'
    }
    if ($msbuildProcs) {
        $count = $msbuildProcs.Count
        $memMB = [math]::Round(($msbuildProcs | Measure-Object -Property WorkingSet64 -Sum).Sum / 1MB)
        Write-Host "  Killing $count MSBuild Server processes (${memMB}MB)" -ForegroundColor Yellow
        $msbuildProcs | Stop-Process -Force
    }

    # KillAll 时同时杀 testhost/vstest 进程（避免 DLL 锁定）
    if ($KillAll) {
        $testProcs = Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object {
            $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
            $cmd -and $cmd -match 'testhost|vstest'
        }
        if ($testProcs) {
            $tCount = $testProcs.Count
            $tMemMB = [math]::Round(($testProcs | Measure-Object -Property WorkingSet64 -Sum).Sum / 1MB)
            Write-Host "  Killing $tCount testhost/vstest processes (${tMemMB}MB)" -ForegroundColor Yellow
            $testProcs | Stop-Process -Force
        }
    }

    Start-Sleep -Milliseconds 500
    if (-not $msbuildProcs -and -not $testProcs) {
        Write-Host '  No processes found' -ForegroundColor DarkGray
    }
} elseif (-not $NoShutdown) {
    Write-Host '=== Step 0: Shutdown MSBuild Build Server ===' -ForegroundColor Cyan
    dotnet build-server shutdown 2>&1 | Out-Null
} else {
    Write-Host '=== Step 0: Skip MSBuild shutdown (NoShutdown) ===' -ForegroundColor DarkGray
}
} # end CI else block

# ── GeneratorsOnly: 仅编译生成器 ──
if ($GeneratorsOnly) {
    Write-Host '=== Build generators ===' -ForegroundColor Cyan
    $buildSw = [System.Diagnostics.Stopwatch]::StartNew()
    $buildArgs = @('build', $genSlnx, '-c', $Configuration, '--nologo') + $commonBuildArgs
    if ($parallelArg) { $buildArgs += $parallelArg }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAILED: generators build' -ForegroundColor Red; exit 1 }
    $buildSw.Stop()
    Write-Host "Generators OK: $($buildSw.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor Green
    return
}

# ── Component: 仅编译指定组件 ──
if ($Component) {
    $compSlnxPath = "$rootDir\components\$Component\$Component.slnx"
    if (-not (Test-Path $compSlnxPath)) {
        # 没有 slnx 的组件，直接编译 csproj
        $compCsproj = "$rootDir\components\$Component\src\$Component.csproj"
        if (Test-Path $compCsproj) {
            $compSlnxPath = $compCsproj
        } else {
            # 搜索领域目录下的组件
            $found = Get-ChildItem -Path "$rootDir\components" -Directory | Where-Object { $_.Name -match '^\d{2}-' } | ForEach-Object {
                $candidate = "$($_.FullName)\$Component\$Component.slnx"
                if (Test-Path $candidate) { $candidate }
                else {
                    $candidate = "$($_.FullName)\$Component\src\$Component.csproj"
                    if (Test-Path $candidate) { $candidate }
                }
            } | Select-Object -First 1
            if (-not $found) {
                Write-Host "FAILED: Component '$Component' not found (no .slnx or .csproj)" -ForegroundColor Red; exit 1
            }
            $compSlnxPath = $found
        }
    }
    Write-Host "=== Build component: $Component ===" -ForegroundColor Cyan
    $buildSw = [System.Diagnostics.Stopwatch]::StartNew()
    $buildArgs = @('build', $compSlnxPath, '-c', $Configuration, '--nologo') + $commonBuildArgs
    if ($parallelArg) { $buildArgs += $parallelArg }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: component $Component build" -ForegroundColor Red; exit 1 }
    $buildSw.Stop()
    Write-Host "Component $Component OK: $($buildSw.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor Green
    return
}

# ── ComponentsOnly: 仅编译全部组件 ──
if ($ComponentsOnly) {
    Write-Host '=== Build components ===' -ForegroundColor Cyan
    $buildSw = [System.Diagnostics.Stopwatch]::StartNew()
    $buildArgs = @('build', $compSlnx, '-c', $Configuration, '--nologo') + $commonBuildArgs
    if ($parallelArg) { $buildArgs += $parallelArg }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAILED: components build' -ForegroundColor Red; exit 1 }
    $buildSw.Stop()
    Write-Host "Components OK: $($buildSw.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor Green
    return
}

# ── Subsystem: 仅编译+测试指定子系统 ──
if ($Subsystem) {
    $subsystemMap = @{
        'Guard'  = @('Guard', 'Guard.Tests')
        'Vault'  = @('Vault', 'Vault.Tests')
        'Brain'  = @('Brain', 'Brain.Tests')
        'Hands'  = @('Hands', 'Hands.Tests')
        'Eyes'   = @('Eyes', 'Eyes.Tests')
        'Composition'   = @('Composition', 'Composition.Tests')
        'Clock'  = @('Clock', 'Clock.Tests')
        'Host'   = @('JoinCode', 'Host.Tests')
        'Infra'  = @('Infrastructure', 'Infra.Tests')
    }
    if (-not $subsystemMap.ContainsKey($Subsystem)) {
        Write-Host "FAILED: Unknown subsystem '$Subsystem'. Available: $($subsystemMap.Keys -join ', ')" -ForegroundColor Red; exit 1
    }
    $subName, $testName = $subsystemMap[$Subsystem]
    Write-Host "=== Subsystem: $Subsystem ===" -ForegroundColor Cyan
    $buildSw = [System.Diagnostics.Stopwatch]::StartNew()

    # 编译子系统源码 + 测试
    $testCsproj = if ($Subsystem -eq 'Host') {
        "$rootDir\src\JoinCode\JoinCode.csproj"
    } elseif ($Subsystem -eq 'Infra') {
        "$rootDir\src\Infrastructure\Infrastructure.csproj"
    } else {
        "$rootDir\src\subsystems\$subName\$subName.csproj"
    }
    Write-Host "  Building $subName..." -ForegroundColor Yellow
    dotnet build $testCsproj -c $Configuration --nologo /nodeReuse:false 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $subName build" -ForegroundColor Red; exit 1 }

    $testProj = "$rootDir\tests\Unit\$testName\$testName.csproj"
    if (Test-Path $testProj) {
        Write-Host "  Building $testName..." -ForegroundColor Yellow
        dotnet build $testProj -c $Configuration --nologo /nodeReuse:false 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $testName build" -ForegroundColor Red; exit 1 }

        if (-not $SkipTests) {
            Write-Host "  Testing $testName..." -ForegroundColor Yellow
            dotnet test $testProj -c $Configuration --no-build --nologo --filter "Category!=Integration&Category!=Benchmark"
            if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $testName tests" -ForegroundColor Red; exit 1 }
        }
    }
    $buildSw.Stop()
    Write-Host "Subsystem $Subsystem OK: $($buildSw.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor Green
    return
}

# ── Step 1: Build ──
# Full 和 Fast 模式均先编译 components 再编译 JoinCode
# components 是 JoinCode 的 NuGet 依赖，必须先编译
Write-Host "=== Step 1a: Build Components ($Mode) ===" -ForegroundColor Cyan

$compBuildArgs = @('build', $compSlnx, '-c', $Configuration, '--nologo') + $commonBuildArgs
if ($parallelArg) { $compBuildArgs += $parallelArg }
$compBuildArgs += $modeProps
if ($CI) { $compBuildArgs += '--no-restore'; $compBuildArgs += '/p:SkipLocalPack=true' }

$buildSw = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet @compBuildArgs
$compExit = $LASTEXITCODE

if ($compExit -ne 0) {
    Write-Host "FAILED: components build ($($buildSw.Elapsed.ToString('mm\:ss')))" -ForegroundColor Red
    exit 1
}
$buildSw.Stop()
Write-Host "Components OK: $($buildSw.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor Green

Write-Host "=== Step 1b: Build Host ($Mode) ===" -ForegroundColor Cyan

$buildArgs = @('build', $slnx, '-c', $Configuration, '--nologo') + $commonBuildArgs
if ($parallelArg) { $buildArgs += $parallelArg }
$buildArgs += $modeProps
if ($CI) { $buildArgs += '--no-restore'; $buildArgs += '/p:SkipLocalPack=true' }

$buildSw2 = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet @buildArgs
$buildExit = $LASTEXITCODE
$buildSw2.Stop()

if ($buildExit -ne 0) {
    Write-Host "FAILED: host build ($($buildSw2.Elapsed.ToString('mm\:ss')))" -ForegroundColor Red
    exit 1
}
Write-Host "Host OK: $($buildSw2.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor Green

# ── Step 2: Tests ──
$testSw = $null
if (-not $SkipTests) {
    Write-Host '=== Step 2: Unit tests ===' -ForegroundColor Cyan
    $testSw = [System.Diagnostics.Stopwatch]::StartNew()
    $testFilter = 'Category!=Integration&Category!=Benchmark'

    $testArgs = @('test', $compSlnx, '-c', $Configuration, '--no-build', '--nologo', '--filter', $testFilter, '--logger', 'console;verbosity=detailed')
    if ($CI) { $testArgs += @('--logger', 'trx;LogFileName=unit-tests.trx') }

    & dotnet @testArgs
    $testExit = $LASTEXITCODE

    $hostTestArgs = @('test', $slnx, '-c', $Configuration, '--no-build', '--nologo', '--filter', $testFilter, '--logger', 'console;verbosity=detailed')
    if ($CI) { $hostTestArgs += @('--logger', 'trx;LogFileName=host-tests.trx') }

    & dotnet @hostTestArgs
    $hostTestExit = $LASTEXITCODE

    if ($testExit -ne 0 -or $hostTestExit -ne 0) {
        Write-Host 'FAILED: tests' -ForegroundColor Red
        exit 1
    }
    $testSw.Stop()
    Write-Host "Tests OK: $($testSw.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor Green
}

# ── 计时 ──
if ($Timing) {
    $total = $buildSw.Elapsed.Add($buildSw2.Elapsed)
    if ($testSw) { $total = $total.Add($testSw.Elapsed) }
    Write-Host ''
    Write-Host "  TIMING  Components=$($buildSw.Elapsed.ToString('mm\:ss\.ff'))  Host=$($buildSw2.Elapsed.ToString('mm\:ss\.ff'))  Total=$($total.ToString('mm\:ss\.ff'))  Mode=$Mode" -ForegroundColor Cyan
}

Write-Host ''
Write-Host "=== Build completed ($Mode) ===" -ForegroundColor Green
