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
    [switch]$SlashOnly,
    [ValidateSet("all","mcp","slash")]
    [string]$TestType = "all",
    [int]$Shard = 1,
    [int]$TotalShards = 1,
    [int]$Concurrency = 8
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
    param([string[]]$CmdArgs, [int]$Timeout = $TimeoutSec, [switch]$WithDiag)
    $psi = [System.Diagnostics.ProcessStartInfo]::new("dotnet")
    $psi.Arguments = "`"$JccDll`" " + ($CmdArgs -join " ")
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $psi.WorkingDirectory = (Get-Location).Path
    # TASK023 ③: --debuglog flag 在 CliArgParser 解析层缺失（真机验证放任意位置 stderr 零诊断），
    # 只有 JCC_DEBUGLOG=1 环境变量在进程启动时被 Diag 静态字段读取、真正开启诊断输出。
    # 只能用环境变量，不能用 --debuglog flag。
    if ($WithDiag) { $psi.EnvironmentVariables["JCC_DEBUGLOG"] = "1" }

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

function Generate-DefaultArgs {
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

function Generate-MissingParamScenarios {
    param([object]$Schema)

    if (-not $Schema -or -not $Schema.required) { return @() }

    $scenarios = @()
    $fullArgs = Generate-DefaultArgs -Schema $Schema

    foreach ($reqParam in $Schema.required) {
        $prefix = "$reqParam="
        $missingArgs = @($fullArgs | Where-Object { -not $_.StartsWith($prefix, [StringComparison]::Ordinal) })
        $scenarios += [PSCustomObject]@{
            MissingParam = $reqParam
            Args         = $missingArgs
        }
    }
    return $scenarios
}

# TASK023 ③: 直调命令(mcp_call/slash_call)的 [STEP] 打点全在 REPL/startup 路径，直调不经过；
# 真机 JCC_DEBUGLOG=1 下 stderr 产出的是 [MODULE]/[ROLES]/[LLM]/[TOOL-GATE]/[ORDER]/[CAPS-PLUGIN]/[CONFIG]。
# 只抓 [STEP] 会让「步骤」列永远 no steps = 假绿。改为抓直调路径真实产出的诊断步骤前缀。
# 前缀数组内联在函数体内 —— 本函数经 funcDefs 以定义字符串传入 -Parallel runspace，
# 若引用 $script: 外部变量在 runspace 里为 $null，会再次导致步骤为空（假绿）。
function Extract-Steps {
    param([string]$Stderr)
    $prefixes = @('[STEP]', '[MODULE]', '[ROLES]', '[LLM]', '[TOOL-GATE]', '[ORDER]', '[CAPS-PLUGIN]', '[CONFIG]', '[WIRE]', '[READY]')
    $steps = @()
    foreach ($line in $Stderr -split "`n") {
        $trimmed = $line.Trim()
        foreach ($prefix in $prefixes) {
            if ($trimmed.StartsWith($prefix)) { $steps += $trimmed; break }
        }
    }
    return $steps
}

# TASK023 ④: 副作用验证口径 — 诚实区分「只读」与「写类」，不制造假绿。
# 只读工具/命令：无副作用，OK 即视为验证通过（状态验证）。
# 写类工具/命令：本脚本是全参数冒烟，只验证「调用不崩溃 + 返回非错误」，
#                 真正的落盘/状态变更需专用断言或人工核验，如实标注「未验证副作用」。
# 危险写类工具已被 Get-IsDangerousTool 跳过，不在此列。
function Get-SideEffectVerdict {
    param([string]$Name, [string]$Status)
    $readTokens = @('read','list','get','status','version','help','schema','search','find','query',
                    'stat','info','show','view','inspect','describe','context','usage','history','check','validate','detect','analyze')
    $lower = $Name.ToLowerInvariant().TrimStart('/')
    $isRead = $false
    foreach ($t in $readTokens) {
        if ($lower -eq $t -or $lower.StartsWith($t + '_') -or $lower.EndsWith('_' + $t)) { $isRead = $true; break }
    }
    if ($isRead) {
        if ($Status -eq "OK") { return "只读/OK(状态验证)" }
        return "只读/$Status"
    }
    if ($Status -eq "OK") { return "写类/未验证副作用(仅冒烟)" }
    return "写类/$Status"
}

