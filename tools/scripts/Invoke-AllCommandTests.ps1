<#
.SYNOPSIS
    覆盖全部 MCP 工具 + 斜杠命令的全参数填充总测试
.DESCRIPTION
    遍历所有 MCP 工具(394) + 斜杠命令(97),对每个:
    1. 获取参数 schema(mcp_schema / slash_schema)
    2. 生成全参数填充(启发式映射 + 可选参数也填)
    3. 调用(mcp_call / slash_call --debuglog)
    4. 记录: ok | isError | 步骤追踪 | 退出原因 | 耗时
    5. 生成 Markdown 报告(含慢命令列表,>10s 需针对性优化)
    > ADR: [0080](docs/adr/0080-manual-exe-testing-guide.md) — 手动测试 exe 功能(非mock操作)
.PARAMETER JccDll
    jcc.dll 路径
.PARAMETER Limit
    限制测试数量(0=全部,N=前N个,用于小规模验证)
.PARAMETER TimeoutSec
    每个工具/命令调用超时秒数
.PARAMETER ReportPath
    报告输出路径
.PARAMETER McpOnly
    只测 MCP 工具
.PARAMETER SlashOnly
    只测斜杠命令
#>
param(
    [string]$JccDll = (Join-Path $PSScriptRoot "..\..\artifacts\bin\JoinCode\Debug\net10.0\jcc.dll"),
    [int]$Limit = 0,
    [int]$TimeoutSec = 30,
    [string]$ReportPath = (Join-Path $PSScriptRoot "..\..\artifacts\reports\command-test-report.md"),
    [switch]$McpOnly,
    [switch]$SlashOnly
)

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not (Test-Path $JccDll)) {
    Write-Error "jcc.dll not found: $JccDll"
    exit 1
}

$JccDll = (Resolve-Path $JccDll).Path
Write-Host "jcc.dll: $JccDll"

$reportDir = Split-Path $ReportPath -Parent
if (-not (Test-Path $reportDir)) { New-Item -ItemType Directory -Force -Path $reportDir | Out-Null }

function Invoke-Jcc {
    param([string[]]$CmdArgs, [int]$Timeout = $TimeoutSec)
    $psi = [System.Diagnostics.ProcessStartInfo]::new("dotnet")
    $psi.Arguments = "`"$JccDll`" " + ($CmdArgs -join " ")
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $psi.WorkingDirectory = (Get-Location).Path

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()

    if (-not $proc.WaitForExit($Timeout * 1000)) {
        try { $proc.Kill() } catch {}
        return @{ stdout = ""; stderr = "[TIMEOUT] 超时 ${Timeout}s"; exitCode = -1; timedOut = $true }
    }

    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    return @{ stdout = $stdout; stderr = $stderr; exitCode = $proc.ExitCode; timedOut = $false }
}

function Extract-Json {
    param([string]$Text)
    $lines = $Text -split "`n"
    for ($i = $lines.Length - 1; $i -ge 0; $i--) {
        $trimmed = $lines[$i].Trim()
        if ($trimmed.StartsWith('{') -or $trimmed.StartsWith('[')) {
            try { return $trimmed | ConvertFrom-Json } catch {}
        }
    }
    $jsonStart = $Text.LastIndexOf('{"')
    if ($jsonStart -ge 0) {
        try { return $Text.Substring($jsonStart) | ConvertFrom-Json } catch {}
    }
    return $null
}

