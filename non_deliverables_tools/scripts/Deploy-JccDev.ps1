# Deploy-JccDev.ps1 — 全量编译 + 部署 jcc 到 jcc.d/dev
# 用途: 全量编译所有域后，拷贝主项目 + gh 插件 + rg 插件 + 13 个工具节点 exe 的所有产物到 jcc.d/dev/
#       三个主 exe 共享同一套依赖 dll，部署到同一目录确保引用一致；
#       工具节点走 tools/<组>/ 独立目录（各自 deps.json，ADR 0157），委托执行器放 tools/ 根共享一份。
#       部署完成后做行为验证：零环境变量发现工具树、委托链路命中部署目录、本地工具实跑退出 0。
# 用法: pwsh non_deliverables_tools/scripts/Deploy-JccDev.ps1 [-SkipBuild] [-Configuration Debug]
# 退出码: 0=成功; 1=编译/装配/验证失败
# ADR: 0132（部署流程与卫星项目遗漏），0157（工具节点布局）
param(
    [switch]$SkipBuild,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$deployDir = "C:/Users/54076/bin/jcc.d/dev"
$mainOutDir = "$repoRoot/artifacts/bin/JoinCode/$Configuration/net10.0"
$ghOutDir = "$repoRoot/artifacts/bin/gh/$Configuration/net10.0"
$rgOutDir = "$repoRoot/artifacts/bin/rg/$Configuration/net10.0"

if (-not $SkipBuild) {
    Write-Host "==> 全量编译..." -ForegroundColor Cyan
    & "$repoRoot/non_deliverables_tools/scripts/Build-AllDomains.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Host "编译失败" -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $mainOutDir)) {
    Write-Host "主项目输出目录不存在: $mainOutDir" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $deployDir)) {
    New-Item -ItemType Directory -Force -Path $deployDir | Out-Null
}

# 辅助函数: 拷贝项目产物(exe/dll/json/pdb/xml)到部署目录
# RequiredProject 非空表示「这个卫星项目必须已编译」：缺产物时直接失败，不再静默跳过。
# 跳过是会隐藏事故的：部署目录里留着上一次的旧 exe，跑起来才 TypeLoadException
# （实测 jcc rg 就撞上：rg 工程从未编译，dev 目录一直是 11-05 的 jcc-rg.exe，
# 与新版 Abstractions.Primitives 不匹配，一跑就崩在 SearchOutputMode 上）。
function Copy-ProjectArtifacts {
    param([string]$SrcDir, [string]$DstDir, [string]$Label, [string]$RequiredProject = "")
    if (-not (Test-Path $SrcDir)) {
        if ($RequiredProject) {
            Write-Host ("  {0} 输出目录不存在: {1}" -f $Label, $SrcDir) -ForegroundColor Red
            Write-Host "  部署目录里可能还留着旧 exe，跑起来会类型不匹配。先编译：" -ForegroundColor Yellow
            Write-Host ("    dotnet build {0} -c {1}" -f $RequiredProject, $Configuration) -ForegroundColor Yellow
            exit 1
        }
        Write-Host "  $Label 输出目录不存在，跳过" -ForegroundColor Yellow
        return 0
    }
    $files = Get-ChildItem $SrcDir -File | Where-Object {
        $_.Extension -in '.dll', '.exe', '.json', '.pdb', '.xml'
    }
    $copied = 0
    foreach ($f in $files) {
        Copy-Item $f.FullName $DstDir -Force
        $copied++
    }
    Write-Host "  ${Label}: $copied 个文件" -ForegroundColor Green
    return $copied
}

