# Build-AllDomains.ps1 — 全仓编译检查（防复发 B，TASK070 CI #959 事故）
# 用途: 跨域契约变更（接口新增成员/协议扩展/共享 DTO 变更）后，本地全量编译 16 域 + 卫星项目，
#       不等 CI 就暴露其他域的实现遗漏（如 fake 实现漏 CS0535）。
#
# 用法（日常优先按需，别动不动跑全量）:
#   pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -Changed
#       只编译 git 改动涉及的 csproj，秒级。AI 助手日常改代码后用这个。
#   pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -Changed -LastCommit
#       连同最近一次提交一起算改动范围（刚提交完想复核时用）。
#   pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -Changed -Since HEAD~3
#       把最近 N 次提交纳入改动范围，复核刚做完的一批改动。
#   pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -Changed -NoAnalyzers
#       按需编译且关闭 JCC/NET 分析器与生成文件落盘，最快。
#   pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1
#       完整全仓（分钟级）：JoinCode.slnx + native_tui/native_gui（UI 域不在 JoinCode.slnx 内，默认一并编译）。
#       跨域契约变更 / CI 前才用。
#   pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -NoAnalyzers
#       全仓但关分析器，明显提速；编译器错误（CS0535 等）仍会暴露。
#   pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -Domains a,b,c
#       按域编译（16 域 slnx + 卫星项目）。
# 退出码: 0=全部成功; 1=存在失败
param(
    [string]$Domains = "",
    [string]$Configuration = "Debug",
    [string]$Since = "",
    [switch]$Changed,
    [switch]$LastCommit,
    [switch]$NoAnalyzers,
    [switch]$IncludeUntracked
)

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot

$analyzerArgs = @()
if ($NoAnalyzers) {
    $analyzerArgs = @(
        "/p:JccAnalyzers=false",
        "/p:EnableNETAnalyzers=false",
        "/p:EnforceCodeStyleInBuild=false",
        "/p:EmitCompilerGeneratedFiles=false"
    )
}

function Get-ChangedProjectPaths {
    param([string]$Root)

    $files = @()
    $files += @(git diff --name-only --diff-filter=ACMR HEAD)
    $files += @(git diff --name-only --diff-filter=ACMR --cached)
    if ($LastCommit) {
        $files += @(git diff --name-only --diff-filter=ACMR HEAD~1 HEAD)
    }
    if (-not [string]::IsNullOrWhiteSpace($Since)) {
        $files += @(git diff --name-only --diff-filter=ACMR $Since HEAD)
    }
    if ($IncludeUntracked) {
        $files += @(git ls-files --others --exclude-standard)
    }

    $projects = @()
    foreach ($file in $files) {
        if ([string]::IsNullOrWhiteSpace($file)) { continue }
        if ($file -notmatch '\.(cs|csproj|props|targets)$') { continue }
        $full = Join-Path $Root $file
        $dir = Split-Path -Parent $full
        while ($dir -and $dir.Length -ge $Root.Length) {
            $candidate = Get-ChildItem -Path $dir -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -ne $candidate) {
                $projects += $candidate.FullName
                break
            }
            $parent = Split-Path -Parent $dir
            if ($parent -eq $dir) { break }
            $dir = $parent
        }
    }
    return @($projects | Sort-Object -Unique)
}

function Invoke-BuildTarget {
    param([string]$Target, [string[]]$ExtraArgs, [string[]]$AnalyzerArgs)

    Write-Host ">>> dotnet build $Target ..." -ForegroundColor Cyan
    $buildArgs = @("build", $Target, "-c", $Configuration, "--nologo", "-v", "q") + $ExtraArgs + $AnalyzerArgs
    $log = Join-Path $env:TEMP ("jcc_build_" + [guid]::NewGuid().ToString("N") + ".log")
    dotnet @buildArgs 2>&1 | Tee-Object -FilePath $log | Out-Null
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $errors = @(Select-String -Path $log -Pattern "error" | Select-Object -First 30)
        if ($errors.Count -eq 0) {
            Write-Host "    无直接 error 行（可能被 MSB4181/级联失败掩盖），请查看日志" -ForegroundColor Yellow
        }
        $errors | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
        Write-Host "    日志: $log" -ForegroundColor Yellow
    }
    return $code
}

