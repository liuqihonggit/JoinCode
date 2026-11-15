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

# 一切分组 exe 调用都必须经此转发。
# 原因：分组 exe 正常业务失败时会把诊断写到 stderr，而本脚本的 $ErrorActionPreference = "Stop"
# 会把原生命令的 stderr 变成终止性异常 —— 于是「被校验对象报了个错」会把对账脚本自己掀翻，
# 连汇总表都打不出来。校验器不许被校验对象的正常错误路径打死。
function Invoke-Probe {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $Exe @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    [pscustomobject]@{ Code = $code; Output = $output.TrimEnd() }
}

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
        $build = Invoke-Probe -Exe "dotnet" -Arguments @(
            "build", (Join-Path $group.FullName ("jcc_tool_" + $name + ".csproj")),
            "-c", $Configuration, "--no-restore", "--nologo", "-v", "minimal",
            "-p:BuildProjectReferences=false", "-p:EmitCompilerGeneratedFiles=false")
        if ($build.Code -ne 0) {
            $problems += "[$name] 编译失败"
            $build.Output -split "`r?`n" | Select-Object -Last 6 |
                ForEach-Object { Write-Output "    $_" }
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
    $selfReport = (Invoke-Probe -Exe $exe -Arguments @("--list-local")).Output
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
    $helpText = (Invoke-Probe -Exe $exe -Arguments @("-h")).Output
    $helpBytes = [System.Text.Encoding]::UTF8.GetByteCount($helpText)
    if ($helpText.Length -eq 0) {
        $problems += "[$name] -h 无输出"
    } elseif ($helpBytes -gt $MaxHelpBytes) {
        $problems += "[$name] -h 输出 $helpBytes 字节，超过 $MaxHelpBytes —— 大组应输出索引而不是全量参数表"
    }

    # 单工具 -h：取第一个工具名验证参数表能定位（不执行）
    $firstTool = [regex]::Match($helpText, '(?m)^\s{2}([/\w-]+)\s{2,}').Groups[1].Value
    if ($firstTool) {
        $toolHelp = (Invoke-Probe -Exe $exe -Arguments @($firstTool, "-h")).Output
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
# code 组整组都是 notebook 系列，仓库里没有 .ipynb，所以先在临时目录建一个再读回来：
# 这样验到的是成功路径（写-读闭环），而不是只验「拿 .md 当 notebook 会报错」。
$sampleSpecs = @(
    @{ Group = "file";   Args = @("file_read", "--file_path", (Join-Path $repoRoot "AGENTS.md"));             Expect = 0 }
    @{ Group = "git";    Args = @("git_status");                                                              Expect = 0 }
    @{ Group = "search"; Args = @("glob", "--pattern", "resources/tools/*.yaml");                             Expect = 0 }
    @{ Group = "agent";  Args = @("worktree_list_all");                                                       Expect = 0 }
    @{ Group = "system"; Args = @("sleep", "--duration_seconds", "1");                                        Expect = 0 }
    @{ Group = "code";   Args = @("notebook_read", "--file_path", (Join-Path $repoRoot "AGENTS.md"));         Expect = 2 }
)

if ($Sample) {
    $probeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("jcc-verify-" + [guid]::NewGuid().ToString("N"))
    $null = New-Item -ItemType Directory -Path $probeRoot -Force
    $notebookPath = Join-Path $probeRoot "probe.ipynb"

    # 写-读闭环放在最前面，避免临时目录里没有 notebook 时后面读空文件。
    $sampleSpecs = @(
        @{ Group = "code"; Args = @("notebook_create", "--file_path", $notebookPath); Expect = 0 }
        @{ Group = "code"; Args = @("notebook_read", "--file_path", $notebookPath);   Expect = 0 }
    ) + $sampleSpecs

    $sampleRuns = 0
    foreach ($spec in $sampleSpecs) {
        $exe = Join-Path $repoRoot ("artifacts/bin/jcc_tool_" + $spec.Group + "/" + $Configuration + "/net10.0/jcc-tool-" + $spec.Group + ".exe")
        if (-not (Test-Path $exe)) { continue }

        $probe = Invoke-Probe -Exe $exe -Arguments $spec.Args
        $sampleRuns++
        if ($probe.Code -ne $spec.Expect) {
            $problems += "[$($spec.Group)] 抽样 '$($spec.Args[0])' 退出码 $($probe.Code)，预期 $($spec.Expect)"
            $probe.Output -split "`r?`n" | Select-Object -First 3 |
                ForEach-Object { Write-Output "    $_" }
        }
    }

    # 只删自己刚建的临时文件，不用 -Recurse。
    Remove-Item -LiteralPath $notebookPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $probeRoot -Force -ErrorAction SilentlyContinue
    Write-Output ("抽样真跑 {0} 次（临时目录，不碰仓库）" -f $sampleRuns)
}

# ── 汇总 ────────────────────────────────────────────────────────────
# 报告体一律走 Write-Output 一次输出：Write-Host 对含换行的多行字符串在部分终端/ConPTY
# 下会吞掉行尾换行，把表格和汇总行粘成一行（「2.6共 13 组」就是这么来的）。
$report = @()
$report += ""
$report += ($rows | Format-Table -AutoSize -Property Group, Total, Local, Delegate, Isolated, Target, HelpKB |
    Out-String -Width 200).TrimEnd()

$totalTools = ($rows | Measure-Object -Property Total -Sum).Sum
$totalIsolated = ($rows | Measure-Object -Property Isolated -Sum).Sum
$delegateGroups = @($rows | Where-Object { $_.Delegate -gt 0 }).Count
$percent = if ($totalTools -gt 0) { [math]::Floor($totalIsolated * 100 / $totalTools) } else { 0 }
$report += ("共 {0} 组 / {1} 个工具，进程隔离 {2}（{3}%），其中 {4} 组走 delegate" -f `
    @($rows).Count, $totalTools, $totalIsolated, $percent, $delegateGroups)

if ($problems.Count -gt 0) {
    $report += ""
    $report += ("发现 {0} 个问题:" -f $problems.Count)
    $problems | ForEach-Object { $report += "  - $_" }
    Write-Output ($report -join [Environment]::NewLine)
    exit 1
}

$report += "声明与现实一致。"
Write-Output ($report -join [Environment]::NewLine)
exit 0