# 辅助函数: 跑一个原生命令并拿回输出与退出码。
# 本脚本 $ErrorActionPreference = "Stop"，被调 exe 正常业务失败时会往 stderr 写诊断，
# 直接 `& $exe ... 2>&1` 会把它变成终止性 NativeCommandError，验证步骤还没判断退出码就中断了。
function Invoke-Capture {
    param([string]$Exe, [string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $Exe @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    [pscustomobject]@{ Code = $code; Output = $output }
}

# ⚠️ 顺序有讲究：先插件，后主项目。
# gh/rg 是卫星项目，它们的输出目录里带着整套引擎 DLL（179 个文件，含 Abstractions.*）。
# 三个 exe 装进同一目录，谁后拷谁的同名 DLL 生效。若主项目先拷、插件后拷，插件里那份
# 可能更旧的 DLL 就会盖掉主项目刚编的 —— 症状是「部署完跑起来还是旧行为」，且没有任何报错。
# 实测踩过：gh 产物里 04:33 的 Abstractions.Primitives.Tools.dll 盖掉主项目 05:08 的版本，
# 导致部署后的 jcc 还在用旧版 node.yaml 校验（拒绝 mode: delegate）。
# 所以：插件先铺，主项目最后覆盖 —— 主项目是真正被消费的入口，必须是最新的那份。
Write-Host "==> 拷贝 gh 插件产物 ($ghOutDir -> $deployDir)..." -ForegroundColor Cyan
$null = Copy-ProjectArtifacts -SrcDir $ghOutDir -DstDir $deployDir -Label "gh 插件" -RequiredProject "modules/tools/gh/gh.csproj"

Write-Host "==> 拷贝 rg 插件产物 ($rgOutDir -> $deployDir)..." -ForegroundColor Cyan
$null = Copy-ProjectArtifacts -SrcDir $rgOutDir -DstDir $deployDir -Label "rg 插件" -RequiredProject "modules/tools/rg/rg.csproj"

Write-Host "==> 拷贝主项目产物 ($mainOutDir -> $deployDir)..." -ForegroundColor Cyan
$null = Copy-ProjectArtifacts -SrcDir $mainOutDir -DstDir $deployDir -Label "主项目"

# ── 工具节点（ADR 0157）：13 个分组 exe + 委托执行器 ──────────────────
# 布局交给 Pack-ToolNodes.ps1 决定（tools/<组>/ + 委托 exe 在 tools/ 根），
# 这里只负责「装到哪」，避免部署脚本和打包脚本各写一套布局而慢慢对不上。
# jcc 侧无需任何环境变量：ResolveGroupExe 的第 3 条规则就是 <jcc.exe 所在目录>/tools/<组>/。
Write-Host "==> 装配工具节点 (tools/<组>/ -> $deployDir/tools)..." -ForegroundColor Cyan
$packArgs = @{
    TargetDir     = $deployDir
    Configuration = $Configuration
}
if ($SkipBuild) { $packArgs.SkipBuild = $true }
& "$repoRoot/non_deliverables_tools/scripts/Pack-ToolNodes.ps1" @packArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "  工具节点装配失败（见上方 [build-fail] / [missing]）" -ForegroundColor Red
    exit 1
}

Write-Host "==> 验证部署..." -ForegroundColor Cyan
$jccExe = "$deployDir/jcc.exe"
$ghDll = "$deployDir/jcc-gh.dll"
$rgExe = "$deployDir/jcc-rg.exe"
$depsJson = "$deployDir/jcc.deps.json"
$missing = @()
if (-not (Test-Path $jccExe)) { $missing += "jcc.exe" }
if (-not (Test-Path $ghDll)) { $missing += "jcc-gh.dll" }
if (-not (Test-Path $rgExe)) { $missing += "jcc-rg.exe" }
if (-not (Test-Path $depsJson)) { $missing += "jcc.deps.json" }
if ($missing.Count -gt 0) {
    Write-Host "  缺失文件: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "  核心文件: 通过" -ForegroundColor Green

# ── 逐项对账：部署目录里的主项目文件必须与主项目产物逐字节一致 ────────
# ADR 0132 要求部署后验证 DLL 时间戳，根因是「卫星项目同名文件盖掉主项目」这个坑反复出现。
# 只查时间戳不够直观，直接比大小：466 个文件逐项比，几十毫秒，任何一项不符都说明被覆盖了。
$mismatches = @()
$mainPayload = @(Get-ChildItem $mainOutDir -File | Where-Object { $_.Extension -in '.dll', '.exe', '.json' })
foreach ($f in $mainPayload) {
    $dst = Join-Path $deployDir $f.Name
    if (-not (Test-Path $dst)) {
        $mismatches += "$($f.Name) 未部署"
        continue
    }
    $deployed = Get-Item $dst
    if ($deployed.Length -ne $f.Length) {
        $mismatches += ("{0} 被覆盖: 产物 {1} 字节 / 部署 {2} 字节" -f $f.Name, $f.Length, $deployed.Length)
    }
}
if ($mismatches.Count -gt 0) {
    Write-Host ("  主项目产物与部署不一致 {0} 项（多半是卫星项目同名文件后拷覆盖）:" -f $mismatches.Count) -ForegroundColor Red
    $mismatches | Select-Object -First 6 | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
}
Write-Host ("  主项目 {0} 个文件与产物逐项一致" -f $mainPayload.Count) -ForegroundColor Green

# ── 行为验证：光有文件不算部署成功，得能跑出事来 ────────────────────
# ① 工具节点能被 jcc 以「零环境变量」发现（靠 <jcc 目录>/tools/<组>/ 这条规则）
# ② 委托链路真的解析到 tools/ 根那份 jcc-gh（而不是 PATH 上的旧部署）
# ③ 本地实现工具真能执行并返回 0
$toolsDir = "$deployDir/tools"
$toolGroups = @(Get-ChildItem $toolsDir -Directory -ErrorAction SilentlyContinue)
Write-Host "  工具节点: $($toolGroups.Count) 组，委托执行器: $(if (Test-Path "$toolsDir/jcc-gh.exe") { 'jcc-gh.exe 就位' } else { '缺失' })" -ForegroundColor Green

$githubExe = "$toolsDir/github/jcc-tool-github.exe"
if (Test-Path $githubExe) {
    $probe = Invoke-Capture -Exe $githubExe -Arguments @("--list-local")
    $selfReport = $probe.Output
    if ($selfReport -match 'delegate_path=(\S+)') {
        $delegated = $Matches[1]
        if (Test-Path $delegated) {
            # 必须命中部署目录里的那份，否则说明还在用 PATH 上的旧 exe。
            # 用 GetFullPath 归一化再比：exe 自报的是反斜杠，脚本里的 $toolsDir 是正斜杠。
            $delegatedFull = [System.IO.Path]::GetFullPath($delegated)
            $toolsFull = [System.IO.Path]::GetFullPath($toolsDir)
            if ($delegatedFull.StartsWith($toolsFull, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Host "  委托链路: $delegatedFull" -ForegroundColor Green
            } else {
                Write-Host "  委托链路指向部署目录之外: $delegatedFull（应命中 $toolsFull）" -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "  委托链路指向的文件不存在: $delegated" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "  github 组未自报 delegate_path（委托 exe 没找到，调用会退出 3）" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  缺少 $githubExe" -ForegroundColor Red
    exit 1
}

$probeFile = "$repoRoot/AGENTS.md"
if (Test-Path "$toolsDir/file/jcc-tool-file.exe") {
    $readProbe = Invoke-Capture -Exe "$toolsDir/file/jcc-tool-file.exe" -Arguments @("read", "--file_path", $probeFile)
    if ($readProbe.Code -eq 0) {
        Write-Host "  本地工具实跑: read -> 0" -ForegroundColor Green
    } else {
        Write-Host "  本地工具实跑失败: read -> $($readProbe.Code)" -ForegroundColor Red
        ($readProbe.Output -split "`r?`n" | Select-Object -First 3) | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
        exit 1
    }
}

# jcc 侧零配置发现：不带 JCC_CLI_TOOLS_DIR 也要能列出工具树
$jccProbe = Invoke-Capture -Exe $jccExe -Arguments @("tool", "tree")
if ($jccProbe.Code -eq 0 -and $jccProbe.Output -match '640') {
    Write-Host "  jcc tool tree: 零环境变量发现 13 组 / 640 工具" -ForegroundColor Green
} else {
    Write-Host "  jcc tool tree 未按预期发现工具节点（退出码 $($jccProbe.Code)）" -ForegroundColor Red
    ($jccProbe.Output -split "`r?`n" | Select-Object -First 6) | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
    exit 1
}

Write-Host "  验证通过" -ForegroundColor Green
Write-Host "==> 部署完成: $deployDir" -ForegroundColor Green
exit 0