function Generate-ArgValue {
    param([string]$ParamName, [string]$Type, [object[]]$Enum, [string]$Default)

    if ($Enum -and $Enum.Count -gt 0) { return $Enum[0] }
    if ($Default) { return $Default }

    switch ($Type) {
        "integer" { return "1" }
        "boolean" { return "false" }
        "array" { return "[]" }
        "object" { return "{}" }
        "string" {
            switch ($ParamName) {
                "file_path"      { return "test.txt" }
                "path"           { return "test.txt" }
                "directory_path" { return "." }
                "command"        { return "echo hello" }
                "pattern"        { return "*.txt" }
                "query"          { return "test" }
                "url"            { return "http://localhost" }
                "prompt"         { return "test" }
                "code"           { return "var x = 1;" }
                "expression"     { return "1+1" }
                "schema_name"    { return "test_schema" }
                "schema_json"    { return "{}" }
                "action"         { return "list" }
                "target"         { return "test" }
                "confirm"        { return "yes" }
                "agent_pattern"  { return "*" }
                "agent_name"     { return "test" }
                "cassette_name"  { return "test" }
                "client_id"      { return "test" }
                "tool_name"      { return "test" }
                "resource_uri"   { return "test://resource" }
                "auth_id"        { return "test" }
                "api_key"        { return "test-key" }
                "token"          { return "test-token" }
                "username"       { return "test" }
                "password"       { return "test" }
                "refresh_token"  { return "test" }
                "client_secret"  { return "test" }
                "auth_url"       { return "http://localhost/auth" }
                "token_url"      { return "http://localhost/token" }
                "question"       { return "test question" }
                "feedback"       { return "test feedback" }
                "name"           { return "test" }
                "template"       { return "{model}" }
                "flags"          { return "-a" }
                "pr_number"      { return "1" }
                "issue_number"   { return "1" }
                "run_id"         { return "1" }
                "branch"         { return "main" }
                "message"        { return "test" }
                "description"    { return "test" }
                "content"        { return "test" }
                "text"           { return "test" }
                "language"       { return "csharp" }
                "format"         { return "json" }
                "mode"           { return "list" }
                "scope"          { return "all" }
                "level"          { return "info" }
                "category"       { return "general" }
                "tag"            { return "test" }
                "tag_name"       { return "test" }
                "key"            { return "test" }
                "value"          { return "test" }
                "id"             { return "1" }
                "count"          { return "1" }
                "limit"          { return "1" }
                "offset"         { return "0" }
                "timeout"        { return "1000" }
                "duration"       { return "1" }
                "version"        { return "1.0.0" }
                "model"          { return "test" }
                "provider"       { return "test" }
                "filename"       { return "test.txt" }
                "search"         { return "test" }
                "filter"         { return "*" }
                "sort"           { return "name" }
                "order"          { return "asc" }
                "page"           { return "1" }
                "size"           { return "10" }
                "verbose"        { return "false" }
                "dry_run"        { return "false" }
                "force"          { return "false" }
                "recursive"      { return "false" }
                "include"        { return "*" }
                "exclude"        { return "" }
                default          { return "test" }
            }
        }
        default { return "test" }
    }
}

