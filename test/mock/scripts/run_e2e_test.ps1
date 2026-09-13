<#
.SYNOPSIS
    Reasonix 移植功能 E2E 集成测试脚本 — 启动 MockServer + jcc.exe 验证端到端链路

.DESCRIPTION
    1. 启动 MockServer（固定端口 9901）
    2. 启动 jcc.exe 连接 MockServer
    3. 发送测试对话
    4. 观察输出，验证链路

.PARAMETER TestName
    测试名称：basic, complete_step, ssrf, session, dual_model, event_stream, all

.PARAMETER Port
    MockServer 端口（默认 9901）

.EXAMPLE
    .\run_e2e_test.ps1 -TestName basic
    .\run_e2e_test.ps1 -TestName complete_step
    .\run_e2e_test.ps1 -TestName all
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("basic", "complete_step", "ssrf", "session", "dual_model", "event_stream", "all")]
    [string]$test_name,

    [int]$port = 9901
)

$error_action_preference = "Stop"

$project_root = resolve-path (join-path $ps_script_root ".." "..")
$mock_server_exe = join-path $project_root "artifacts\bin\OpenAI.MockServer\Release\net10.0\JoinCode.OpenAI.MockServer.exe"
$jcc_exe = join-path $project_root "artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$config_dir = "$env:TEMP\jcc_e2e_$($PID)"

# 验证 exe 存在
if (-not (test-path $mock_server_exe)) {
    write-host "MockServer exe 不存在: $mockServerExe" -foreground_color red
    exit 1
}
if (-not (test-path $jcc_exe)) {
    write-host "jcc.exe 不存在: $jccExe" -foreground_color red
    exit 1
}

# 创建配置目录
new-item -item_type directory -path $config_dir -force | out-null

# === 测试配置 ===

function write-mock_server_config {
    param([string]$config_path, [string]$json_content)
    set-content -path $config_path -value $json_content -encoding utf8
    write-host "  配置文件: $ConfigPath" -foreground_color gray
}

$test_configs = @{
    basic = @{
        name = "基础对话测试"
        config = @"
{
  "port": $port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "你好！我是ai助手，有什么可以帮你的？",
      "follow_up_text": null
    }
  ]
}
"@
        input = "你好"
    }

    complete_step = @{
        name = "P0-1: complete_step 证据门控测试"
        config = @"
{
  "port": $port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": [
        {
          "tool_name": "complete_step",
          "arguments": "{\"step\":\"1. 分析需求\",\"result\":\"需求分析完成\",\"evidence\":[{\"kind\":\"manual\",\"summary\":\"需求文档已审阅\"}]}"
        }
      ],
      "text_response": null,
      "follow_up_text": "第一步已完成：需求分析。"
    }
  ]
}
"@
        input = "完成第一步"
    }

    ssrf = @{
        name = "P0-2: SSRF 防护链路测试"
        config = @"
{
  "port": $port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": [
        {
          "tool_name": "web_fetch",
          "arguments": "{\"url\":\"https://example.com/api/data\"}"
        }
      ],
      "text_response": null,
      "follow_up_text": "网页内容已获取。"
    }
  ]
}
"@
        input = "获取一个网页内容"
    }

    session = @{
        name = "P1-2: SessionController 统一消费测试"
        config = @"
{
  "port": $port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": "让我仔细想想这个问题...",
      "tool_calls": null,
      "text_response": "C# 是一种现代的、面向对象的编程语言，由微软开发。它运行在 .NET 平台上。",
      "follow_up_text": null
    }
  ]
}
"@
        input = "介绍一下C#"
    }

    dual_model = @{
        name = "P1-1: 双模型分离链路测试"
        config = @"
{
  "port": $port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": [
        {
          "tool_name": "bash",
          "arguments": "{\"command\":\"find . -name '*.cs' | head -5\"}"
        }
      ],
      "text_response": null,
      "follow_up_text": "项目包含 5 个 c# 文件。代码质量分析：整体结构清晰，命名规范。"
    }
  ]
}
"@
        input = "分析这个项目的代码质量"
    }

    event_stream = @{
        name = "P1-3: 事件流上下文保持测试"
        config = @"
{
  "port": $port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "好的，我记住了，你的项目名叫 alpha。",
      "follow_up_text": null
    },
    {
      "thinking_content": null,
      "tool_calls": [
        {
          "tool_name": "bash",
          "arguments": "{\"command\":\"ls Alpha/\"}"
        }
      ],
      "text_response": null,
      "follow_up_text": "alpha 项目包含 src、tests、docs 目录。"
    },
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "你的项目名叫 alpha。",
      "follow_up_text": null
    }
  ]
}
"@
        input = "multi_turn"
    }
}

