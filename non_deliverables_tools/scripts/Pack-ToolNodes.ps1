<#
.SYNOPSIS
  把 12+1 个工具分组 exe 装配成 jcc 可直接消费的自包含目录布局。

.DESCRIPTION
  产物布局（与 jcc 的解析顺序一致）：
    <TargetDir>/tools/<组>/jcc-tool-<组>.exe
    <TargetDir>/tools/<组>/jcc-tool-<组>.dll + deps.json + runtimeconfig.json
    <TargetDir>/tools/<组>/node.yaml + help.txt

  jcc 侧两种消费方式：
    $env:JCC_CLI_TOOLS_DIR = "<TargetDir>/tools"; jcc tool call <组> <工具> ...
    jcc tool tree --dir "<TargetDir>/tools"

  设计约束：
    * 不删除任何文件（项目规范），只覆盖写入
    * 默认只在缺 exe 时编译该组，已编译的组直接复制 —— 避免每次都等 13 次编译
    * 单个组失败不影响其余组，最后统一汇总失败清单并以非 0 退出

.PARAMETER TargetDir
  装配根目录，默认 artifacts/pack/tool-nodes（已在 artifacts 下，不进版本库）。

.PARAMETER Configuration
  Debug / Release，默认 Debug。

.PARAMETER Groups
  只装配指定分组（如 file,git,slash）；省略表示全部。

.PARAMETER SkipBuild
  完全不编译，只复制已有产物。

.PARAMETER Force
  即使 exe 已存在也重新编译该组。
#>
[CmdletBinding()]
param(
    [string]$TargetDir = "",
    [string]$Configuration = "Debug",
    [string[]]$Groups = @(),
    [switch]$SkipBuild,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path $repoRoot "artifacts/pack/tool-nodes"
}

$groupsRoot = Join-Path $repoRoot "modules/tools/cli_nodes/groups"
if (-not (Test-Path $groupsRoot)) {
    Write-Host "找不到分组目录: $groupsRoot" -ForegroundColor Red
    exit 2
}

$toolsOut = Join-Path $TargetDir "tools"
New-Item -ItemType Directory -Path $toolsOut -Force | Out-Null

$allGroups = Get-ChildItem -Path $groupsRoot -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName ("jcc_tool_" + $_.Name + ".csproj")) } |
    Sort-Object Name

# 委托执行器: group_map.yaml 的 delegates 段声明「某组工具由另一个独立 exe 实现」。
# 这里读同样一份数据源，把被委托的 exe 放到 tools/ 根共享一份（jcc-gh 带依赖 20 MB，
# 按组各放一份不划算）。刻意不引入 YAML 库，格式与 migrate 脚本的解析规则保持一致。
function Read-Delegates {
    param([string]$MapPath)
    $result = @{}
    if (-not (Test-Path $MapPath)) { return $result }
    $section = ""
    $current = ""
    foreach ($line in Get-Content -Path $MapPath) {
        if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -match '^delegates:\s*$') { $section = "delegates"; $current = ""; continue }
        if ($line -match '^\S') { if ($line -notmatch '^delegates:') { $section = "" }; continue }
        if ($section -ne "delegates") { continue }
        if ($line -match '^  (\w+):\s*$') { $current = $Matches[1]; $result[$current] = @{}; continue }
        if ($current -and $line -match '^\s{4,}([\w_]+):\s*(.+)$') {
            $result[$current][$Matches[1]] = $Matches[2].Trim()
        }
    }
    return $result
}

$delegateMap = Read-Delegates -MapPath (Join-Path $repoRoot "resources/tools/group_map.yaml")

if ($Groups.Count -gt 0) {
    $allGroups = $allGroups | Where-Object { $Groups -contains $_.Name }
    if (-not $allGroups) {
        Write-Host "指定的分组都不存在: $($Groups -join ', ')" -ForegroundColor Red
        exit 2
    }
}

$results = @()
$failures = @()

