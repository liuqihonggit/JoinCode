<#
.SYNOPSIS
  校验 13 个工具分组 exe 的「声明 vs 现实」是否一致，并可选抽样真跑。

.DESCRIPTION
  做三类对账（ADR 0157 3.2.8 要求的可审计性）：

    1. 自报对账：<exe> --list-local 报的 local 数，必须等于 node.yaml 里 mode: local 的条数
    2. 清单对账：node.yaml 的 tool_count、tools 条目数、-h 索引里的工具名个数三者一致
    3. 帮助可用：<exe> -h 能输出、不超长（大组必须走索引），单工具 -h 能定位到参数表

  加 -Sample 才真调工具，且只用无副作用的那几个（读文件 / git 状态 / glob / worktree 列表 /
  sleep 1 秒）。写文件、提交、删除类工具一律不抽样 —— 校验脚本不该改仓库。

.PARAMETER SkipBuild
  不编译，直接用已有产物。

.PARAMETER Sample
  抽样真跑每组一个无副作用工具。

.PARAMETER MaxHelpBytes
  单组 -h 输出的字节上限，默认 8192。超过说明该走索引却没走（github 全量曾是 70 KB）。
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$Sample,
    [int]$MaxHelpBytes = 8192,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$groupsRoot = Join-Path $repoRoot "modules/tools/cli_nodes/groups"
$problems = @()
$rows = @()

$groups = Get-ChildItem -Path $groupsRoot -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName ("jcc_tool_" + $_.Name + ".csproj")) } |
    Sort-Object Name

if (-not $groups) {
    Write-Host "找不到任何分组工程: $groupsRoot" -ForegroundColor Red
    exit 2
}

foreach ($group in $groups) {
    $name = $group.Name
    if (-not $SkipBuild) {
        $out = & dotnet build (Join-Path $group.FullName ("jcc_tool_" + $name + ".csproj")) `
            -c $Configuration --no-restore --nologo -v minimal `
            -p:BuildProjectReferences=false -p:EmitCompilerGeneratedFiles=false 2>&1
        if ($LASTEXITCODE -ne 0) {
            $problems += "[$name] 编译失败"
            $out | Select-Object -Last 6 | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
            continue
        }
    }

    $exe = Join-Path $repoRoot ("artifacts/bin/jcc_tool_" + $name + "/" + $Configuration + "/net10.0/jcc-tool-" + $name + ".exe")
    if (-not (Test-Path $exe)) {
        $problems += "[$name] 找不到 exe: $exe"
        continue
    }

    # ── 声明侧：node.yaml ──────────────────────────────────────────────
    $yaml = Get-Content -Path (Join-Path $group.FullName "node.yaml") -Raw
    $declaredTotal = [int]([regex]::Match($yaml, 'tool_count:\s*(\d+)').Groups[1].Value)
    $declaredLocal = ([regex]::Matches($yaml, 'mode:\s*local\b')).Count
    $declaredDelegate = ([regex]::Matches($yaml, 'mode:\s*delegate\b')).Count
    $entryCount = ([regex]::Matches($yaml, '(?m)^\s+- id:')).Count

    if ($declaredTotal -ne $entryCount) {
        $problems += "[$name] node.yaml 自相矛盾: tool_count=$declaredTotal 但只有 $entryCount 条 tools 条目"
    }

    # ── 现实侧：--list-local ─────────────────────────────────────────
    $selfReport = (& $exe --list-local) -join "`n"
    $reportedLocal = 0
    $match = [regex]::Match($selfReport, 'local_count=(\d+)')
    if ($match.Success) { $reportedLocal = [int]$match.Groups[1].Value }
    $reportedDelegate = if ($selfReport -match 'delegated_to=(\S+)') { $Matches[1] } else { "" }

    if ($reportedLocal -ne $declaredLocal) {
        $problems += "[$name] 自报 local=$reportedLocal 与 node.yaml 声明 $declaredLocal 不一致（实现与登记漂移）"
    }
    if ($declaredDelegate -gt 0 -and -not $reportedDelegate) {
        $problems += "[$name] node.yaml 声明了 delegate，但 exe 未自报 delegated_to"
    }

    # ── 帮助侧：-h 体积与单工具定位 ─────────────────────────────────
    $helpText = (& $exe -h) -join "`n"
    $helpBytes = [System.Text.Encoding]::UTF8.GetByteCount($helpText)
    if ($helpText.Length -eq 0) {
        $problems += "[$name] -h 无输出"
    } elseif ($helpBytes -gt $MaxHelpBytes) {
        $problems += "[$name] -h 输出 $helpBytes 字节，超过 $MaxHelpBytes —— 大组应输出索引而不是全量参数表"
    }

    # 单工具 -h：取第一个工具名验证参数表能定位（不执行）
    $firstTool = [regex]::Match($helpText, '(?m)^\s{2}([/\w-]+)\s{2,}').Groups[1].Value
    if ($firstTool) {
        $toolHelp = (& $exe $firstTool -h) -join "`n"
        if ($toolHelp -notmatch '执行方式') {
            $problems += "[$name] '$firstTool -h' 未打印执行方式（无法审计它跑在哪）"
        }
    }

    $rows += [pscustomobject]@{
        Group    = $name
        Total    = $declaredTotal
        Local    = $reportedLocal
        Delegate = $declaredDelegate
        Isolated = $reportedLocal + $declaredDelegate
        Target   = $reportedDelegate
        HelpKB   = [math]::Round($helpBytes / 1KB, 1)
    }
}

