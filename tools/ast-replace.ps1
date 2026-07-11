<#
.SYNOPSIS
    AST 批量替换入口脚本 — 自带 git 备份与撤回

.DESCRIPTION
    执行 AST 批量替换前自动创建 git 备份点，替换后显示 diff 验证。
    支持一键撤回到备份点。

    用法:
      1. 替换模式:  .\ast-replace.ps1 -Target <csproj|slnx> -Rule <JCC规则ID> [-FixAll] [-DryRun]
      2. 撤回模式:  .\ast-replace.ps1 -Rollback
      3. 查看备份:  .\ast-replace.ps1 -Status

.PARAMETER Target
    目标项目或解决方案路径 (.csproj / .slnx)

.PARAMETER Rule
    要应用的 JCC 规则 ID (如 JCC1001, JCC6002)

.PARAMETER FixAll
    应用该规则的所有修复（不仅是第一个）

.PARAMETER DryRun
    仅显示将要修改的文件，不实际写入

.PARAMETER Rollback
    撤回最近一次 AST 替换（git reset --hard HEAD~1）

.PARAMETER Status
    显示当前 git 状态和最近的 AST 备份点

.EXAMPLE
    .\ast-replace.ps1 -Target JoinCode.slnx -Rule JCC1001 -FixAll
    .\ast-replace.ps1 -Rollback
    .\ast-replace.ps1 -Status
#>
[CmdletBinding()]
param(
    [Parameter(ParameterSetName = "Replace")]
    [string]$Target,

    [Parameter(ParameterSetName = "Replace")]
    [string]$Rule,

    [Parameter(ParameterSetName = "Replace")]
    [switch]$FixAll,

    [Parameter(ParameterSetName = "Replace")]
    [switch]$DryRun,

    [Parameter(ParameterSetName = "Rollback")]
    [switch]$Rollback,

    [Parameter(ParameterSetName = "Status")]
    [switch]$Status
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot

# ── 辅助函数 ──

function Invoke-Git {
    param([string]$Arguments)
    $result = git --no-pager $Arguments 2>&1
    $global:LASTEXITCODE = $LASTEXITCODE
    return $result
}

function Test-GitClean {
    $status = Invoke-Git "status --porcelain"
    return [string]::IsNullOrWhiteSpace($status)
}

function New-GitBackup {
    param([string]$Message)
    Invoke-Git "add -A" | Out-Null
    Invoke-Git "commit --allow-empty -m `"$Message`"" | Out-Null
    $commit = (Invoke-Git "rev-parse --short HEAD")[0]
    Write-Host "  备份点已创建: $commit" -ForegroundColor Green
    return $commit
}

function Find-AuditCli {
    # 优先使用已发布的 exe
    $exePath = Join-Path $rootDir "tools\JccAuditCli\bin\Release\net10.0\jcc-audit.exe"
    if (Test-Path $exePath) {
        return $exePath
    }

    # 回退到 dotnet run
    $csprojPath = Join-Path $rootDir "tools\JccAuditCli\JccAuditCli.csproj"
    if (Test-Path $csprojPath) {
        return "dotnet run --project `"$csprojPath`" -c Release --"
    }

    Write-Error "未找到 jcc-audit 工具。请先运行 build.ps1 构建。"
    exit 1
}

# ── 撤回模式 ──

if ($PSCmdlet.ParameterSetName -eq "Rollback") {
    Write-Host "=== AST 替换撤回 ===" -ForegroundColor Cyan

    # 查找最近的 AST 备份点
    $log = Invoke-Git "log --oneline -20"
    $astCommit = $log | Where-Object { $_ -match "ast-replace:" } | Select-Object -First 1

    if (-not $astCommit) {
        Write-Host "未找到 AST 备份点，无法撤回。" -ForegroundColor Yellow
        exit 1
    }

    $commitHash = ($astCommit -split ' ')[0]
    Write-Host "  找到备份点: $astCommit" -ForegroundColor Yellow
    Write-Host "  即将执行: git reset --hard HEAD~1 (撤回到 $commitHash 之前)" -ForegroundColor Red
    Write-Host "  警告: 这将丢失该备份点之后的所有更改!" -ForegroundColor Red

    $confirm = Read-Host "确认撤回? (y/N)"
    if ($confirm -ne 'y' -and $confirm -ne 'Y') {
        Write-Host "已取消。" -ForegroundColor Yellow
        exit 0
    }

    Invoke-Git "reset --hard HEAD~1" | Out-Null
    Write-Host "  已撤回到备份点之前。" -ForegroundColor Green

    # 显示当前状态
    $currentCommit = (Invoke-Git "rev-parse --short HEAD")[0]
    Write-Host "  当前 HEAD: $currentCommit" -ForegroundColor Cyan
    exit 0
}

# ── 状态模式 ──