# 委托执行器先落位：分组 exe 启动时会先找同目录、再找上一级目录（此处 = tools/）。
$delegatePlaced = @{}
foreach ($groupName in $delegateMap.Keys) {
    $info = $delegateMap[$groupName]
    $exeStem = $info["exe"]
    if ([string]::IsNullOrWhiteSpace($exeStem) -or $delegatePlaced.ContainsKey($exeStem)) { continue }
    $project = $info["project"]
    if ([string]::IsNullOrWhiteSpace($project)) { $project = $exeStem }
    $srcBin = Join-Path $repoRoot ("artifacts/bin/" + $project + "/" + $Configuration + "/net10.0")
    $srcExe = Join-Path $srcBin ($exeStem + ".exe")
    if (-not (Test-Path $srcExe)) {
        Write-Host ("[delegate-missing] {0}.exe —— 先编译 {1}.csproj（{2}）" -f $exeStem, $project, $srcBin) -ForegroundColor Yellow
        continue
    }
    $payload = Get-ChildItem -Path $srcBin -File |
        Where-Object { $_.Extension -in @(".exe", ".dll", ".json") -and $_.Name -notlike "*.pdb" }
    foreach ($file in $payload) {
        Copy-Item -Path $file.FullName -Destination (Join-Path $toolsOut $file.Name) -Force
    }
    $delegatePlaced[$exeStem] = (Get-ChildItem -Path $srcBin -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("[delegate] {0} -> tools/ ({1} 个文件)" -f $exeStem, $payload.Count) -ForegroundColor DarkCyan
}

foreach ($group in $allGroups) {
    $name = $group.Name
    $proj = Join-Path $group.FullName ("jcc_tool_" + $name + ".csproj")
    $binDir = Join-Path $repoRoot ("artifacts/bin/jcc_tool_" + $name + "/" + $Configuration + "/net10.0")
    $exeName = "jcc-tool-" + $name + ".exe"
    $exePath = Join-Path $binDir $exeName

    $needBuild = -not $SkipBuild -and ($Force -or -not (Test-Path $exePath))
    if ($needBuild) {
        Write-Host ("[build] {0}" -f $name) -ForegroundColor Cyan
        # obj 被清理过时没有 project.assets.json，先按需 restore 一次（只涉及契约层，秒级）
        $assets = Join-Path $repoRoot ("artifacts/obj/jcc_tool_" + $name + "/project.assets.json")
        if (-not (Test-Path $assets)) {
            & dotnet restore $proj --nologo -v quiet 2>&1 | Out-Null
        }
        $out = & dotnet build $proj -c $Configuration --no-restore --nologo -v minimal `
            -p:BuildProjectReferences=false -p:EmitCompilerGeneratedFiles=false 2>&1
        if ($LASTEXITCODE -ne 0) {
            $failures += $name
            Write-Host ("[build-fail] {0}" -f $name) -ForegroundColor Red
            $out | Select-Object -Last 8 | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
            continue
        }
    }

    if (-not (Test-Path $exePath)) {
        $failures += $name
        Write-Host ("[missing] {0} —— exe 不存在，且未要求编译；先编该组 csproj" -f $name) -ForegroundColor Red
        continue
    }

    $dest = Join-Path $toolsOut $name
    New-Item -ItemType Directory -Path $dest -Force | Out-Null

    # 只带运行时真正需要的：apphost + 程序集 + 依赖清单 + 运行时配置
    $payload = Get-ChildItem -Path $binDir -File |
        Where-Object { $_.Extension -in @(".exe", ".dll", ".json") -and $_.Name -notlike "*.pdb" }
    foreach ($file in $payload) {
        Copy-Item -Path $file.FullName -Destination (Join-Path $dest $file.Name) -Force
    }

    # 元数据随 exe 走：jcc tool tree/find 与 exe 的 -h 都读这两个文件
    foreach ($meta in @("node.yaml", "help.txt")) {
        $src = Join-Path $group.FullName $meta
        if (Test-Path $src) {
            Copy-Item -Path $src -Destination (Join-Path $dest $meta) -Force
        }
    }

    $size = (Get-ChildItem -Path $dest -File | Measure-Object -Property Length -Sum).Sum
    $results += [pscustomobject]@{
        Group = $name
        Exe   = $exeName
        Files = (Get-ChildItem -Path $dest -File).Count
        SizeKB = [math]::Round($size / 1KB, 0)
    }
}

Write-Host ""
if ($results.Count -gt 0) {
    $results | Format-Table -AutoSize | Out-String | Write-Host
    $totalKB = ($results | Measure-Object -Property SizeKB -Sum).Sum
    Write-Host ("共 {0} 组，合计 {1} KB，输出: {2}" -f $results.Count, $totalKB, $toolsOut) -ForegroundColor Green
    Write-Host "用法: `$env:JCC_CLI_TOOLS_DIR = '$toolsOut'" -ForegroundColor DarkGray
    Write-Host "      jcc tool tree   # 或 jcc tool call <组> <工具> ..." -ForegroundColor DarkGray
}

if ($failures.Count -gt 0) {
    Write-Host ("失败分组: {0}" -f ($failures -join ", ")) -ForegroundColor Red
    exit 1
}

exit 0
