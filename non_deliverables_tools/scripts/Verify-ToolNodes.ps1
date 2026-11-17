<#
.SYNOPSIS
  校验 13 个工具分组 exe 的「声明 vs 现实」是否一致，并可选抽样真跑。

.DESCRIPTION
  做三类对账（ADR 0157 3.2.8 要求的可审计性）：

    1. 自报对账：<exe> --list-local 报的 local 数，必须等于 node.yaml 里 mode: local 的条数
    2. 清单对账：node.yaml 的 tool_count、tools 条目数、-h 索引里的工具名个数三者一致
    3. 帮助可用：<exe> -h 能输出、不超长（大组必须走索引），单工具 -h 能定位到参数表

  另有声明侧自洽（不需要 exe，-DeclarationsOnly 只跑这部分）：
  tool_count / local_tool_count 与条目对得上、mode 只有 local/delegate/host、id 不重复、
  exe 名与组名一致、delegates 与 group_map.yaml 双向一致、分组目录与 groups 段双向一致。

  加 -Sample 才真调工具，且只用无副作用的那几个（读文件 / git 状态 / glob / worktree 列表 /
  sleep 1 秒 / 临时目录里建一个 notebook 再读回）。写文件、提交、删除类工具一律不抽样 ——
  校验脚本不该改仓库。

.PARAMETER SkipBuild
  不编译，直接用已有产物。

.PARAMETER FromScratch
  CI 冷启动用：分组工程自己完成还原与依赖构建（去掉 --no-restore 与 -p:BuildProjectReferences=false）。
  本地增量编译不要加它 —— 那两个开关是为「依赖产物已就绪」的快路径准备的，
  在干净容器里会让 MSBuild 找不到 assets 文件与契约层 DLL。

.PARAMETER DeclarationsOnly
  只验声明侧：13 份 node.yaml 自洽 + 与 group_map.yaml 交叉一致。
  不编译、不跑 exe，几秒完成 —— CI 的 tool-node-declarations job 用这个模式，
  好在等 13 个 exe 编完之前就把「yaml 自己写错了」拦下来。

.PARAMETER Sample
  抽样真跑每组一个无副作用工具。

.PARAMETER MaxHelpBytes
  单组 -h 输出的字节上限，默认 8192。超过说明该走索引却没走（github 全量曾是 70 KB）。
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$DeclarationsOnly,
    [switch]$FromScratch,
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

# 工具 id 归一化：slash 组的 node.yaml 登记名与 exe 自报名在前导斜杠上不一致（/version vs version），
# 与 CliNodeRunner.CanonicalToolId 同口径处理，否则集合比对会对 slash 组假报漂移。
function Normalize-ToolId([string]$id) { if ($id) { $id.TrimStart('/') } else { $id } }

if (-not $groups) {
    Write-Host "找不到任何分组工程: $groupsRoot" -ForegroundColor Red
    exit 2
}

if ($DeclarationsOnly -and $Sample) {
    Write-Output "-DeclarationsOnly 与 -Sample 互斥（前者不跑任何 exe），已忽略 -Sample。"
    $Sample = $false
}

# ── 单一数据源侧：group_map.yaml ────────────────────────────────────
# 只解析两段键名，不引入 YAML 库：delegates 段（委托给独立 exe 的组）与 groups 段（组→分类）。
$groupMapPath = Join-Path $repoRoot "resources/tools/group_map.yaml"
$groupMapText = Get-Content -Path $groupMapPath -Raw

$delegatedInMap = @{}
$delegateSection = [regex]::Match($groupMapText, '(?ms)^delegates:\s*$(.*?)(?=^\S)')
if ($delegateSection.Success) {
    foreach ($m in [regex]::Matches($delegateSection.Groups[1].Value, '(?m)^  ([A-Za-z0-9_]+):\s*$')) {
        $delegatedInMap[$m.Groups[1].Value] = $true
    }
} else {
    $problems += "group_map.yaml 里找不到 delegates 段（github 组的委托登记靠它）"
}

