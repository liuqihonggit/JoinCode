# Build-AllDomains.ps1 — 全仓编译检查（防复发 B，TASK070 CI #959 事故）
# 用途: 跨域契约变更（接口新增成员/协议扩展/共享 DTO 变更）后，本地全量编译 16 域 + 卫星项目，
#       不等 CI 就暴露其他域的实现遗漏（如 IToolPermissionManager 扩展漏 native_gui 的 fake 实现）。
# 用法: pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 [-Domains a,b,c] [-Configuration Debug]
# 退出码: 0=全部成功; 1=存在失败（输出各域结果表）
param(
    [string]$Domains = "",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot

# 16 功能域 slnx + 跨层测试/卫星项目（无 slnx 的域按 CI ci-gui-daily.yml 形态直构 csproj）
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

# 无 slnx 的卫星域 — 与 slnx 同样按 (domain, target) 参与结果表
$csprojTargets = @(
    @{ Domain = "test";      Target = "test/unit/host.tests/Host.Tests.csproj" },
    @{ Domain = "native_gui"; Target = "native_gui/tests/native_gui.core.tests/NativeGui.Core.Tests.csproj" },
    @{ Domain = "native_gui"; Target = "native_gui/tests/native_gui.host.tests/NativeGui.Host.Tests.csproj" }
)

if ($Domains -ne "") {
    $wanted = $Domains.Split(",") | ForEach-Object { $_.Trim() }
    $allSolutions = $allSolutions | Where-Object { $name = ($_ -split "/")[0]; $wanted -contains $name }
}

$results = @()
$failed = @()
$targets = @()
foreach ($sln in $allSolutions) {
    if (Test-Path $sln) { $targets += @{ Domain = ($sln -split "/")[0]; Target = $sln } }
}
$targets += $csprojTargets | Where-Object { Test-Path $_.Target }

if ($Domains -ne "") {
    $wanted = $Domains.Split(",") | ForEach-Object { $_.Trim() }
    $targets = $targets | Where-Object { $wanted -contains $_.Domain }
}

foreach ($t in $targets) {
    Write-Host ">>> Building $($t.Target) ..." -ForegroundColor Cyan
    $output = dotnet build $t.Target -c $Configuration --nologo -v q 2>&1
    $exitCode = $LASTEXITCODE
    $ok = $exitCode -eq 0
    $results += [pscustomobject]@{ Domain = $t.Domain; Target = $t.Target; Ok = $ok }
    if (-not $ok) {
        $failed += $t.Target
        Write-Host "    FAILED ($exitCode)" -ForegroundColor Red
        $output | Select-String -Pattern "error" | Select-Object -First 15 | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
    }
}

Write-Host ""
Write-Host "===== 全仓编译结果 =====" -ForegroundColor Cyan
$results | Format-Table Domain, Ok -AutoSize | Out-String | Write-Host
if ($failed.Count -gt 0) {
    Write-Host "失败 solution: $($failed -join ', ')" -ForegroundColor Red
    Pop-Location
    exit 1
}

Write-Host "全部通过" -ForegroundColor Green
Pop-Location
exit 0