# 按需编译 — 日常默认推荐路径
if ($Changed) {
    $projects = @(Get-ChangedProjectPaths -Root $repoRoot)
    if ($projects.Count -eq 0) {
        Write-Host "无源码改动，跳过编译。" -ForegroundColor Green
        Pop-Location
        exit 0
    }
    Write-Host "按需编译 $($projects.Count) 个项目:" -ForegroundColor Cyan
    $failed = @()
    foreach ($project in $projects) {
        $code = Invoke-BuildTarget -Target $project -ExtraArgs @() -AnalyzerArgs $analyzerArgs
        if ($code -ne 0) { $failed += $project }
    }
    if ($failed.Count -gt 0) {
        Write-Host "失败项目: $($failed -join ', ')" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Write-Host "按需编译全部通过 ($($projects.Count) 个项目)" -ForegroundColor Green
    Pop-Location
    exit 0
}

# 完整全仓 — 默认覆盖 JoinCode.slnx + native_tui/native_gui（这两个域不在 JoinCode.slnx 内，
# 遗漏会放过跨域契约漂移：TASK099 B7 实测 GoalEngine/INodeRegistry 成员变更只被 UI 域编译暴露）
if ($Domains -eq "") {
    $fullTargets = @(
        @{ Domain = "solution";   Target = "build/sln/JoinCode.slnx"; Extra = @("--no-restore") },
        @{ Domain = "native_tui"; Target = "native_tui/native_tui.slnx"; Extra = @() },
        @{ Domain = "native_gui"; Target = "native_gui/tests/native_gui.core.tests/NativeGui.Core.Tests.csproj"; Extra = @() },
        @{ Domain = "native_gui"; Target = "native_gui/tests/native_gui.host.tests/NativeGui.Host.Tests.csproj"; Extra = @() }
    )
    $fullResults = @()
    $fullFailed = @()
    foreach ($t in $fullTargets) {
        $code = Invoke-BuildTarget -Target $t.Target -ExtraArgs $t.Extra -AnalyzerArgs $analyzerArgs
        $ok = $code -eq 0
        $fullResults += [pscustomobject]@{ Domain = $t.Domain; Target = $t.Target; Ok = $ok }
        if (-not $ok) { $fullFailed += $t.Target }
    }
    Write-Host ""
    Write-Host "===== 编译结果 =====" -ForegroundColor Cyan
    $fullResults | Format-Table Domain, Ok -AutoSize | Out-String | Write-Host
    if ($fullFailed.Count -gt 0) {
        Write-Host "失败 target: $($fullFailed -join ', ')" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Write-Host "全部通过" -ForegroundColor Green
    Pop-Location
    exit 0
}

# 按域编译模式
$allSolutions = @(
    "modules/abstractions/module.slnx",
    "modules/async_primitives/module.slnx",
    "modules/collections/module.slnx",
    "modules/cognition/module.slnx",
    "modules/composition/module.slnx",
    "modules/generators/module.slnx",
    "modules/guard/module.slnx",
    "modules/llm/module.slnx",
    "modules/mcp/module.slnx",
    "modules/perception/module.slnx",
    "modules/persistence/module.slnx",
    "modules/plugins/module.slnx",
    "modules/services/module.slnx",
    "modules/storage/module.slnx",
    "modules/tools/module.slnx",
    "modules/apps/module.slnx",
    "native_tui/native_tui.slnx"
)

$csprojTargets = @(
    @{ Domain = "test";       Target = "test/unit/host.tests/Host.Tests.csproj" },
    @{ Domain = "native_gui"; Target = "native_gui/tests/native_gui.core.tests/NativeGui.Core.Tests.csproj" },
    @{ Domain = "native_gui"; Target = "native_gui/tests/native_gui.host.tests/NativeGui.Host.Tests.csproj" }
)

$wanted = $Domains.Split(",") | ForEach-Object { $_.Trim() }
$targets = @()
foreach ($sln in $allSolutions) {
    if ((Test-Path $sln) -and ($wanted -contains ($sln -split "/")[0])) {
        $targets += @{ Domain = ($sln -split "/")[0]; Target = $sln }
    }
}
$targets += $csprojTargets | Where-Object { (Test-Path $_.Target) -and ($wanted -contains $_.Domain) }

$results = @()
$failed = @()
foreach ($t in $targets) {
    $code = Invoke-BuildTarget -Target $t.Target -ExtraArgs @() -AnalyzerArgs $analyzerArgs
    $ok = $code -eq 0
    $results += [pscustomobject]@{ Domain = $t.Domain; Target = $t.Target; Ok = $ok }
    if (-not $ok) { $failed += $t.Target }
}

Write-Host ""
Write-Host "===== 编译结果 =====" -ForegroundColor Cyan
$results | Format-Table Domain, Ok -AutoSize | Out-String | Write-Host
if ($failed.Count -gt 0) {
    Write-Host "失败 solution: $($failed -join ', ')" -ForegroundColor Red
    Pop-Location
    exit 1
}
Write-Host "全部通过" -ForegroundColor Green
Pop-Location
exit 0