function Generate-FullArgs {
    param([object]$Schema)

    if (-not $Schema -or -not $Schema.properties) { return @() }

    $args = @()
    foreach ($prop in $Schema.properties.PSObject.Properties) {
        $name = $prop.Name
        $info = $prop.Value
        $type = if ($info.type) { $info.type } else { "string" }
        $enum = if ($info.enum) { $info.enum } else { $null }
        $default = if ($info.default) { $info.default } else { $null }

        $value = Generate-ArgValue -ParamName $name -Type $type -Enum $enum -Default $default

        if ($type -eq "integer" -or $type -eq "boolean") {
            $args += "$name=$value"
        } elseif ($type -eq "array" -or $type -eq "object") {
            $args += "$name=$value"
        } else {
            $args += "$name=`"$value`""
        }
    }
    return $args
}

function Extract-Steps {
    param([string]$Stderr)
    $steps = @()
    foreach ($line in $Stderr -split "`n") {
        if ($line -match '\[STEP\]') { $steps += $line.Trim() }
    }
    return $steps
}

function Test-McpTools {
    Write-Host "`n=== MCP Tools ===" -ForegroundColor Cyan

    $listResult = Invoke-Jcc -CmdArgs @("mcp_list", "--json", "--brief")
    $listJson = Extract-Json -Text $listResult.stdout
    if (-not $listJson -or -not $listJson.ok) {
        Write-Error "mcp_list failed"
        return @()
    }

    $tools = $listJson.data
    Write-Host "Found $($tools.Count) MCP tools"

    if ($Limit -gt 0 -and $Limit -lt $tools.Count) {
        $tools = $tools[0..($Limit - 1)]
        Write-Host "Limited to first $Limit tools"
    }

    $results = @()
    $i = 0
    foreach ($tool in $tools) {
        $i++
        Write-Host "  [$i/$($tools.Count)] $tool" -NoNewline

        $schemaResult = Invoke-Jcc -CmdArgs @("mcp_schema", $tool, "--json")
        $schema = $null
        $schemaJson = Extract-Json -Text $schemaResult.stdout
        if ($schemaJson) { $schema = $schemaJson.data }

        $toolArgs = Generate-FullArgs -Schema $schema
        $callArgs = @("mcp_call", $tool) + $toolArgs + @("--json", "--debuglog")
        $cmdStart = [System.Diagnostics.Stopwatch]::StartNew()
        $callResult = Invoke-Jcc -CmdArgs $callArgs -Timeout $TimeoutSec
        $cmdStart.Stop()
        $cmdDuration = [math]::Round($cmdStart.Elapsed.TotalSeconds, 1)

        $isError = $false
        $contentText = ""
        $exitReason = ""

        try {
            $callJson = Extract-Json -Text $callResult.stdout
            if ($callJson) {
                $isError = [bool]$callJson.isError
                if ($callJson.content) {
                    $contentText = ($callJson.content | ForEach-Object { $_.text } | Out-String).Trim()
                }
            }
        } catch {
            $exitReason = "JSON parse failed"
        }

        $steps = Extract-Steps -Stderr $callResult.stderr

        if ($callResult.timedOut) {
            $status = "TIMEOUT"
            $exitReason = "超时 ${TimeoutSec}s"
        } elseif ($callJson -and -not $isError) {
            $status = "OK"
            $exitReason = ""
        } elseif ($callJson -and $isError) {
            $status = "ERROR"
            $exitReason = if ($contentText) { $contentText.Substring(0, [Math]::Min(120, $contentText.Length)) } else { "isError=true" }
        } elseif ($callResult.exitCode -ne 0 -and $callResult.exitCode -ne 210) {
            $status = "CRASH"
            $exitReason = "exit=$($callResult.exitCode)"
        } else {
            $status = "OK"
            $exitReason = ""
        }

        $stepSummary = if ($steps.Count -gt 0) { "$($steps.Count) steps" } else { "no steps" }

        Write-Host " -> $status ($stepSummary) ${cmdDuration}s" -ForegroundColor $(switch ($status) { "OK" { "Green" } "ERROR" { "Yellow" } default { "Red" } })

        $results += [PSCustomObject]@{
            Category = "MCP"
            Name     = $tool
            Status   = $status
            Args     = ($toolArgs -join " ")
            Steps    = $stepSummary
            Reason   = $exitReason
            Content  = $contentText
            Duration = $cmdDuration
        }
    }
    return $results
}

function Test-SlashCommands {
    Write-Host "`n=== Slash Commands ===" -ForegroundColor Cyan

    $listResult = Invoke-Jcc -CmdArgs @("slash_list", "--json")
    $listJson = Extract-Json -Text $listResult.stdout
    if (-not $listJson -or -not $listJson.ok) {
        Write-Error "slash_list failed"
        return @()
    }

    $cmds = $listJson.data | ForEach-Object { $_.name.TrimStart('/') }
    Write-Host "Found $($cmds.Count) slash commands"

    if ($Limit -gt 0 -and $Limit -lt $cmds.Count) {
        $cmds = $cmds[0..($Limit - 1)]
        Write-Host "Limited to first $Limit commands"
    }

    $results = @()
    $i = 0
    foreach ($cmd in $cmds) {
        $i++
        Write-Host "  [$i/$($cmds.Count)] /$cmd" -NoNewline

        $schemaResult = Invoke-Jcc -CmdArgs @("slash_schema", $cmd, "--json")
        $schema = Extract-Json -Text $schemaResult.stdout

        $toolArgs = Generate-FullArgs -Schema $schema
        $callArgs = @("slash_call", $cmd) + $toolArgs + @("--debuglog")
        $cmdStart = [System.Diagnostics.Stopwatch]::StartNew()
        $callResult = Invoke-Jcc -CmdArgs $callArgs -Timeout $TimeoutSec
        $cmdStart.Stop()
        $cmdDuration = [math]::Round($cmdStart.Elapsed.TotalSeconds, 1)

        $contentText = $callResult.stdout.Trim()
        $steps = Extract-Steps -Stderr $callResult.stderr

        if ($callResult.timedOut) {
            $status = "TIMEOUT"
            $exitReason = "超时 ${TimeoutSec}s"
        } elseif ($callResult.exitCode -ne 0) {
            $status = "CRASH"
            $exitReason = "exit=$($callResult.exitCode)"
        } elseif ($contentText -match "用法|usage|未知参数|未初始化|不可用") {
            $status = "SKIP"
            $exitReason = $contentText.Substring(0, [Math]::Min(120, $contentText.Length))
        } else {
            $status = "OK"
            $exitReason = ""
        }

        $stepSummary = if ($steps.Count -gt 0) { "$($steps.Count) steps" } else { "no steps" }

        Write-Host " -> $status ($stepSummary) ${cmdDuration}s" -ForegroundColor $(switch ($status) { "OK" { "Green" } "SKIP" { "DarkGray" } "ERROR" { "Yellow" } default { "Red" } })

        $results += [PSCustomObject]@{
            Category = "Slash"
            Name     = "/$cmd"
            Status   = $status
            Args     = ($toolArgs -join " ")
            Steps    = $stepSummary
            Reason   = $exitReason
            Content  = $contentText
            Duration = $cmdDuration
        }
    }
    return $results
}

$startTime = Get-Date
$allResults = @()

if (-not $SlashOnly) { $allResults += Test-McpTools }
if (-not $McpOnly) { $allResults += Test-SlashCommands }

$duration = (Get-Date) - $startTime

$okCount = ($allResults | Where-Object { $_.Status -eq "OK" }).Count
$errorCount = ($allResults | Where-Object { $_.Status -eq "ERROR" }).Count
$crashCount = ($allResults | Where-Object { $_.Status -eq "CRASH" }).Count
$timeoutCount = ($allResults | Where-Object { $_.Status -eq "TIMEOUT" }).Count
$skipCount = ($allResults | Where-Object { $_.Status -eq "SKIP" }).Count

$report = [System.Text.StringBuilder]::new()
[void]$report.AppendLine("# 命令全参数填充测试报告")
[void]$report.AppendLine()
[void]$report.AppendLine("| 维度 | 值 |")
[void]$report.AppendLine("|------|------|")
[void]$report.AppendLine("| 时间 | $($startTime.ToString('yyyy-MM-dd HH:mm:ss')) |")
[void]$report.AppendLine("| 耗时 | $([math]::Round($duration.TotalSeconds, 1))s |")
[void]$report.AppendLine("| 总数 | $($allResults.Count) |")
[void]$report.AppendLine("| OK | $okCount |")
[void]$report.AppendLine("| ERROR | $errorCount |")
[void]$report.AppendLine("| CRASH | $crashCount |")
[void]$report.AppendLine("| TIMEOUT | $timeoutCount |")
[void]$report.AppendLine("| SKIP | $skipCount |")
[void]$report.AppendLine()
[void]$report.AppendLine("## 详细结果")
[void]$report.AppendLine()
[void]$report.AppendLine("| 类别 | 名称 | 状态 | 耗时(s) | 参数 | 步骤 | 退出原因 |")
[void]$report.AppendLine("|------|------|------|---------|------|------|----------|")

foreach ($r in $allResults) {
    $argsShort = if ($r.Args.Length -gt 60) { $r.Args.Substring(0, 57) + "..." } else { $r.Args }
    $reasonShort = if ($r.Reason.Length -gt 80) { $r.Reason.Substring(0, 77) + "..." } else { $r.Reason }
    [void]$report.AppendLine("| $($r.Category) | $($r.Name) | $($r.Status) | $($r.Duration) | $argsShort | $($r.Steps) | $reasonShort |")
}

$slowCommands = $allResults | Where-Object { $_.Duration -gt 10 } | Sort-Object Duration -Descending
if ($slowCommands) {
    [void]$report.AppendLine()
    [void]$report.AppendLine("## 慢命令(>10s,可针对性优化)")
    [void]$report.AppendLine()
    [void]$report.AppendLine("| 类别 | 名称 | 耗时(s) | 状态 |")
    [void]$report.AppendLine("|------|------|---------|------|")
    foreach ($r in $slowCommands) {
        [void]$report.AppendLine("| $($r.Category) | $($r.Name) | $($r.Duration) | $($r.Status) |")
    }
}

if ($crashCount -gt 0 -or $timeoutCount -gt 0) {
    [void]$report.AppendLine()
    [void]$report.AppendLine("## CRASH/TIMEOUT 详情")
    [void]$report.AppendLine()
    foreach ($r in $allResults | Where-Object { $_.Status -eq "CRASH" -or $_.Status -eq "TIMEOUT" }) {
        [void]$report.AppendLine("### $($r.Category) $($r.Name)")
        [void]$report.AppendLine("- 状态: $($r.Status)")
        [void]$report.AppendLine("- 参数: $($r.Args)")
        [void]$report.AppendLine("- 原因: $($r.Reason)")
        [void]$report.AppendLine("- 输出: $($r.Content.Substring(0, [Math]::Min(500, $r.Content.Length)))")
        [void]$report.AppendLine()
    }
}

$reportText = $report.ToString()
$reportText | Out-File -FilePath $ReportPath -Encoding UTF8

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Total: $($allResults.Count) | OK: $okCount | ERROR: $errorCount | CRASH: $crashCount | TIMEOUT: $timeoutCount | SKIP: $skipCount"
Write-Host "Duration: $([math]::Round($duration.TotalSeconds, 1))s"
Write-Host "Report: $ReportPath"

if ($crashCount -gt 0 -or $timeoutCount -gt 0) { exit 1 } else { exit 0 }