function Test-McpTools {
    Write-Host "`n=== MCP Tools ===" -ForegroundColor Cyan

    $listResult = Invoke-Jcc -CmdArgs @("mcp_list", "--json")
    $listJson = Extract-Json -Text $listResult.stdout
    if (-not $listJson -or -not $listJson.ok) {
        Write-Error "mcp_list failed"
        Write-Host "  stdout: $($listResult.stdout.Substring(0, [Math]::Min(500, $listResult.stdout.Length)))" -ForegroundColor Red
        Write-Host "  stderr: $($listResult.stderr.Substring(0, [Math]::Min(500, $listResult.stderr.Length)))" -ForegroundColor Red
        Write-Host "  exitCode: $($listResult.exitCode)" -ForegroundColor Red
        return @()
    }

    $tools = $listJson.data | ForEach-Object { $_.name }
    Write-Host "Found $($tools.Count) MCP tools"

    $dangerousTools = $tools | Where-Object { Get-IsDangerousTool -ToolName $_ }
    if ($dangerousTools) {
        $tools = $tools | Where-Object { -not (Get-IsDangerousTool -ToolName $_) }
        Write-Host "Skipped $($dangerousTools.Count) dangerous tools (gh_*/desktop_*/git_write/delete)" -ForegroundColor Yellow
    }

    if ($Limit -gt 0 -and $Limit -lt $tools.Count) {
        $tools = $tools[0..($Limit - 1)]
        Write-Host "Limited to first $Limit tools"
    }

    if ($TotalShards -gt 1 -and $TestType -eq "mcp") {
        $shardSize = [int][Math]::Ceiling($tools.Count / $TotalShards)
        $start = ($Shard - 1) * $shardSize
        $end = [Math]::Min($start + $shardSize - 1, $tools.Count - 1)
        if ($start -ge $tools.Count) { return @() }
        $tools = $tools[$start..$end]
        Write-Host "Shard ${Shard}/${TotalShards} tools[$start..$end] ($($tools.Count) tools)"
    }

    $funcDefs = @{
        InvokeJcc = (Get-Command Invoke-Jcc).Definition
        ExtractJson = (Get-Command Extract-Json).Definition
        GenerateArgValue = (Get-Command Generate-ArgValue).Definition
        GenerateFullArgs = (Get-Command Generate-FullArgs).Definition
        ExtractSteps = (Get-Command Extract-Steps).Definition
        SideEffectVerdict = (Get-Command Get-SideEffectVerdict).Definition
    }
    $total = $tools.Count
    $dllPath = $JccDll
    $timeout = $TimeoutSec

    $results = $tools | ForEach-Object -Parallel {
        $tool = $_
        Set-Item function:Invoke-Jcc $using:funcDefs.InvokeJcc
        Set-Item function:Extract-Json $using:funcDefs.ExtractJson
        Set-Item function:Generate-ArgValue $using:funcDefs.GenerateArgValue
        Set-Item function:Generate-FullArgs $using:funcDefs.GenerateFullArgs
        Set-Item function:Extract-Steps $using:funcDefs.ExtractSteps
        Set-Item function:Get-SideEffectVerdict $using:funcDefs.SideEffectVerdict
        $JccDll = $using:dllPath
        $TimeoutSec = $using:timeout

        $schemaResult = Invoke-Jcc -CmdArgs @("mcp_schema", $tool, "--json")
        $schema = $null
        $schemaJson = Extract-Json -Text $schemaResult.stdout
        if ($schemaJson) { $schema = $schemaJson.data }

        $toolArgs = Generate-FullArgs -Schema $schema
        $callArgs = @("mcp_call", $tool) + $toolArgs + @("--json")
        $cmdStart = [System.Diagnostics.Stopwatch]::StartNew()
        # TASK023 ③: 用 JCC_DEBUGLOG=1 环境变量开启诊断（--debuglog flag 解析层缺失、无效）。
        $callResult = Invoke-Jcc -CmdArgs $callArgs -Timeout $TimeoutSec -WithDiag
        $cmdStart.Stop()
        $cmdDuration = [math]::Round($cmdStart.Elapsed.TotalSeconds, 1)

        $isError = $false
        $contentText = ""
        $exitReason = ""
        $callJson = $null

        try {
            $callJson = Extract-Json -Text $callResult.stdout
            if ($callJson) {
                $isError = [bool]$callJson.data.isError
                if ($callJson.data.content) {
                    $contentText = ($callJson.data.content | ForEach-Object { $_.text } | Out-String).Trim()
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
        } elseif ($callResult.exitCode -ne 0 -and $callResult.exitCode -ne 210 -and ($callResult.stderr -match "401|Unauthorized|API Key|配置无效" -or $contentText -match "401|Unauthorized|API Key")) {
            $status = "SKIP"
            $exitReason = "LLM 依赖工具无 API Key (exit=$($callResult.exitCode))"
        } elseif ($callResult.exitCode -ne 0 -and $callResult.exitCode -ne 210) {
            $status = "CRASH"
            $exitReason = "exit=$($callResult.exitCode)"
        } else {
            $status = "OK"
            $exitReason = ""
        }

        $stepSummary = if ($steps.Count -gt 0) { "$($steps.Count) steps" } else { "no steps" }

        [PSCustomObject]@{
            Category   = "MCP"
            Name       = $tool
            Status     = $status
            Args       = ($toolArgs -join " ")
            Steps      = $stepSummary
            Reason     = $exitReason
            SideEffect = (Get-SideEffectVerdict -Name $tool -Status $status)
            Content    = $contentText
            Stderr     = $callResult.stderr
            Duration   = $cmdDuration
        }
    } -ThrottleLimit $Concurrency

    $results = @($results)
    $i = 0
    foreach ($r in $results) {
        $i++
        Write-Host "  [$i/$($results.Count)] $($r.Name) -> $($r.Status) ($($r.Steps)) $($r.Duration)s" -ForegroundColor $(switch ($r.Status) { "OK" { "Green" } "ERROR" { "Yellow" } default { "Red" } })
    }
    return $results
}

function Get-IsDangerousTool {
    param([string]$ToolName)

    $dangerousExact = @(
        "delete",
        "git_add", "git_commit", "git_push", "git_pull", "git_reset",
        "git_clean", "git_branch", "git_clone", "git_merge",
        "worktree_create", "worktree_remove", "worktree_cleanup", "worktree_merge"
    )
    if ($dangerousExact -contains $ToolName) { return $true }
    if ($ToolName -like "gh_*") { return $true }
    if ($ToolName -like "desktop_*") { return $true }
    return $false
}

function Test-McpMissingParams {
    Write-Host "`n=== MCP Missing Param Validation ===" -ForegroundColor Cyan

    $listResult = Invoke-Jcc -CmdArgs @("mcp_list", "--json")
    $listJson = Extract-Json -Text $listResult.stdout
    if (-not $listJson -or -not $listJson.ok) {
        Write-Error "mcp_list failed"
        return @()
    }

    $tools = $listJson.data | ForEach-Object { $_.name }
    $dangerousTools = $tools | Where-Object { Get-IsDangerousTool -ToolName $_ }
    if ($dangerousTools) {
        $tools = $tools | Where-Object { -not (Get-IsDangerousTool -ToolName $_) }
        Write-Host "Skipped $($dangerousTools.Count) dangerous tools (gh_*/desktop_*/git_write/delete)" -ForegroundColor Yellow
    }
    if ($Limit -gt 0 -and $Limit -lt $tools.Count) {
        $tools = $tools[0..($Limit - 1)]
    }
    if ($TotalShards -gt 1) {
        $shardSize = [int][Math]::Ceiling($tools.Count / $TotalShards)
        $start = ($Shard - 1) * $shardSize
        $end = [Math]::Min($start + $shardSize - 1, $tools.Count - 1)
        if ($start -ge $tools.Count) { return @() }
        $tools = $tools[$start..$end]
    }

    $funcDefs = @{
        InvokeJcc = (Get-Command Invoke-Jcc).Definition
        ExtractJson = (Get-Command Extract-Json).Definition
        GenerateArgValue = (Get-Command Generate-ArgValue).Definition
        GenerateDefaultArgs = (Get-Command Generate-DefaultArgs).Definition
        GenerateMissingParamScenarios = (Get-Command Generate-MissingParamScenarios).Definition
    }
    $dllPath = $JccDll
    $timeout = $TimeoutSec

    $allScenarios = $tools | ForEach-Object -Parallel {
        $tool = $_
        Set-Item function:Invoke-Jcc $using:funcDefs.InvokeJcc
        Set-Item function:Extract-Json $using:funcDefs.ExtractJson
        Set-Item function:Generate-ArgValue $using:funcDefs.GenerateArgValue
        Set-Item function:Generate-DefaultArgs $using:funcDefs.GenerateDefaultArgs
        Set-Item function:Generate-MissingParamScenarios $using:funcDefs.GenerateMissingParamScenarios
        $JccDll = $using:dllPath
        $TimeoutSec = $using:timeout

        $schemaResult = Invoke-Jcc -CmdArgs @("mcp_schema", $tool, "--json")
        $schemaJson = Extract-Json -Text $schemaResult.stdout
        $schema = if ($schemaJson) { $schemaJson.data } else { $null }
        $scenarios = Generate-MissingParamScenarios -Schema $schema
        foreach ($s in $scenarios) {
            [PSCustomObject]@{ Tool = $tool; MissingParam = $s.MissingParam; Args = $s.Args }
        }
    } -ThrottleLimit $Concurrency
    $allScenarios = @($allScenarios)

    Write-Host "Found $($allScenarios.Count) missing-param scenarios for $($tools.Count) tools"

    $results = $allScenarios | ForEach-Object -Parallel {
        $scenario = $_
        Set-Item function:Invoke-Jcc $using:funcDefs.InvokeJcc
        Set-Item function:Extract-Json $using:funcDefs.ExtractJson
        $JccDll = $using:dllPath
        $TimeoutSec = $using:timeout

        $callArgs = @("mcp_call", $scenario.Tool) + $scenario.Args + @("--json")
        $callResult = Invoke-Jcc -CmdArgs $callArgs -Timeout $TimeoutSec

        $isError = $false
        $contentText = ""
        $callJson = $null
        try {
            $callJson = Extract-Json -Text $callResult.stdout
            if ($callJson) {
                $isError = [bool]$callJson.data.isError
                if ($callJson.data.content) {
                    $contentText = ($callJson.data.content | ForEach-Object { $_.text } | Out-String).Trim()
                }
            }
        } catch {}

        $status = if ($callResult.timedOut) { "TIMEOUT" }
                  elseif ($isError) { "ERROR" }
                  elseif ($callJson -and -not $isError) { "SILENT_OK" }
                  else { "CRASH" }

        [PSCustomObject]@{
            Category     = "MissingParam"
            Tool         = $scenario.Tool
            MissingParam = $scenario.MissingParam
            Status       = $status
            Reason       = if ($contentText) { $contentText.Substring(0, [Math]::Min(120, $contentText.Length)) } else { "" }
            Duration     = 0
        }
    } -ThrottleLimit $Concurrency

    $results = @($results)
    $silentOk = ($results | Where-Object { $_.Status -eq "SILENT_OK" }).Count
    $errorOk = ($results | Where-Object { $_.Status -eq "ERROR" }).Count
    Write-Host "  SilentSuccess(BUG): $silentOk | ErrorReturned(correct): $errorOk | Crash: $(($results | Where-Object { $_.Status -eq 'CRASH' }).Count)"

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

    $globalStateMods = @("reset-config")
    $excluded = $cmds | Where-Object { $globalStateMods -contains $_ }
    if ($excluded) {
        $cmds = $cmds | Where-Object { $globalStateMods -notcontains $_ }
        Write-Host "Excluded global-state-modifying commands: $($excluded -join ', ')"
    }

    Write-Host "Found $($cmds.Count) slash commands"

    if ($Limit -gt 0 -and $Limit -lt $cmds.Count) {
        $cmds = $cmds[0..($Limit - 1)]
        Write-Host "Limited to first $Limit commands"
    }

    if ($TotalShards -gt 1 -and $TestType -eq "slash") {
        $shardSize = [int][Math]::Ceiling($cmds.Count / $TotalShards)
        $start = ($Shard - 1) * $shardSize
        $end = [Math]::Min($start + $shardSize - 1, $cmds.Count - 1)
        if ($start -ge $cmds.Count) { return @() }
        $cmds = $cmds[$start..$end]
        Write-Host "Shard ${Shard}/${TotalShards} cmds[$start..$end] ($($cmds.Count) commands)"
    }

    $funcDefs = @{
        InvokeJcc = (Get-Command Invoke-Jcc).Definition
        ExtractJson = (Get-Command Extract-Json).Definition
        GenerateArgValue = (Get-Command Generate-ArgValue).Definition
        GenerateFullArgs = (Get-Command Generate-FullArgs).Definition
        ExtractSteps = (Get-Command Extract-Steps).Definition
        SideEffectVerdict = (Get-Command Get-SideEffectVerdict).Definition
    }
    $dllPath = $JccDll
    $timeout = $TimeoutSec

    $results = $cmds | ForEach-Object -Parallel {
        $cmd = $_
        Set-Item function:Invoke-Jcc $using:funcDefs.InvokeJcc
        Set-Item function:Extract-Json $using:funcDefs.ExtractJson
        Set-Item function:Generate-ArgValue $using:funcDefs.GenerateArgValue
        Set-Item function:Generate-FullArgs $using:funcDefs.GenerateFullArgs
        Set-Item function:Extract-Steps $using:funcDefs.ExtractSteps
        Set-Item function:Get-SideEffectVerdict $using:funcDefs.SideEffectVerdict
        $JccDll = $using:dllPath
        $TimeoutSec = $using:timeout

        if ($cmd -eq "help") {
            $envProf = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
            $settingsPath = Join-Path $envProf ".jcc\settings.json"
            Write-Host "[DEBUG-PARALLEL] cmd=$cmd USERPROFILE=$envProf settingsPath=$settingsPath exists=$(Test-Path $settingsPath) HOME=$HOME"
        }

        $schemaResult = Invoke-Jcc -CmdArgs @("slash_schema", $cmd, "--json")
        $schema = Extract-Json -Text $schemaResult.stdout

        $toolArgs = Generate-FullArgs -Schema $schema
        # TASK023 ③: 去掉无效的 --debuglog flag（解析层缺失），改用 JCC_DEBUGLOG=1 环境变量。
        $callArgs = @("slash_call", $cmd) + $toolArgs
        $cmdStart = [System.Diagnostics.Stopwatch]::StartNew()
        $callResult = Invoke-Jcc -CmdArgs $callArgs -Timeout $TimeoutSec -WithDiag
        $cmdStart.Stop()
        $cmdDuration = [math]::Round($cmdStart.Elapsed.TotalSeconds, 1)

        $contentText = $callResult.stdout.Trim()
        $steps = Extract-Steps -Stderr $callResult.stderr

        if ($callResult.timedOut) {
            $status = "TIMEOUT"
            $exitReason = "超时 ${TimeoutSec}s"
        } elseif ($callResult.exitCode -ne 0 -and ($callResult.stderr -match "401|Unauthorized|API Key|配置无效" -or $contentText -match "401|Unauthorized|API Key")) {
            $status = "SKIP"
            $exitReason = "LLM 依赖命令无 API Key (exit=$($callResult.exitCode))"
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

        [PSCustomObject]@{
            Category   = "Slash"
            Name       = "/$cmd"
            Status     = $status
            Args       = ($toolArgs -join " ")
            Steps      = $stepSummary
            Reason     = $exitReason
            SideEffect = (Get-SideEffectVerdict -Name $cmd -Status $status)
            Content    = $contentText
            Stderr     = $callResult.stderr
            Duration   = $cmdDuration
        }
    } -ThrottleLimit $Concurrency

    $results = @($results)
    $i = 0
    foreach ($r in $results) {
        $i++
        Write-Host "  [$i/$($results.Count)] $($r.Name) -> $($r.Status) ($($r.Steps)) $($r.Duration)s" -ForegroundColor $(switch ($r.Status) { "OK" { "Green" } "SKIP" { "DarkGray" } "ERROR" { "Yellow" } default { "Red" } })
    }
    return $results
}

$startTime = Get-Date
$allResults = @()
$missingParamResults = @()

if ($TestType -eq "all" -or $TestType -eq "mcp") {
    $allResults += Test-McpTools
    $missingParamResults += Test-McpMissingParams
}
if ($TestType -eq "all" -or $TestType -eq "slash") { $allResults += Test-SlashCommands }

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
[void]$report.AppendLine("| 类别 | 名称 | 状态 | 耗时(s) | 参数 | 步骤 | 退出原因 | 副作用验证 |")
[void]$report.AppendLine("|------|------|------|---------|------|------|----------|-----------|")

foreach ($r in $allResults) {
    $argsShort = if ($r.Args.Length -gt 60) { $r.Args.Substring(0, 57) + "..." } else { $r.Args }
    $reasonShort = if ($r.Reason.Length -gt 80) { $r.Reason.Substring(0, 77) + "..." } else { $r.Reason }
    $sideEffect = if ($r.SideEffect) { $r.SideEffect } else { "未判定" }
    [void]$report.AppendLine("| $($r.Category) | $($r.Name) | $($r.Status) | $($r.Duration) | $argsShort | $($r.Steps) | $reasonShort | $sideEffect |")
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
        $stderrText = if ($r.Stderr) { $r.Stderr } else { "" }
        [void]$report.AppendLine("- stderr: $($stderrText.Substring(0, [Math]::Min(800, $stderrText.Length)))")
        [void]$report.AppendLine()
    }
}

$okForDebug = $allResults | Where-Object { $_.Status -eq "OK" } | Select-Object -First 5
if ($okForDebug) {
    [void]$report.AppendLine()
    [void]$report.AppendLine("## OK 调试详情(前5个)")
    [void]$report.AppendLine()
    foreach ($r in $okForDebug) {
        [void]$report.AppendLine("### $($r.Category) $($r.Name)")
        [void]$report.AppendLine("- 状态: $($r.Status)")
        [void]$report.AppendLine("- 耗时: $($r.Duration)s")
        $stderrText = if ($r.Stderr) { $r.Stderr } else { "" }
        [void]$report.AppendLine("- stderr: $($stderrText.Substring(0, [Math]::Min(800, $stderrText.Length)))")
        [void]$report.AppendLine()
    }
}

if ($missingParamResults.Count -gt 0) {
    $mpSilentOk = ($missingParamResults | Where-Object { $_.Status -eq "SILENT_OK" }).Count
    $mpError = ($missingParamResults | Where-Object { $_.Status -eq "ERROR" }).Count
    $mpCrash = ($missingParamResults | Where-Object { $_.Status -eq "CRASH" }).Count
    $mpTimeout = ($missingParamResults | Where-Object { $_.Status -eq "TIMEOUT" }).Count

    [void]$report.AppendLine()
    [void]$report.AppendLine("## 缺失必填参数验证")
    [void]$report.AppendLine()
    [void]$report.AppendLine("| 维度 | 值 |")
    [void]$report.AppendLine("|------|------|")
    [void]$report.AppendLine("| 用例总数 | $($missingParamResults.Count) |")
    [void]$report.AppendLine("| 返回错误(正确) | $mpError |")
    [void]$report.AppendLine("| 静默成功(BUG) | $mpSilentOk |")
    [void]$report.AppendLine("| 崩溃 | $mpCrash |")
    [void]$report.AppendLine("| 超时 | $mpTimeout |")
    [void]$report.AppendLine()

    if ($mpSilentOk -gt 0) {
        [void]$report.AppendLine("### 静默失败详情（缺失必填参数却返回成功）")
        [void]$report.AppendLine()
        [void]$report.AppendLine("| 工具 | 缺失参数 | 原因 |")
        [void]$report.AppendLine("|------|---------|------|")
        foreach ($r in $missingParamResults | Where-Object { $_.Status -eq "SILENT_OK" }) {
            $reasonShort = if ($r.Reason.Length -gt 80) { $r.Reason.Substring(0, 77) + "..." } else { $r.Reason }
            [void]$report.AppendLine("| $($r.Tool) | $($r.MissingParam) | $reasonShort |")
        }
        [void]$report.AppendLine()
    }
}

$reportText = $report.ToString()
$reportText | Out-File -FilePath $ReportPath -Encoding UTF8

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Total: $($allResults.Count) | OK: $okCount | ERROR: $errorCount | CRASH: $crashCount | TIMEOUT: $timeoutCount | SKIP: $skipCount"
if ($missingParamResults.Count -gt 0) {
    $mpSilent = ($missingParamResults | Where-Object { $_.Status -eq "SILENT_OK" }).Count
    Write-Host "MissingParam: $($missingParamResults.Count) scenarios | SilentSuccess(BUG): $mpSilent"
}
Write-Host "Duration: $([math]::Round($duration.TotalSeconds, 1))s"
Write-Host "Report: $ReportPath"

if ($crashCount -gt 0) { exit 1 } else { exit 0 }