# ── 可选抽样：只用无副作用工具 ──────────────────────────────────────
$sampleSpecs = @(
    @{ Group = "file";   Args = @("file_read", "--file_path", (Join-Path $repoRoot "AGENTS.md")) }
    @{ Group = "git";    Args = @("git_status") }
    @{ Group = "search"; Args = @("glob", "--pattern", "resources/tools/*.yaml") }
    @{ Group = "agent";  Args = @("worktree_list_all") }
    @{ Group = "system"; Args = @("sleep", "--duration_seconds", "1") }
    @{ Group = "code";   Args = @("notebook_read", "--file_path", (Join-Path $repoRoot "AGENTS.md")) }
)

if ($Sample) {
    foreach ($spec in $sampleSpecs) {
        $exe = Join-Path $repoRoot ("artifacts/bin/jcc_tool_" + $spec.Group + "/" + $Configuration + "/net10.0/jcc-tool-" + $spec.Group + ".exe")
        if (-not (Test-Path $exe)) { continue }
        $null = & $exe @($spec.Args) 2>&1
        $code = $LASTEXITCODE
        # notebook_read 传 .md 预期失败（退出 2），那是「错误路径可用」，不算问题。
        $expectedFail = $spec.Args[0] -eq "notebook_read"
        if ($expectedFail) {
            if ($code -ne 2) { $problems += "[$($spec.Group)] 错误路径未返回 2（实际 $code）" }
        } elseif ($code -ne 0) {
            $problems += "[$($spec.Group)] 抽样 '$($spec.Args[0])' 退出码 $code"
        }
    }
}

# ── 汇总 ────────────────────────────────────────────────────────────
Write-Host ""
$rows | Format-Table -AutoSize -Property Group, Total, Local, Delegate, Isolated, Target, HelpKB |
    Out-String -Width 200 | Write-Host

$totalTools = ($rows | Measure-Object -Property Total -Sum).Sum
$totalIsolated = ($rows | Measure-Object -Property Isolated -Sum).Sum
$delegateGroups = ($rows | Where-Object { $_.Delegate -gt 0 }).Count
$percent = if ($totalTools -gt 0) { [math]::Floor($totalIsolated * 100 / $totalTools) } else { 0 }
Write-Host ("共 {0} 组 / {1} 个工具，进程隔离 {2}（{3}%），其中 {4} 组走 delegate" -f `
    $rows.Count, $totalTools, $totalIsolated, $percent, $delegateGroups) -ForegroundColor Green

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host ("发现 {0} 个问题:" -f $problems.Count) -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "声明与现实一致。" -ForegroundColor Green
exit 0