# === 启动 mock_server ===

function start-mock_server {
    param([string]$config_path)

    write-host "`n=== 启动 MockServer ===" -foreground_color cyan
    write-host "  端口: $Port" -foreground_color gray

    $process = start-process -file_path $mock_server_exe -argument_list "--config `"$ConfigPath`" --port $Port" -pass_thru -no_new_window -redirect_standard_output "$configDir\mockserver_stdout.txt" -redirect_standard_error "$configDir\mockserver_stderr.txt"

    # 等待 mock_server 就绪
    $max_wait = 10
    $waited = 0
    while ($waited -lt $max_wait) {
        start-sleep -seconds 1
        $waited++
        try {
            $response = invoke-rest_method -uri "http://localhost:$Port/" -method get -timeout_sec 2 -error_action silently_continue
            if ($response) {
                write-host "  MockServer 就绪 (PID: $($process.Id), 等待 ${waited}s)" -foreground_color green
                return $process
            }
        } catch {
            # 还没就绪
        }
    }

    write-host "  MockServer 启动超时 (${maxWait}s)" -foreground_color red
    if (test-path "$configDir\mockserver_stderr.txt") {
        write-host "  stderr:" -foreground_color yellow
        get-content "$configDir\mockserver_stderr.txt" -tail 5
    }
    return $null
}

# === 运行单个测试 ===

function run-single_test {
    param([string]$test_key)

    $test_config = $test_configs[$test_key]
    if (-not $test_config) {
        write-host "  未知测试: $TestKey" -foreground_color red
        return $false
    }

    write-host "`n=== 运行测试: $($testConfig.Name) ===" -foreground_color cyan

    # 写配置文件
    $config_path = "$configDir\mockserver_$TestKey.json"
    Write-MockServerConfig -ConfigPath $configPath -JsonContent $testConfig.Config

    # 启动 MockServer
    $mockServer = Start-MockServer -ConfigPath $configPath
    if (-not $mockServer) {
        Write-Host "  FAIL: MockServer 启动失败" -ForegroundColor Red
        return $false
    }

    try {
        # 启动 jcc.exe
        Write-Host "`n--- 启动 jcc.exe ---" -ForegroundColor Yellow

                if ($testConfig.Input -eq "multi_turn") {
            # 多轮对话 — 交互模式
            # ⚠️ stdout/stderr 不重定向（避免 pipe 缓冲区死锁，见 docs P3-1）
            # 验证方式：检查 exit code + 输出摘要（文件重定向）
            $outFile = "$configDir\jcc_stdout.txt"
            $err_file = "$configDir\jcc_stderr.txt"

            $psi = [System.Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = $jccExe
            $psi.Arguments = "--trust --force-interactive --await 30"
            $psi.EnvironmentVariables["JCC_ENDPOINT"] = "http://localhost:$Port"
            $psi.EnvironmentVariables["openai_api_key"] = "sk-test-1234567890"
            $psi.EnvironmentVariables["JCC_VENDOR"] = "openai"
            $psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o"
            $psi.UseShellExecute = $false
            $psi.WorkingDirectory = $projectRoot.Path
            $psi.RedirectStandardInput = $true
            # stdout/stderr 不重定向（避免 pipe 死锁），输出到控制台
            $psi.RedirectStandardOutput = $false
            $psi.RedirectStandardError = $false

            $jcc = [System.Diagnostics.Process]::Start($psi)

            # 发送三轮对话
            $inputs = @("我的项目名叫Alpha", "帮我查看项目文件", "我的项目叫什么名字")
            foreach ($input in $inputs) {
                start-sleep -seconds 2
                write-host "  > 发送: $input" -ForegroundColor Gray
                $jcc.StandardInput.WriteLine($input)
                Start-Sleep -Seconds 5
            }

            Start-Sleep -Seconds 2
            $jcc.StandardInput.WriteLine("/exit")
            $jcc.WaitForExit(10000) | Out-Null

            $exitCode = $jcc.ExitCode
            Write-Host "`n--- jcc.exe 退出码: $exitCode ---" -ForegroundColor Yellow

            # 无输出捕获时仅验证 exit code

            Start-Sleep -Seconds 2
            $jcc.StandardInput.WriteLine("/exit")
            $jcc.WaitForExit(10000) | Out-Null

            $stdout = $jcc.StandardOutput.ReadToEnd()
            $stderr = $jcc.StandardError.ReadToEnd()

            Write-Host "`n--- jcc.exe 输出 (前500字符) ---" -ForegroundColor Yellow
            Write-Host $stdout.Substring(0, [Math]::Min(500, $stdout.Length))

            if ($stderr) {
                Write-Host "`n--- stderr ---" -ForegroundColor Yellow
                Write-Host $stderr.Substring(0, [Math]::Min(500, $stderr.Length))
            }

            # 验证
            $passed = $true
            if ($stdout -notmatch "Alpha") {
                write-host "  FAIL: 输出中未找到 'alpha'" -foreground_color red
                $passed = $false
            }
            if ($stdout -match "JsonException|反序列化失败") {
                write-host "  FAIL: 发现 JSON 解析错误" -ForegroundColor Red
                $passed = $false
            }

            return $passed
        }
        else {
            # 单轮对话 — 非交互模式
            $psi = [System.Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = $jccExe
            $psi.Arguments = "--trust -p `"$($testConfig.Input)`" --await 30"
            $psi.EnvironmentVariables["JCC_ENDPOINT"] = "http://localhost:$Port"
            $psi.EnvironmentVariables["openai_api_key"] = "sk-test-1234567890"
            $psi.EnvironmentVariables["JCC_VENDOR"] = "openai"
            $psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o"
            $psi.UseShellExecute = $false
            $psi.WorkingDirectory = $projectRoot.Path
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true

            $jcc = [System.Diagnostics.Process]::Start($psi)
            $jcc.WaitForExit(30000) | Out-Null

            $stdout = $jcc.StandardOutput.ReadToEnd()
            $stderr = $jcc.StandardError.ReadToEnd()

            Write-Host "`n--- jcc.exe 输出 (前500字符) ---" -ForegroundColor Yellow
            Write-Host $stdout.Substring(0, [Math]::Min(500, $stdout.Length))

            if ($stderr) {
                Write-Host "`n--- stderr (前300字符) ---" -ForegroundColor Yellow
                Write-Host $stderr.Substring(0, [Math]::Min(300, $stderr.Length))
            }

            # 验证
            $passed = $true
            if ($jcc.ExitCode -eq 1234) {
                Write-Host "  FAIL: jcc.exe 超时退出 (exit code 1234)" -foreground_color red
                $passed = $false
            }
            if ($stdout -match "JsonException|反序列化失败") {
                write-host "  FAIL: 发现 JSON 解析错误" -ForegroundColor Red
                $passed = $false
            }
            if ($stdout.Length -lt 5) {
                Write-Host "  FAIL: 输出为空或过短" -foreground_color red
                $passed = $false
            }

            return $passed
        }
    }
    finally {
        # 关闭 mock_server
        write-host "`n--- 关闭 MockServer ---" -foreground_color yellow
        try {
            invoke-rest_method -uri "http://localhost:$Port/shutdown" -Method Get -TimeoutSec 3 -ErrorAction SilentlyContinue | Out-Null
        } catch {}
        Start-Sleep -Seconds 1
        if (-not $mockServer.HasExited) {
            $mockServer.Kill()
        }
    }
}

# === 主逻辑 ===

Write-Host "=== Reasonix 移植功能 E2E 集成测试 ===" -foreground_color cyan
write-host "MockServer: $mockServerExe" -foreground_color gray
write-host "jcc.exe:   $jccExe" -foreground_color gray

$results = @{}

if ($test_name -eq "all") {
    foreach ($key in $testConfigs.Keys) {
        $results[$key] = Run-SingleTest -TestKey $key
    }
} else {
    $results[$TestName] = Run-SingleTest -TestKey $TestName
}

# === 汇总 ===

Write-Host "`n========================================" -foreground_color cyan
write-host "E2E 测试结果汇总" -foreground_color cyan
write-host "========================================" -ForegroundColor Cyan

$allPassed = $true
foreach ($key in $results.Keys) {
    $status = if ($results[$key]) { "PASS" } else { "fail" }
    $color = if ($results[$key]) { "green" } else { "red" }
    Write-Host "  $key : $status" -foreground_color $color
    if (-not $results[$key]) { $all_passed = $false }
}

write-host "========================================" -foreground_color cyan

# 清理
remove-item -path $config_dir -recurse -force -error_action silently_continue

if ($all_passed) {
    write-host "全部通过!" -foreground_color green
    exit 0
} else {
    write-host "存在失败!" -ForegroundColor Red
    exit 1
}