if ($PSCmdlet.ParameterSetName -eq "Status") {
    Write-Host "=== AST 替换状态 ===" -ForegroundColor Cyan

    # 当前 git 状态
    $isClean = Test-GitClean
    if ($isClean) {
        Write-Host "  Git 工作区: 干净" -ForegroundColor Green
    } else {
        Write-Host "  Git 工作区: 有未提交更改" -ForegroundColor Yellow
        Invoke-Git "status --short" | ForEach-Object { Write-Host "    $_" }
    }

    # 最近的 AST 备份点
    Write-Host ""
    Write-Host "  最近的 AST 备份点:" -ForegroundColor Cyan
    $log = Invoke-Git "log --oneline -20"
    $astCommits = $log | Where-Object { $_ -match "ast-replace:" }
    if ($astCommits) {
        $astCommits | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" }
    } else {
        Write-Host "    (无)" -ForegroundColor Yellow
    }

    # jcc-audit 工具状态
    Write-Host ""
    $auditCli = Find-AuditCli
    Write-Host "  jcc-audit 工具: $auditCli" -ForegroundColor Cyan
    exit 0
}

# ── 替换模式 ──

if ($PSCmdlet.ParameterSetName -eq "Replace") {
    if (-not $Target) {
        Write-Error "必须指定 -Target 参数 (csproj 或 slnx 路径)"
        exit 1
    }
    if (-not $Rule) {
        Write-Error "必须指定 -Rule 参数 (JCC 规则 ID，如 JCC1001)"
        exit 1
    }

    # 解析目标路径
    $targetPath = if ([System.IO.Path]::IsPathRooted($Target)) {
        $Target
    } else {
        Join-Path $rootDir $Target
    }

    if (-not (Test-Path $targetPath)) {
        Write-Error "目标路径不存在: $targetPath"
        exit 1
    }

    Write-Host "=== AST 批量替换 ===" -ForegroundColor Cyan
    Write-Host "  目标: $targetPath"
    Write-Host "  规则: $Rule"
    if ($FixAll) { Write-Host "  模式: 全部修复" }
    if ($DryRun) { Write-Host "  模式: 仅预览 (DryRun)" }

    # Step 1: 检查 git 工作区
    Write-Host ""
    Write-Host "--- Step 1: 检查 git 工作区 ---" -ForegroundColor Cyan
    $isClean = Test-GitClean
    if (-not $isClean) {
        Write-Host "  工作区有未提交更改，先创建备份点..." -ForegroundColor Yellow
    }

    # Step 2: 创建 git 备份点
    Write-Host ""
    Write-Host "--- Step 2: 创建 git 备份点 ---" -ForegroundColor Cyan
    $backupCommit = New-GitBackup "ast-replace: 备份点 (规则: $Rule, 目标: $(Split-Path $targetPath -Leaf))"

    # Step 3: 运行 jcc-audit replace
    Write-Host ""
    Write-Host "--- Step 3: 执行 AST 替换 ---" -ForegroundColor Cyan
    $auditCli = Find-AuditCli

    $auditArgs = @("replace", $targetPath, "--rule", $Rule)
    if ($FixAll) { $auditArgs += "--fix-all" }
    if ($DryRun) { $auditArgs += "--dry-run" }

    if ($auditCli -match "jcc-audit\.exe$") {
        # 直接运行 exe
        & $auditCli $auditArgs
    } else {
        # dotnet run 模式
        $fullArgs = $auditCli -split ' '
        $remainingArgs = $auditArgs
        & $fullArgs[0] ($fullArgs[1..($fullArgs.Length-1)] + $remainingArgs)
    }

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Host "  AST 替换失败 (退出码: $exitCode)，正在撤回..." -ForegroundColor Red
        Invoke-Git "reset --hard HEAD~1" | Out-Null
        Write-Host "  已撤回到备份点 $backupCommit 之前" -ForegroundColor Yellow
        exit $exitCode
    }

    # Step 4: 显示 diff 验证
    Write-Host ""
    Write-Host "--- Step 4: 替换结果验证 ---" -ForegroundColor Cyan
    $diff = Invoke-Git "diff --stat"
    if ([string]::IsNullOrWhiteSpace($diff)) {
        Write-Host "  无文件变更（规则可能未匹配到任何代码）" -ForegroundColor Yellow
    } else {
        Write-Host "  变更文件:" -ForegroundColor Cyan
        Write-Host $diff

        Write-Host ""
        Write-Host "  详细 diff:" -ForegroundColor Cyan
        Invoke-Git "diff --no-color" | Select-Object -First 200 | ForEach-Object { Write-Host "    $_" }
    }

    # Step 5: 提示后续操作
    Write-Host ""
    Write-Host "--- 完成 ---" -ForegroundColor Green
    Write-Host "  备份点: $backupCommit"
    Write-Host "  如需撤回: .\ast-replace.ps1 -Rollback"
    Write-Host "  如需查看: .\ast-replace.ps1 -Status"
    exit 0
}
