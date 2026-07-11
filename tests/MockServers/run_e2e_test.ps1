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
    [string]$TestName,

    [int]$Port = 9901
)

$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot ".." "..")
$mockServerExe = Join-Path $projectRoot "artifacts\bin\OpenAI.MockServer\Release\net10.0\JoinCode.OpenAI.MockServer.exe"
$jccExe = Join-Path $projectRoot "artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$configDir = "$env:TEMP\jcc_e2e_$($PID)"

# 验证 exe 存在
if (-not (Test-Path $mockServerExe)) {
    Write-Host "MockServer exe 不存在: $mockServerExe" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $jccExe)) {
    Write-Host "jcc.exe 不存在: $jccExe" -ForegroundColor Red
    exit 1
}

# 创建配置目录
New-Item -ItemType Directory -Path $configDir -Force | Out-Null

# === 测试配置 ===

function Write-MockServerConfig {
    param([string]$ConfigPath, [string]$JsonContent)
    Set-Content -Path $ConfigPath -Value $JsonContent -Encoding UTF8
    Write-Host "  配置文件: $ConfigPath" -ForegroundColor Gray
}

$testConfigs = @{
    basic = @{
        Name = "基础对话测试"
        Config = @"
{
  "port": $Port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "你好！我是AI助手，有什么可以帮你的？",
      "follow_up_text": null
    }
  ]
}
"@
        Input = "你好"
    }

    complete_step = @{
        Name = "P0-1: complete_step 证据门控测试"
        Config = @"
{
  "port": $Port,
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
        Input = "完成第一步"
    }

    ssrf = @{
        Name = "P0-2: SSRF 防护链路测试"
        Config = @"
{
  "port": $Port,
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
        Input = "获取一个网页内容"
    }

    session = @{
        Name = "P1-2: SessionController 统一消费测试"
        Config = @"
{
  "port": $Port,
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
        Input = "介绍一下C#"
    }

    dual_model = @{
        Name = "P1-1: 双模型分离链路测试"
        Config = @"
{
  "port": $Port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": [
        {
          "tool_name": "Bash",
          "arguments": "{\"command\":\"find . -name '*.cs' | head -5\"}"
        }
      ],
      "text_response": null,
      "follow_up_text": "项目包含 5 个 C# 文件。代码质量分析：整体结构清晰，命名规范。"
    }
  ]
}
"@
        Input = "分析这个项目的代码质量"
    }

    event_stream = @{
        Name = "P1-3: 事件流上下文保持测试"
        Config = @"
{
  "port": $Port,
  "default_response": "脚本耗尽",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "好的，我记住了，你的项目名叫 Alpha。",
      "follow_up_text": null
    },
    {
      "thinking_content": null,
      "tool_calls": [
        {
          "tool_name": "Bash",
          "arguments": "{\"command\":\"ls Alpha/\"}"
        }
      ],
      "text_response": null,
      "follow_up_text": "Alpha 项目包含 src、tests、docs 目录。"
    },
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "你的项目名叫 Alpha。",
      "follow_up_text": null
    }
  ]
}
"@
        Input = "multi_turn"
    }
}

# === 启动 MockServer ===