$mappedGroups = @{}
$groupSection = [regex]::Match($groupMapText, '(?ms)^groups:\s*$(.*)')
if ($groupSection.Success) {
    foreach ($m in [regex]::Matches($groupSection.Groups[1].Value, '(?m)^  ([A-Za-z0-9_]+):\s*$')) {
        $mappedGroups[$m.Groups[1].Value] = $true
    }
} else {
    $problems += "group_map.yaml 里找不到 groups 段"
}

$dirNames = @($groups | ForEach-Object { $_.Name })
# slash 组本来就不该在 groups 段：它由 [ChatCommand] 经 generate_slash_cli_node.py 生成，
# 没有 MCP 分类可映射。除它之外出现「有工程没登记」都是真漂移。
$groupsOutsideMap = @("slash")
foreach ($missing in @($dirNames | Where-Object { $groupsOutsideMap -notcontains $_ -and -not $mappedGroups.ContainsKey($_) })) {
    $problems += "[$missing] 有分组工程，但 group_map.yaml 的 groups 段没登记"
}
foreach ($orphan in @($mappedGroups.Keys | Where-Object { $dirNames -notcontains $_ })) {
    $problems += "[$orphan] group_map.yaml 登记了分组，但没有对应的分组工程"
}

foreach ($group in $groups) {
    $name = $group.Name

    # ── 声明侧：node.yaml 自身必须自洽（不需要编译，CI 里几秒跑完）─────────
    $yaml = Get-Content -Path (Join-Path $group.FullName "node.yaml") -Raw
    $declaredTotal = [int]([regex]::Match($yaml, 'tool_count:\s*(\d+)').Groups[1].Value)
    $declaredLocalCount = [int]([regex]::Match($yaml, 'local_tool_count:\s*(\d+)').Groups[1].Value)
    $declaredLocal = ([regex]::Matches($yaml, 'mode:\s*local\b')).Count
    $declaredDelegate = ([regex]::Matches($yaml, 'mode:\s*delegate\b')).Count
    $declaredHost = ([regex]::Matches($yaml, 'mode:\s*host\b')).Count
    $declaredExe = [regex]::Match($yaml, '(?m)^exe:\s*(\S+)').Groups[1].Value
    $toolIds = @([regex]::Matches($yaml, '(?m)^\s+- id:\s*(\S+)') | ForEach-Object { $_.Groups[1].Value })
    $entryCount = $toolIds.Count
    $declaredOther = $entryCount - $declaredLocal - $declaredDelegate - $declaredHost

    if ($declaredTotal -ne $entryCount) {
        $problems += "[$name] node.yaml 自相矛盾: tool_count=$declaredTotal 但只有 $entryCount 条 tools 条目"
    }
    if ($declaredLocalCount -ne $declaredLocal) {
        $problems += "[$name] node.yaml 自相矛盾: local_tool_count=$declaredLocalCount 但 mode: local 有 $declaredLocal 条"
    }
    if ($declaredOther -ne 0) {
        $problems += "[$name] node.yaml 有 $declaredOther 条 tools 的 mode 不是 local/delegate/host"
    }
    $duplicateIds = @($toolIds | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    if ($duplicateIds.Count -gt 0) {
        $problems += "[$name] tools 里重复登记了 $($duplicateIds -join ', ')（同名工具会互相覆盖）"
    }
    if ($declaredExe -ne "jcc-tool-$name.exe") {
        $problems += "[$name] node.yaml 的 exe=$declaredExe 与组名对不上（应为 jcc-tool-$name.exe）"
    }
    if ($delegatedInMap.ContainsKey($name) -ne ($declaredDelegate -gt 0)) {
        $problems += if ($declaredDelegate -gt 0) {
            "[$name] node.yaml 有 $declaredDelegate 条 delegate，但 group_map.yaml 的 delegates 段没登记该组"
        } else {
            "[$name] group_map.yaml 的 delegates 段登记了该组，但 node.yaml 里没有 mode: delegate"
        }
    }

    if ($DeclarationsOnly) {
        $rows += [pscustomobject]@{
            Group = $name; Total = $declaredTotal; Local = $declaredLocal
            Delegate = $declaredDelegate; Isolated = $declaredLocal + $declaredDelegate
            Target = ""; HelpKB = 0
        }
        continue
    }

    # ── 现实侧：先编译，再自报 ────────────────────────────────────────
    if (-not $SkipBuild) {
        # 快路径（默认）：依赖产物已就绪，跳过还原与依赖项目重建，单组 20~40 秒。
        # 冷启动（-FromScratch）：干净容器里没有 assets 文件、也没有契约层 DLL，
        # 必须让 MSBuild 自己还原并构建 Abstractions.Primitives.Tools，否则单组直接失败。
        $buildArgs = @(
            "build", (Join-Path $group.FullName ("jcc_tool_" + $name + ".csproj")),
            "-c", $Configuration, "--nologo", "-v", "minimal")
        if (-not $FromScratch) {
            $buildArgs += @("--no-restore", "-p:BuildProjectReferences=false", "-p:EmitCompilerGeneratedFiles=false")
        }
        $build = Invoke-Probe -Exe "dotnet" -Arguments $buildArgs
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

    $selfReport = (Invoke-Probe -Exe $exe -Arguments @("--list-local")).Output
    $reportedLocal = 0
    $match = [regex]::Match($selfReport, 'local_count=(\d+)')
    if ($match.Success) { $reportedLocal = [int]$match.Groups[1].Value }
    $reportedDelegate = if ($selfReport -match 'delegated_to=(\S+)') { $Matches[1] } else { "" }

    if ($reportedLocal -ne $declaredLocal) {
        $problems += "[$name] 自报 local=$reportedLocal 与 node.yaml 声明 $declaredLocal 不一致（实现与登记漂移）"
    }

    # 只比数量不够：把 A 从 local 改成 host、同时把 B 从 host 改成 local，计数一模一样，
    # -h 也不会报错，但「工具实际跑在哪」已经换了地方。所以还要逐 id 比一遍集合。
    # slash 组的登记名带前导斜杠（/version），exe 自报不带，按 CliNodeRunner.CanonicalToolId 同口径归一化。
    $reportedLocalIds = @()
    $idMatch = [regex]::Match($selfReport, 'local=(\S*)')
    if ($idMatch.Success -and $idMatch.Groups[1].Value) {
        $reportedLocalIds = @($idMatch.Groups[1].Value -split ',' |
            Where-Object { $_ } | ForEach-Object { Normalize-ToolId $_ })
    }
    $declaredLocalIds = @([regex]::Matches($yaml, '(?ms)^\s+- id:\s*(\S+)\s*\r?\n\s+mode:\s*local\b') |
        ForEach-Object { Normalize-ToolId $_.Groups[1].Value })

    if ($reportedLocalIds.Count -gt 0) {
        $onlyDeclared = @($declaredLocalIds | Where-Object { $reportedLocalIds -notcontains $_ })
        $onlyReported = @($reportedLocalIds | Where-Object { $declaredLocalIds -notcontains $_ })
        if ($onlyDeclared.Count -gt 0 -or $onlyReported.Count -gt 0) {
            $problems += "[$name] local 工具集合不一致：声明为 local 但 exe 未实现 [$($onlyDeclared -join ', ')]；" +
                "exe 实现了但 node.yaml 登记成别的 mode [$($onlyReported -join ', ')]"
        }
    }

    if ($declaredDelegate -gt 0 -and -not $reportedDelegate) {
        $problems += "[$name] node.yaml 声明了 delegate，但 exe 未自报 delegated_to"
    }
    if ($reportedDelegate -and -not $delegatedInMap.ContainsKey($name)) {
        $problems += "[$name] exe 自报委托到 $reportedDelegate，但 group_map.yaml 没登记"
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
    @{ Group = "file";   Args = @("read", "--file_path", (Join-Path $repoRoot "AGENTS.md"));             Expect = 0 }
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
# ── 首轮提示词索引对账 ──────────────────────────────────────────────
# resources/tools/cli_groups.yaml 由各分组 node.yaml 生成，是 ToolsSection 告诉模型
# 「有哪些独立 exe、各自干什么」的唯一数据源。加组却忘记重新生成时不会有任何报错：
# 新组只是永远不出现在提示词里，模型也就永远发现不了它——这种「静默失明」必须在这一层拦。
$indexPath = Join-Path $repoRoot "resources/tools/cli_groups.yaml"
if (-not (Test-Path $indexPath)) {
    $problems += "首轮提示词索引缺失: $indexPath（用 migrate_mcp_tools_to_cli_nodes.py --index-only 生成）"
} else {
    $indexText = Get-Content -Path $indexPath -Raw -Encoding UTF8
    $indexGroups = @([regex]::Matches(
        $indexText,
        '(?ms)^\s+- id:\s*(\S+)\s*\r?\n\s+exe:\s*(\S+)\s*\r?\n\s+tools:\s*(\d+)\s*\r?\n\s+local:\s*(\d+)\s*\r?\n\s+delegate:\s*(\d+)') |
        ForEach-Object {
            [pscustomobject]@{
                Id = $_.Groups[1].Value; Exe = $_.Groups[2].Value
                Tools = [int]$_.Groups[3].Value; Local = [int]$_.Groups[4].Value
                Delegate = [int]$_.Groups[5].Value
            }
        })
    foreach ($row in $rows) {
        $entry = $indexGroups | Where-Object { $_.Id -eq $row.Group } | Select-Object -First 1
        if (-not $entry) {
            $problems += "[$($row.Group)] 首轮提示词索引缺少该组（模型不知道有这个 exe，等于该组不存在）"
            continue
        }
        if ($entry.Exe -ne "jcc-tool-$($row.Group)") {
            $problems += "[$($row.Group)] 索引里的 exe 名 '$($entry.Exe)' 与装配名 'jcc-tool-$($row.Group)' 不一致"
        }
        if ($entry.Tools -ne $row.Total -or $entry.Local -ne $row.Local -or $entry.Delegate -ne $row.Delegate) {
            # 先把两段拼好再 -f：PowerShell 的 -f 优先级高于 +，写成 "a"+"b" -f x 只会格式化后半段。
            $message = ("[$($row.Group)] 索引数字过时：索引 {0}/{1}/{2}（总/local/delegate），" +
                "node.yaml 为 {3}/{4}/{5} —— 重新生成 cli_groups.yaml")
            $problems += ($message -f $entry.Tools, $entry.Local, $entry.Delegate,
                $row.Total, $row.Local, $row.Delegate)
        }
    }
    foreach ($entry in $indexGroups) {
        if (-not ($rows | Where-Object { $_.Group -eq $entry.Id })) {
            $problems += "[$($entry.Id)] 提示词索引登记了不存在的分组（模型会去敲一个不存在的命令）"
        }
    }
}

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
if ($DeclarationsOnly) {
    $report += ("共 {0} 组 / {1} 个工具，声明隔离 {2}（{3}%），其中 {4} 组走 delegate —— 仅声明侧，未编译未真跑" -f `
        @($rows).Count, $totalTools, $totalIsolated, $percent, $delegateGroups)
} else {
    $report += ("共 {0} 组 / {1} 个工具，进程隔离 {2}（{3}%），其中 {4} 组走 delegate" -f `
        @($rows).Count, $totalTools, $totalIsolated, $percent, $delegateGroups)
}

if ($problems.Count -gt 0) {
    $report += ""
    $report += ("发现 {0} 个问题:" -f $problems.Count)
    $problems | ForEach-Object { $report += "  - $_" }
    Write-Output ($report -join [Environment]::NewLine)
    exit 1
}

$report += if ($DeclarationsOnly) { "声明自洽（node.yaml 与 group_map.yaml 一致）。" } else { "声明与现实一致。" }
Write-Output ($report -join [Environment]::NewLine)
exit 0