function Start-MockServer {
    param([string]$ConfigPath)

    Write-Host "`n=== 启动 MockServer ===" -ForegroundColor Cyan
    Write-Host "  端口: $Port" -ForegroundColor Gray

    $process = Start-Process -FilePath $mockServerExe -ArgumentList "--config `"$ConfigPath`" --port $Port" -PassThru -NoNewWindow -RedirectStandardOutput "$configDir\mockserver_stdout.txt" -RedirectStandardError "$configDir\mockserver_stderr.txt"

    # 等待 MockServer 就绪
    $maxWait = 10
    $waited = 0
    while ($waited -lt $maxWait) {
        Start-Sleep -Seconds 1
        $waited++
        try {
            $response = Invoke-RestMethod -Uri "http://localhost:$Port/" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response) {
                Write-Host "  MockServer 就绪 (PID: $($process.Id), 等待 ${waited}s)" -ForegroundColor Green
                return $process
            }
        } catch {
            # 还没就绪
        }
    }

    Write-Host "  MockServer 启动超时 (${maxWait}s)" -ForegroundColor Red
    if (Test-Path "$configDir\mockserver_stderr.txt") {
        Write-Host "  stderr:" -ForegroundColor Yellow
        Get-Content "$configDir\mockserver_stderr.txt" -Tail 5
    }
    return $null
}

# === 运行单个测试 ===

function Run-SingleTest {
    param([string]$TestKey)

    $testConfig = $testConfigs[$TestKey]
    if (-not $testConfig) {
        Write-Host "  未知测试: $TestKey" -ForegroundColor Red
        return $false
    }

    Write-Host "`n=== 运行测试: $($testConfig.Name) ===" -ForegroundColor Cyan

    # 写配置文件
    $configPath = "$configDir\mockserver_$TestKey.json"
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
            $errFile = "$configDir\jcc_stderr.txt"

            $psi = [System.Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = $jccExe
            $psi.Arguments = "--trust --force-interactive --await 30"
            $psi.EnvironmentVariables["JCC_ENDPOINT"] = "http://localhost:$Port"
            $psi.EnvironmentVariables["JCC_API_KEY"] = "sk-test-1234567890"
            $psi.EnvironmentVariables["JCC_PROVIDER"] = "openai"
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
                Start-Sleep -Seconds 2
                Write-Host "  > 发送: $input" -ForegroundColor Gray
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
                Write-Host "  FAIL: 输出中未找到 'Alpha'" -ForegroundColor Red
                $passed = $false
            }
            if ($stdout -match "JsonException|反序列化失败") {
                Write-Host "  FAIL: 发现 JSON 解析错误" -ForegroundColor Red
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
            $psi.EnvironmentVariables["JCC_API_KEY"] = "sk-test-1234567890"
            $psi.EnvironmentVariables["JCC_PROVIDER"] = "openai"
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
                Write-Host "  FAIL: jcc.exe 超时退出 (exit code 1234)" -ForegroundColor Red
                $passed = $false
            }
            if ($stdout -match "JsonException|反序列化失败") {
                Write-Host "  FAIL: 发现 JSON 解析错误" -ForegroundColor Red
                $passed = $false
            }
            if ($stdout.Length -lt 5) {
                Write-Host "  FAIL: 输出为空或过短" -ForegroundColor Red
                $passed = $false
            }

            return $passed
        }
    }
    finally {
        # 关闭 MockServer
        Write-Host "`n--- 关闭 MockServer ---" -ForegroundColor Yellow
        try {
            Invoke-RestMethod -Uri "http://localhost:$Port/shutdown" -Method Get -TimeoutSec 3 -ErrorAction SilentlyContinue | Out-Null
        } catch {}
        Start-Sleep -Seconds 1
        if (-not $mockServer.HasExited) {
            $mockServer.Kill()
        }
    }
}

# === 主逻辑 ===

Write-Host "=== Reasonix 移植功能 E2E 集成测试 ===" -ForegroundColor Cyan
Write-Host "MockServer: $mockServerExe" -ForegroundColor Gray
Write-Host "jcc.exe:   $jccExe" -ForegroundColor Gray

$results = @{}

if ($TestName -eq "all") {
    foreach ($key in $testConfigs.Keys) {
        $results[$key] = Run-SingleTest -TestKey $key
    }
} else {
    $results[$TestName] = Run-SingleTest -TestKey $TestName
}

# === 汇总 ===

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "E2E 测试结果汇总" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$allPassed = $true
foreach ($key in $results.Keys) {
    $status = if ($results[$key]) { "PASS" } else { "FAIL" }
    $color = if ($results[$key]) { "Green" } else { "Red" }
    Write-Host "  $key : $status" -ForegroundColor $color
    if (-not $results[$key]) { $allPassed = $false }
}

Write-Host "========================================" -ForegroundColor Cyan

# 清理
Remove-Item -Path $configDir -Recurse -Force -ErrorAction SilentlyContinue

if ($allPassed) {
    Write-Host "全部通过!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "存在失败!" -ForegroundColor Red
    exit 1
}
