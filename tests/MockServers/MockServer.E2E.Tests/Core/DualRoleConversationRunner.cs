namespace MockServer.E2E.Tests.Core;

// 测试运行器需要启动真实进程和访问文件系统路径
#pragma warning disable JCC9001, JCC9002
public sealed class DualRoleConversationRunner : IAsyncDisposable
{
    private readonly ILogger<DualRoleConversationRunner> _logger;
    private readonly IFileSystem _fs;
    private StdioProcessManager? _processManager;
    private readonly ILoggerFactory _loggerFactory;
    private string? _configFilePath;
    private string? _stateFilePath;
    private string? _dumpDir;
    private Process? _mockServerProcess;
    private int _mockServerPort;
    private string? _mockServerConfigDir;
    private ProviderKind _activeProvider = ProviderKind.OpenAI;
    private Process? _mcpMockServerProcess;
    private int _mcpMockServerPort;

    public DualRoleConversationRunner(ILogger<DualRoleConversationRunner> logger, IFileSystem? fs = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fs = fs ?? new IO.FileSystem.PhysicalFileSystem();
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    }

    /// <summary>
    /// 运行对话脚本 — 支持多供应商 MockServer
    /// </summary>
    /// <param name="script">对话脚本</param>
    /// <param name="provider">供应商类型，默认 OpenAI</param>
    /// <param name="ct">取消令牌</param>
    public async Task<ConversationResult> RunAsync(ConversationScript script, ProviderKind provider = ProviderKind.OpenAI, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        _activeProvider = provider;

        _logger.LogInformation("[DualRoleRunner] 开始执行脚本: {Script}, 供应商: {Provider}, 轮次: {Turns}",
            script.Name, provider, script.Turns.Count);

        // 按需先启动 Mcp.MockServer（用于测试 jcc 连接外部 MCP 服务器并调用工具的正向链路）
        // 必须在 WriteMockServerConfig 之前启动,以便将实际端口注入到 LLM MockServer 的工具调用参数中
        if (script.RequiresMcpMockServer)
        {
            _mcpMockServerPort = script.McpMockServerPort > 0 ? script.McpMockServerPort : GetAvailablePort();
            await StartMcpMockServerAsync(_mcpMockServerPort, ct).ConfigureAwait(true);
        }

        _configFilePath = WriteMockServerConfig(script);
        await StartMockServerAsync(_configFilePath, ct).ConfigureAwait(true);

        var exePath = ResolveExecutablePath();
        _logger.LogInformation("[DualRoleRunner] jcc.exe 路径: {Path}", exePath);

        var stateDir = _fs.CombinePath(Path.GetTempPath(), $"jcc_test_{Guid.NewGuid():N}");
        _fs.CreateDirectory(stateDir);
        _stateFilePath = _fs.CombinePath(stateDir, "workflow_state.json");

        var providerValue = _activeProvider switch
        {
            ProviderKind.OpenAI => "openai",
            ProviderKind.Anthropic => "anthropic",
            ProviderKind.DeepSeek => "deepseek",
            _ => "openai"
        };
        var modelId = _activeProvider switch
        {
            ProviderKind.OpenAI => "gpt-4o",
            ProviderKind.Anthropic => "claude-sonnet-4-20250514",
            ProviderKind.DeepSeek => "deepseek-v4-flash",
            _ => "gpt-4o"
        };
        var apiKeyEnvVar = _activeProvider switch
        {
            ProviderKind.OpenAI => "OPENAI_API_KEY",
            ProviderKind.Anthropic => "ANTHROPIC_API_KEY",
            ProviderKind.DeepSeek => "DEEPSEEK_API_KEY",
            _ => "OPENAI_API_KEY"
        };

        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JCC_ENDPOINT"] = $"http://localhost:{_mockServerPort}",
            ["JCC_API_KEY"] = "sk-test-1234567890",
            ["JCC_PROVIDER"] = providerValue,
            ["JCC_MODEL_ID"] = modelId,
            [apiKeyEnvVar] = "sk-test-1234567890",
            ["JCC_STATE_FILE_PATH"] = _stateFilePath,
            // E2E 测试自动升级权限到 bypassPermissions，避免 100+ 工具被权限拒绝阻塞
            ["JCC_PERMISSION_MODE"] = "bypassPermissions",
            // 隔离 AppData 目录，避免并发测试共享 onboarding_complete.json 导致文件锁冲突
            ["JCC_APP_DATA_FOLDER"] = stateDir,

        };

        if (script.ExtraEnvVars is not null)
        {
            foreach (var (key, value) in script.ExtraEnvVars)
            {
                envVars[key] = value;
            }
        }

        var args = script.Mode == ConversationMode.NonInteractive
            ? $"--trust -p \"{script.Turns[0].UserInput}\""
            : "--trust --force-interactive";

        if (!string.IsNullOrWhiteSpace(script.AdditionalArgs))
        {
            args += $" {script.AdditionalArgs}";
        }

        _processManager = new StdioProcessManager(_loggerFactory.CreateLogger<StdioProcessManager>());

        var config = new StdioProcessConfig
        {
            ExecutablePath = exePath,
            Arguments = args,
            EnvironmentVariables = envVars,
            WorkingDirectory = script.WorkingDirectory
        };

        await _processManager.StartAsync(config, ct).ConfigureAwait(true);

        ConversationResult result;
        if (script.Mode == ConversationMode.NonInteractive)
        {
            result = await RunNonInteractiveAsync(script, ct).ConfigureAwait(true);
        }
        else
        {
            result = await RunInteractiveAsync(script, ct).ConfigureAwait(true);
        }

        if (script.DumpMessages)
        {
            var dumpFiles = CollectDumpFiles();
            var analyzer = new PrefixCacheAnalyzer(_fs);
            var cacheAnalysis = analyzer.Analyze(dumpFiles);

            result = result with
            {
                DumpFiles = dumpFiles,
                CacheAnalysis = cacheAnalysis
            };

            _logger.LogInformation("[DualRoleRunner] 收集到 {Count} 个 dump 文件, 前缀缓存稳定: {Stable}",
                dumpFiles.Count, cacheAnalysis.AllPrefixesStable);

            foreach (var brk in cacheAnalysis.Breaks)
            {
                _logger.LogWarning("[DualRoleRunner] 前缀缓存失效: Turn {From} -> Turn {To}, 原因: {Reason}",
                    brk.FromTurn, brk.ToTurn, brk.Reason);
            }
        }

        return result;
    }

    private async Task<ConversationResult> RunNonInteractiveAsync(ConversationScript script, CancellationToken ct)
    {
        var turnRecords = new List<ConversationTurnRecord>();
        var assertResults = new List<AssertResult>();

        var turn = script.Turns[0];

        var output = await WaitForNonInteractiveOutputAsync(turn.ResponseTimeout, ct).ConfigureAwait(true);

        var record = ConversationOutputParser.Parse(output);
        turnRecords.Add(record with { UserInput = turn.UserInput });

        var turnAsserts = ConversationOutputParser.EvaluateAsserts(record, turn.Asserts);
        assertResults.AddRange(turnAsserts);

        // CI 环境间歇性失败重试: 如果 HasAssistantResponse 断言失败（jcc.exe 可能因资源竞争未收到响应），
        // 重新启动 jcc.exe 再试一次
        if (assertResults.Any(a => !a.IsPassed && a.Type == AssertType.HasAssistantResponse))
        {
            _logger.LogWarning("[DualRoleRunner] NonInteractive 首次运行未获得助手回复，重试一次");

            await _processManager!.DisposeAsync().ConfigureAwait(true);
            _processManager = null;

            var exePath = ResolveExecutablePath();
            var providerValue = _activeProvider switch
            {
                ProviderKind.OpenAI => "openai",
                ProviderKind.Anthropic => "anthropic",
                ProviderKind.DeepSeek => "deepseek",
                _ => "openai"
            };
            var modelId = _activeProvider switch
            {
                ProviderKind.OpenAI => "gpt-4o",
                ProviderKind.Anthropic => "claude-sonnet-4-20250514",
                ProviderKind.DeepSeek => "deepseek-v4-flash",
                _ => "gpt-4o"
            };
            var apiKeyEnvVar = _activeProvider switch
            {
                ProviderKind.OpenAI => "OPENAI_API_KEY",
                ProviderKind.Anthropic => "ANTHROPIC_API_KEY",
                ProviderKind.DeepSeek => "DEEPSEEK_API_KEY",
                _ => "OPENAI_API_KEY"
            };
            var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["JCC_ENDPOINT"] = $"http://localhost:{_mockServerPort}",
                ["JCC_API_KEY"] = "sk-test-1234567890",
                ["JCC_PROVIDER"] = providerValue,
                ["JCC_MODEL_ID"] = modelId,
                [apiKeyEnvVar] = "sk-test-1234567890",
                ["JCC_STATE_FILE_PATH"] = _stateFilePath!,
                ["JCC_PERMISSION_MODE"] = "bypassPermissions",
                ["JCC_APP_DATA_FOLDER"] = Path.GetDirectoryName(_stateFilePath)!,
            };

            if (script.ExtraEnvVars is not null)
            {
                foreach (var (key, value) in script.ExtraEnvVars)
                {
                    envVars[key] = value;
                }
            }

            var args = $"--trust -p \"{script.Turns[0].UserInput}\"";
            if (!string.IsNullOrWhiteSpace(script.AdditionalArgs))
            {
                args += $" {script.AdditionalArgs}";
            }

            _processManager = new StdioProcessManager(_loggerFactory.CreateLogger<StdioProcessManager>());
            var config = new StdioProcessConfig
            {
                ExecutablePath = exePath,
                Arguments = args,
                EnvironmentVariables = envVars,
                WorkingDirectory = script.WorkingDirectory
            };

            await _processManager.StartAsync(config, ct).ConfigureAwait(true);

            var retryOutput = await WaitForNonInteractiveOutputAsync(turn.ResponseTimeout, ct).ConfigureAwait(true);

            turnRecords.Clear();
            assertResults.Clear();

            var retryRecord = ConversationOutputParser.Parse(retryOutput);
            turnRecords.Add(retryRecord with { UserInput = turn.UserInput });

            var retryAsserts = ConversationOutputParser.EvaluateAsserts(retryRecord, turn.Asserts);
            assertResults.AddRange(retryAsserts);

            if (script.DumpMessages)
            {
                DumpTurnRecord(script.Name, 0, retryRecord with { UserInput = turn.UserInput }, turnRecords);
            }
        }
        else
        {
            if (script.DumpMessages)
            {
                DumpTurnRecord(script.Name, 0, record with { UserInput = turn.UserInput }, turnRecords);
            }
        }

        var stderrOutput = await CaptureStderrAsync().ConfigureAwait(true);
        LogStepComponents(stderrOutput);

        return new ConversationResult
        {
            ScriptName = script.Name,
            TurnRecords = turnRecords,
            AssertResults = assertResults,
            StderrOutput = stderrOutput
        };
    }

    /// <summary>
    /// 等待 NonInteractive 模式输出 — 监听 [DONE] 标记或进程退出
    /// </summary>
    private async Task<string> WaitForNonInteractiveOutputAsync(TimeSpan timeout, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var seenDone = false;
        var seenAlive = false;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (!_processManager!.IsRunning)
            {
                await Task.Delay(300, ct).ConfigureAwait(true);
                var exitOutput = await _processManager.GetOutputAsync().ConfigureAwait(true);
                if (exitOutput.Length > 0)
                {
                    _logger.LogInformation("[DualRoleRunner] jcc.exe 进程已退出（NonInteractive），输出长度={Len}", exitOutput.Length);
                    return exitOutput;
                }
                var exitError = await CaptureStderrAsync().ConfigureAwait(true);
                if (exitError.Contains("[DONE]", StringComparison.Ordinal))
                {
                    _logger.LogInformation("[DualRoleRunner] jcc.exe 进程已退出，stderr含[DONE]，视为成功");
                    return string.Empty;
                }
                _logger.LogError("[DualRoleRunner] jcc.exe 进程已退出且无输出，stderr={Stderr}", exitError);
                throw new InvalidOperationException($"jcc.exe 进程已退出且无输出, stderr={exitError}");
            }

            var incrementalStderr = await CaptureStderrIncrementalAsync().ConfigureAwait(true);
            if (incrementalStderr.Contains("[DONE]", StringComparison.Ordinal))
                seenDone = true;
            if (incrementalStderr.Contains("[ALIVE]", StringComparison.Ordinal))
                seenAlive = true;

            if (seenDone)
            {
                var doneOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);
                if (doneOutput.Length > 0)
                {
                    _logger.LogInformation("[DualRoleRunner] 检测到 [DONE] 标记（NonInteractive），输出长度={Len}", doneOutput.Length);
                    return doneOutput;
                }
            }

            var currentOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);

            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed >= TimeSpan.FromSeconds(10)
                && currentOutput.Length > 5
                && !HasUnfinishedToolCall(currentOutput)
                && !seenAlive)
            {
                return currentOutput;
            }

            await Task.Delay(100, ct).ConfigureAwait(true);
        }

        if (!_processManager!.IsRunning)
        {
            var exitOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);
            if (exitOutput.Length > 0)
            {
                _logger.LogInformation("[DualRoleRunner] jcc.exe 进程已退出（NonInteractive 超时后），输出长度={Len}", exitOutput.Length);
                return exitOutput;
            }
            var exitError = await CaptureStderrAsync().ConfigureAwait(true);
            if (exitError.Contains("[DONE]", StringComparison.Ordinal))
            {
                _logger.LogInformation("[DualRoleRunner] jcc.exe 进程已退出（超时后），stderr含[DONE]，视为成功");
                return string.Empty;
            }
            throw new InvalidOperationException($"jcc.exe 进程已退出且无输出, stderr={exitError}");
        }

        return await _processManager!.GetOutputAsync().ConfigureAwait(true);
    }

    private async Task<string> CaptureStderrAsync()
    {
        if (_processManager is null) return "";
        try
        {
            return await _processManager.GetErrorAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[DualRoleRunner] 捕获 stderr 时异常");
            return "";
        }
    }

    private async Task<string> CaptureStderrIncrementalAsync()
    {
        if (_processManager is null) return "";
        try
        {
            return await _processManager.GetErrorIncrementalAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[DualRoleRunner] 增量捕获 stderr 时异常");
            return "";
        }
    }

    private async Task<ConversationResult> RunInteractiveAsync(ConversationScript script, CancellationToken ct)
    {
        var turnRecords = new List<ConversationTurnRecord>();
        var assertResults = new List<AssertResult>();

        if (script.DumpMessages)
        {
            _dumpDir = _fs.CombinePath(Path.GetTempPath(), $"jcc_dump_{Guid.NewGuid():N}");
            _fs.CreateDirectory(_dumpDir);
        }

        await WaitForProcessReadyAsync(ct).ConfigureAwait(true);

        var startupOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);

        for (var i = 0; i < script.Turns.Count; i++)
        {
            var turn = script.Turns[i];
            _logger.LogInformation("[DualRoleRunner] Turn {Index}/{Total}: UserInput=\"{Input}\"",
                i + 1, script.Turns.Count, turn.UserInput);

            await _processManager!.ClearOutputAsync().ConfigureAwait(true);
            await _processManager.SendAsync(turn.UserInput, ct).ConfigureAwait(true);

            var output = await WaitForStableOutputAsync(turn.ResponseTimeout, ct).ConfigureAwait(true);

            var record = ConversationOutputParser.Parse(output);
            var turnRecord = record with { UserInput = turn.UserInput };
            turnRecords.Add(turnRecord);

            var turnAsserts = ConversationOutputParser.EvaluateAsserts(record, turn.Asserts);
            assertResults.AddRange(turnAsserts);

            foreach (var ar in turnAsserts.Where(a => !a.IsPassed))
            {
                _logger.LogWarning("[DualRoleRunner] 断言失败: {Type} Expected=\"{Expected}\" Actual=\"{Actual}\" Desc=\"{Desc}\"",
                    ar.Type, ar.Expected, ar.ActualValue?[..Math.Min(100, ar.ActualValue.Length)], ar.Description);
            }

            if (script.DumpMessages)
            {
                DumpTurnRecord(script.Name, i, turnRecord, turnRecords);
            }
        }

        var stderrOutput = await CaptureStderrAsync().ConfigureAwait(true);
        LogStepComponents(stderrOutput);

        try
        {
            await _processManager!.SendAsync("/exit", ct).ConfigureAwait(true);
            if (_processManager!.IsRunning)
            {
                using var exitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                exitCts.CancelAfter(TimeSpan.FromSeconds(3));
                while (_processManager.IsRunning && !exitCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, ct).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[DualRoleRunner] 退出时异常（可忽略）");
        }

        return new ConversationResult
        {
            ScriptName = script.Name,
            TurnRecords = turnRecords,
            AssertResults = assertResults,
            StderrOutput = stderrOutput
        };
    }

    /// <summary>
    /// 从 stderr 输出中解析 [STEP] 组件标记和 [Timing] 计时行并记录
    /// </summary>
    private void LogStepComponents(string stderrOutput)
    {
        if (string.IsNullOrWhiteSpace(stderrOutput)) return;

        var steps = new List<string>();
        var timings = new List<string>();

        foreach (var line in stderrOutput.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Contains("[STEP]", StringComparison.Ordinal))
            {
                steps.Add(trimmed);
            }
            if (trimmed.Contains("[Timing]", StringComparison.Ordinal))
            {
                timings.Add(trimmed);
            }
        }

        if (steps.Count > 0)
        {
            _logger.LogInformation("[DualRoleRunner] 组件验证: 发现 {Count} 个 [STEP] 组件标记", steps.Count);
            foreach (var step in steps)
            {
                _logger.LogDebug("  {Step}", step.Trim());
            }
        }

        if (timings.Count > 0)
        {
            _logger.LogInformation("[DualRoleRunner] 计时记录: 发现 {Count} 个 [Timing] 记录", timings.Count);
            foreach (var timing in timings)
            {
                _logger.LogDebug("  {Timing}", timing.Trim());
            }
        }

        if (steps.Count == 0)
        {
            _logger.LogWarning("[DualRoleRunner] 未发现 [STEP] 组件标记 — 组件验证无法执行");
        }
    }

    private async Task<string> WaitForStableOutputAsync(TimeSpan timeout, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var lastChangeTime = DateTime.UtcNow;
        var lastLength = 0;
        var lastAliveTime = DateTime.UtcNow;
        var seenDone = false;
        var pollCount = 0;

        var stderrBaseline = await CaptureStderrAsync().ConfigureAwait(true);
        var baselineDoneCount = CountMarker(stderrBaseline, "[DONE]");
        var cumulativeDoneCount = baselineDoneCount;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (!_processManager!.IsRunning)
            {
                await Task.Delay(300, ct).ConfigureAwait(true);
                var exitOutput = await _processManager.GetOutputAsync().ConfigureAwait(true);
                if (exitOutput.Length > 0)
                {
                    _logger.LogInformation("[DualRoleRunner] jcc.exe 进程已退出，返回已有输出（长度={Len}）", exitOutput.Length);
                    return exitOutput;
                }
                var exitError = await CaptureStderrAsync().ConfigureAwait(true);
                _logger.LogError("[DualRoleRunner] jcc.exe 进程已退出且无输出，stderr={Stderr}", exitError);
                throw new InvalidOperationException($"jcc.exe 进程已退出且无输出, stderr={exitError}");
            }

            var currentOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);

            if (currentOutput.Length != lastLength)
            {
                if (pollCount % 20 == 0 && lastLength > 0)
                {
                    _logger.LogInformation("[DualRoleRunner] 输出变化: {From}->{To} 字符", lastLength, currentOutput.Length);
                }
                lastLength = currentOutput.Length;
                lastChangeTime = DateTime.UtcNow;
            }

            var incrementalStderr = await CaptureStderrIncrementalAsync().ConfigureAwait(true);
            if (incrementalStderr.Contains("[ALIVE]", StringComparison.Ordinal))
            {
                lastAliveTime = DateTime.UtcNow;
            }
            cumulativeDoneCount += CountMarker(incrementalStderr, "[DONE]");
            if (cumulativeDoneCount > baselineDoneCount)
            {
                seenDone = true;
            }

            // 优先级1: [DONE] 标记 — jcc.exe 明确表示处理完成
            if (seenDone && currentOutput.Length >= 2)
            {
                if (HasUnfinishedToolCall(currentOutput))
                {
                    seenDone = false;
                    await Task.Delay(100, ct).ConfigureAwait(true);
                    continue;
                }
                // 等待 stdout 完全刷新 — stderr [DONE] 可能先于 stdout [Tool]/[FAIL] 到达
                await Task.Delay(200, ct).ConfigureAwait(true);
                currentOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);
                _logger.LogInformation("[DualRoleRunner] 检测到 [DONE] 标记，输出长度={Len}，轮询次数={Polls}", currentOutput.Length, pollCount);
                return currentOutput;
            }

            // 优先级2: 输出稳定 + 心跳已停止 — 兜底逻辑（兼容旧版本无 [DONE] 标记）
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed >= TimeSpan.FromSeconds(3)
                && currentOutput.Length >= 2
                && DateTime.UtcNow - lastChangeTime >= TimeSpan.FromMilliseconds(500))
            {
                if (HasUnfinishedToolCall(currentOutput))
                {
                    lastChangeTime = DateTime.UtcNow;
                    await Task.Delay(100, ct).ConfigureAwait(true);
                    continue;
                }

                var aliveAge = DateTime.UtcNow - lastAliveTime;
                if (aliveAge < TimeSpan.FromSeconds(3))
                {
                    await Task.Delay(200, ct).ConfigureAwait(true);
                    continue;
                }

                _logger.LogInformation("[DualRoleRunner] 输出稳定判定: 长度={Len}, 稳定时间={StableMs}ms, 心跳静默={AliveMs}ms",
                    currentOutput.Length, (DateTime.UtcNow - lastChangeTime).TotalMilliseconds, aliveAge.TotalMilliseconds);
                return currentOutput;
            }

            pollCount++;
            await Task.Delay(50, ct).ConfigureAwait(true);
        }

        if (!_processManager!.IsRunning)
        {
            var exitOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);
            if (exitOutput.Length > 0)
            {
                _logger.LogInformation("[DualRoleRunner] jcc.exe 进程已退出（超时后），返回已有输出（长度={Len}）", exitOutput.Length);
                return exitOutput;
            }
            var exitError = await CaptureStderrAsync().ConfigureAwait(true);
            throw new InvalidOperationException($"jcc.exe 进程已退出且无输出, stderr={exitError}");
        }

        var finalOutput = await _processManager!.GetOutputAsync().ConfigureAwait(true);
        if (finalOutput.Length >= 2) return finalOutput;

        throw new TimeoutException($"等待输出超时 (>{timeout.TotalSeconds}s)");
    }

    private static int CountMarker(string text, string marker)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += marker.Length;
        }
        return count;
    }

    /// <summary>
    /// 检测输出中是否有未完成的工具调用 — [Tool] 标记没有对应的 [OK]/[FAIL] 结束标记
    /// </summary>
    private static bool HasUnfinishedToolCall(string output)
    {
        var toolStartCount = 0;
        var toolEndCount = 0;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Contains("[Tool] ", StringComparison.Ordinal))
                toolStartCount++;
            if (trimmed.Contains("[OK] ", StringComparison.Ordinal) || trimmed.Contains("[FAIL] ", StringComparison.Ordinal))
                toolEndCount++;
        }
        return toolStartCount > toolEndCount;
    }

    /// <summary>
    /// 等待 jcc.exe 进程就绪 — 监听 stderr 中的 [READY] 标记
    /// jcc.exe 在 ReplLoopStep/NonInteractiveExecuteStep 就绪时输出 [READY] 到 stderr
    /// </summary>
    private async Task WaitForProcessReadyAsync(CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(60);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (!_processManager!.IsRunning)
            {
                var exitError = await CaptureStderrAsync().ConfigureAwait(true);
                throw new InvalidOperationException($"jcc.exe 进程已退出, stderr={exitError}");
            }

            var incrementalStderr = await CaptureStderrIncrementalAsync().ConfigureAwait(true);
            if (incrementalStderr.Contains("[READY]", StringComparison.Ordinal))
            {
                _logger.LogInformation("[DualRoleRunner] 检测到 [READY] 标记，进程就绪");
                return;
            }

            await Task.Delay(100, ct).ConfigureAwait(true);
        }

        var finalStderr = await CaptureStderrAsync().ConfigureAwait(true);
        _logger.LogError("[DualRoleRunner] 等待进程就绪超时(60s), 未检测到 [READY], stderr前200字符={Preview}",
            finalStderr.Length > 200 ? finalStderr[..200] : finalStderr);
        throw new TimeoutException($"jcc.exe 60秒内未输出 [READY]，可能初始化卡住");
    }

    private string WriteMockServerConfig(ConversationScript script)
    {
        // 构建 MockServer 配置 JSON（snake_case 格式，匹配 MockServerJsonContext）
        // ⚠️ 带工具调用的对话轮次需要 2 个 MockServer 脚本轮次:
        //   1) 工具调用响应 (tool_calls)
        //   2) 跟进文本响应 (follow_up_text → text_response)
        //   因为 LLM 协议中: 用户输入 → 工具调用 → 工具结果 → 跟进文本 是 2 次请求
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"port\": 0,");
        sb.AppendLine("  \"default_response\": \"(script exhausted)\",");
        sb.AppendLine("  \"scripted_turns\": [");

        var isFirst = true;
        for (var i = 0; i < script.Turns.Count; i++)
        {
            var turn = script.Turns[i];
            var hasToolCalls = turn.AiResponse.ToolCalls is { Count: > 0 };
            var hasText = !string.IsNullOrEmpty(turn.AiResponse.TextResponse);
            var hasFollowUp = !string.IsNullOrEmpty(turn.AiResponse.FollowUpText);
            var hasThinking = !string.IsNullOrEmpty(turn.AiResponse.ThinkingContent);

            if (hasToolCalls)
            {
                // Turn A: 工具调用（不含 follow_up_text，避免 MockServer 在一次请求中同时返回工具调用和文本）
                if (!isFirst) sb.AppendLine(","); else isFirst = false;
                AppendMockTurn(sb, hasThinking ? turn.AiResponse.ThinkingContent : null,
                    toolCalls: turn.AiResponse.ToolCalls,
                    textResponse: null,
                    followUp: null,
                    httpStatusCode: turn.AiResponse.HttpStatusCode);

                if (hasFollowUp)
                {
                    // Turn B: 跟进文本 — 作为单独的文本响应，MockServer 消耗下一个脚本轮次返回
                    sb.AppendLine(",");
                    AppendMockTurn(sb, thinkingContent: null,
                        toolCalls: null,
                        textResponse: turn.AiResponse.FollowUpText,
                        followUp: null);
                }
            }
            else
            {
                // 纯文本响应 — 一个对话轮次对应一个 MockServer 脚本轮次
                if (!isFirst) sb.AppendLine(","); else isFirst = false;
                AppendMockTurn(sb,
                    hasThinking ? turn.AiResponse.ThinkingContent : null,
                    toolCalls: null,
                    textResponse: turn.AiResponse.TextResponse,
                    followUp: null,
                    httpStatusCode: turn.AiResponse.HttpStatusCode);
            }
        }

        sb.AppendLine();
        // 追加额外脚本轮次 — 用于子进程（subagent）的 LLM 调用
        if (script.MockServerExtraTurns is { Count: > 0 })
        {
            foreach (var extraTurn in script.MockServerExtraTurns)
            {
                sb.AppendLine(",");
                AppendMockTurn(sb,
                    !string.IsNullOrEmpty(extraTurn.AiResponse.ThinkingContent) ? extraTurn.AiResponse.ThinkingContent : null,
                    toolCalls: extraTurn.AiResponse.ToolCalls,
                    textResponse: extraTurn.AiResponse.TextResponse,
                    followUp: extraTurn.AiResponse.FollowUpText);
            }
        }
        else if (script.MockServerExtraTextResponses is { Count: > 0 })
        {
            foreach (var extraText in script.MockServerExtraTextResponses)
            {
                sb.AppendLine(",");
                AppendMockTurn(sb, thinkingContent: null, toolCalls: null, textResponse: extraText, followUp: null);
            }
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        _mockServerConfigDir = _fs.CombinePath(Path.GetTempPath(), $"jcc_mock_cfg_{Guid.NewGuid():N}");
        _fs.CreateDirectory(_mockServerConfigDir);
        var filePath = _fs.CombinePath(_mockServerConfigDir, "mockserver.json");
        _fs.WriteAllText(filePath, sb.ToString());

        _logger.LogInformation("[DualRoleRunner] MockServer 配置文件: {Path}", filePath);
        return filePath;
    }

        /// <summary>
    /// 追加一个 MockServer 脚本轮次到 JSON 构建器
    /// 实例方法 — 支持将工具调用参数中的 {MCP_MOCK_PORT} 占位符替换为实际 Mcp.MockServer 端口
    /// </summary>
    private void AppendMockTurn(StringBuilder sb, string? thinkingContent,
        IReadOnlyList<MockToolCallScript>? toolCalls, string? textResponse, string? followUp,
        int? httpStatusCode = null)
    {
        sb.AppendLine("  {");

        sb.Append("    \"thinking_content\": ");
        sb.Append(thinkingContent is not null ? $"\"{EscapeJsonString(ReplacePortPlaceholders(thinkingContent))}\"" : "null");
        sb.AppendLine(",");

        if (toolCalls is { Count: > 0 })
        {
            sb.AppendLine("    \"tool_calls\": [");
            for (var j = 0; j < toolCalls.Count; j++)
            {
                var tc = toolCalls[j];
                var replacedArguments = ReplacePortPlaceholders(tc.Arguments);
                sb.AppendLine("    {");
                sb.AppendLine($"      \"tool_name\": \"{EscapeJsonString(tc.ToolName)}\",");
                sb.AppendLine($"      \"arguments\": \"{EscapeJsonString(replacedArguments)}\"");
                sb.Append("    }");
                if (j < toolCalls.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("    ],");
        }
        else
        {
            sb.AppendLine("    \"tool_calls\": null,");
        }

        sb.Append("    \"text_response\": ");
        sb.Append(textResponse is not null ? $"\"{EscapeJsonString(ReplacePortPlaceholders(textResponse))}\"" : "null");
        sb.AppendLine(",");

        sb.Append("    \"follow_up_text\": ");
        sb.Append(followUp is not null ? $"\"{EscapeJsonString(ReplacePortPlaceholders(followUp))}\"" : "null");
        sb.AppendLine(",");

        sb.Append("    \"http_status_code\": ");
        sb.Append(httpStatusCode?.ToString() ?? "null");
        sb.AppendLine();

        sb.Append("  }");
    }

    /// <summary>
    /// 替换文本中的 {MCP_MOCK_PORT} 占位符为实际 Mcp.MockServer 端口
    /// 用于让脚本中的 mcp_connect 参数动态注入端口（支持自动端口分配）
    /// </summary>
    private string ReplacePortPlaceholders(string? text)
    {
        if (string.IsNullOrEmpty(text) || _mcpMockServerPort == 0) return text ?? "";
        return text.Replace("{MCP_MOCK_PORT}", _mcpMockServerPort.ToString(), StringComparison.Ordinal);
    }

    private async Task StartMockServerAsync(string configPath, CancellationToken ct)
    {
        var mockServerExe = ResolveMockServerPath();
        _logger.LogInformation("[DualRoleRunner] MockServer.exe 路径: {Path}", mockServerExe);

        var startInfo = new ProcessStartInfo
        {
            FileName = mockServerExe,
            Arguments = $"--config \"{configPath}\" --port 0",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(mockServerExe) ?? _fs.GetCurrentDirectory()
        };

        _mockServerProcess = new Process { StartInfo = startInfo };

        var readyTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 根据供应商类型计算就绪标记
        var serverName = _activeProvider switch
        {
            ProviderKind.OpenAI => "OpenAI",
            ProviderKind.Anthropic => "Anthropic",
            ProviderKind.DeepSeek => "DeepSeek",
            _ => "OpenAI"
        };
        var readyMarker = $"[{serverName}]   URL:";

        _mockServerProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logger.LogTrace("[MockServer] {Line}", e.Data);

            var idx = e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && _mockServerProcess is not null)
            {
                var urlPart = e.Data[(idx + readyMarker.Length)..].Trim();
                // 解析 http://localhost:{port}/ 中的端口
                var match = Regex.Match(urlPart, @":(\d+)/?");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                {
                    readyTcs.TrySetResult(port);
                }
            }
        };

        _mockServerProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogTrace("[MockServer:ERR] {Line}", e.Data);
            }
        };

        if (!_mockServerProcess.Start())
        {
            throw new InvalidOperationException("无法启动 MockServer 进程");
        }

        _mockServerProcess.BeginOutputReadLine();
        _mockServerProcess.BeginErrorReadLine();

        // 等待 MockServer 就绪（最多 25 秒，并行测试时资源竞争可能延迟启动）
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(25));

        try
        {
            _mockServerPort = await readyTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
            _logger.LogInformation("[DualRoleRunner] MockServer 就绪, 端口: {Port}", _mockServerPort);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("等待 MockServer 就绪超时（25s）");
        }
    }

    /// <summary>
    /// 启动 Mcp.MockServer 进程 — 提供 MCP JSON-RPC 端点供 jcc 通过 mcp_connect 连接
    /// 使用源码目录的 mockserver.json 配置（暴露 echo/add/uppercase/reverse/length 工具）
    /// </summary>
    private async Task StartMcpMockServerAsync(int port, CancellationToken ct)
    {
        var mcpMockServerExe = ResolveMcpMockServerPath();
        _logger.LogInformation("[DualRoleRunner] Mcp.MockServer.exe 路径: {Path}", mcpMockServerExe);

        var configPath = ResolveMcpMockServerConfigPath();

        var startInfo = new ProcessStartInfo
        {
            FileName = mcpMockServerExe,
            Arguments = $"--config \"{configPath}\" --port {port}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(mcpMockServerExe) ?? _fs.GetCurrentDirectory()
        };

        _mcpMockServerProcess = new Process { StartInfo = startInfo };

        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyMarker = "[Mcp.MockServer] Listening on";

        _mcpMockServerProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logger.LogTrace("[Mcp.MockServer] {Line}", e.Data);

            if (e.Data.Contains(readyMarker, StringComparison.OrdinalIgnoreCase))
            {
                readyTcs.TrySetResult(true);
            }
        };

        _mcpMockServerProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogTrace("[Mcp.MockServer:ERR] {Line}", e.Data);
            }
        };

        if (!_mcpMockServerProcess.Start())
        {
            throw new InvalidOperationException("无法启动 Mcp.MockServer 进程");
        }

        _mcpMockServerProcess.BeginOutputReadLine();
        _mcpMockServerProcess.BeginErrorReadLine();

        // 等待 Mcp.MockServer 就绪（最多 15 秒）
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await readyTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
            _logger.LogInformation("[DualRoleRunner] Mcp.MockServer 就绪, 端口: {Port}", port);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("等待 Mcp.MockServer 就绪超时（15s）");
        }
    }

    /// <summary>
    /// 解析 Mcp.MockServer.exe 路径
    /// </summary>
    private string ResolveMcpMockServerPath()
    {
        const string exeName = "JoinCode.Mcp.MockServer.exe";
        return ResolveExeFromArtifactsBin(exeName);
    }

    /// <summary>
    /// 解析 Mcp.MockServer 配置文件路径 — 使用源码目录的 mockserver.json
    /// </summary>
    private string ResolveMcpMockServerConfigPath()
    {
        var exePath = ResolveMcpMockServerPath();
        var exeDir = Path.GetDirectoryName(exePath) ?? "";
        var candidates = new[]
        {
            Path.Combine(exeDir, "mockserver.json"),
            // 源码目录回退: tests/MockServers/Mcp.MockServer/mockserver.json
            Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "tests", "MockServers", "Mcp.MockServer", "mockserver.json")),
        };

        foreach (var path in candidates)
        {
            if (_fs.FileExists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"Mcp.MockServer 配置文件 mockserver.json 未找到。搜索路径: {string.Join(", ", candidates)}");
    }

    /// <summary>
    /// 获取可用端口
    /// </summary>
    private static int GetAvailablePort()
    {
        using var tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }

    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private string ResolveMockServerPath()
    {
        var (_, exeName) = _activeProvider switch
        {
            ProviderKind.Anthropic => ("Anthropic.MockServer", "JoinCode.Anthropic.MockServer.exe"),
            ProviderKind.DeepSeek => ("DeepSeek.MockServer", "JoinCode.DeepSeek.MockServer.exe"),
            _ => ("OpenAI.MockServer", "JoinCode.OpenAI.MockServer.exe")
        };

        return ResolveExeFromArtifactsBin(exeName);
    }

    private string ResolveExecutablePath()
    {
        return ResolveExeFromArtifactsBin("jcc.exe");
    }

    private string ResolveExeFromArtifactsBin(string exeName)
    {
        var baseDir = AppContext.BaseDirectory;
        var artifactsBin = FindArtifactsBinRoot(baseDir);
        if (artifactsBin is not null)
        {
            var found = SearchExeUnderDir(artifactsBin, exeName);
            if (found is not null)
            {
                _logger.LogInformation("[PathResolver] {ExeName} 解析成功: {Path}", exeName, found);
                return found;
            }
        }

        var fallback = Path.GetFullPath(Path.Combine(baseDir, exeName));
        if (_fs.FileExists(fallback))
        {
            _logger.LogInformation("[PathResolver] {ExeName} 回退解析成功: {Path}", exeName, fallback);
            return fallback;
        }

        var diag = new StringBuilder();
        diag.AppendLine($"{exeName} 未找到。诊断:");
        diag.AppendLine($"  BaseDirectory: {baseDir}");
        diag.AppendLine($"  artifacts/bin 根: {(artifactsBin ?? "(未找到)")}");
        if (artifactsBin is not null)
        {
            diag.AppendLine($"  artifacts/bin 下项目目录:");
            try
            {
                foreach (var dir in _fs.GetDirectories(artifactsBin, "*", SearchOption.TopDirectoryOnly))
                {
                    diag.AppendLine($"    {Path.GetFileName(dir)}/");
                }
            }
            catch (Exception ex)
            {
                diag.AppendLine($"    枚举失败: {ex.Message}");
            }
        }
        diag.AppendLine($"  回退路径: {fallback} ({(_fs.FileExists(fallback) ? "EXISTS" : "MISSING")})");

        throw new FileNotFoundException(diag.ToString());
    }

    private string? FindArtifactsBinRoot(string baseDir)
    {
        var dir = baseDir;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "artifacts", "bin");
            if (_fs.DirectoryExists(candidate))
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }

        var baseParent = Path.GetDirectoryName(baseDir);
        if (baseParent is not null)
        {
            var candidate = Path.Combine(baseParent, "artifacts", "bin");
            if (_fs.DirectoryExists(candidate))
            {
                return candidate;
            }
        }

        var baseGrandParent = baseParent is not null ? Path.GetDirectoryName(baseParent) : null;
        if (baseGrandParent is not null)
        {
            var candidate = Path.Combine(baseGrandParent, "artifacts", "bin");
            if (_fs.DirectoryExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? SearchExeUnderDir(string rootDir, string exeName)
    {
        try
        {
            var files = _fs.GetFiles(rootDir, exeName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[PathResolver] 搜索 {ExeName} 时异常", exeName);
            return null;
        }
    }

    private IReadOnlyList<string> CollectDumpFiles()
    {
        if (string.IsNullOrEmpty(_dumpDir) || !_fs.DirectoryExists(_dumpDir)) return [];

        return _fs.GetFiles(_dumpDir, "turn_*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void DumpTurnRecord(string scriptName, int turnIndex, ConversationTurnRecord record, IReadOnlyList<ConversationTurnRecord> allRecords)
    {
        if (string.IsNullOrEmpty(_dumpDir)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"=== 对话脚本: {scriptName} ===");
        sb.AppendLine($"=== 轮次: {turnIndex} ===");
        sb.AppendLine();

        for (var i = 0; i <= turnIndex; i++)
        {
            var tr = allRecords[i];
            sb.AppendLine($"--- 第{i + 1}轮 ---");
            sb.AppendLine("[User]");
            sb.AppendLine(tr.UserInput);
            sb.AppendLine();

            if (tr.ToolCalls.Count > 0)
            {
                foreach (var tc in tr.ToolCalls)
                {
                    sb.AppendLine($"[Tool] {tc.ToolName}({tc.Arguments})");
                    sb.AppendLine(tc.IsSuccess ? $"[OK] {tc.ToolName}" : $"[FAIL] {tc.ToolName}");
                    if (!string.IsNullOrEmpty(tc.Result))
                    {
                        sb.AppendLine($"  Result: {tc.Result}");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("[Assistant]");
            sb.AppendLine(tr.AssistantResponse);
            sb.AppendLine();

            if (tr.Errors.Count > 0)
            {
                sb.AppendLine("[Errors]");
                foreach (var err in tr.Errors)
                {
                    sb.AppendLine(err);
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("=== 当前轮原始输出 ===");
        sb.AppendLine(record.RawOutput);

        var fileName = $"turn_{turnIndex:D3}.txt";
        var filePath = _fs.CombinePath(_dumpDir, fileName);
        _fs.WriteAllText(filePath, sb.ToString());

        _logger.LogInformation("[DualRoleRunner] 已转储轮次 {Turn}: {Path}", turnIndex, filePath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_processManager is not null)
        {
            try
            {
                await _processManager.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DualRoleRunner] 释放进程管理器时异常");
            }
            _processManager = null;
        }

        if (_mockServerProcess is not null)
        {
            try
            {
                if (!_mockServerProcess.HasExited)
                {
                    _logger.LogInformation("[DualRoleRunner] 停止 MockServer 进程 (PID={Pid})", _mockServerProcess.Id);
                    _mockServerProcess.Kill(entireProcessTree: true);
                    await _mockServerProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(true);
                }
                _mockServerProcess.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DualRoleRunner] 停止 MockServer 进程时异常");
            }
            _mockServerProcess = null;
        }

        if (_mcpMockServerProcess is not null)
        {
            try
            {
                if (!_mcpMockServerProcess.HasExited)
                {
                    _logger.LogInformation("[DualRoleRunner] 停止 Mcp.MockServer 进程 (PID={Pid})", _mcpMockServerProcess.Id);
                    _mcpMockServerProcess.Kill(entireProcessTree: true);
                    await _mcpMockServerProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(true);
                }
                _mcpMockServerProcess.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DualRoleRunner] 停止 Mcp.MockServer 进程时异常");
            }
            _mcpMockServerProcess = null;
        }

        if (_mockServerConfigDir is not null)
        {
            try
            {
                if (_fs.DirectoryExists(_mockServerConfigDir))
                {
                    foreach (var f in _fs.GetFiles(_mockServerConfigDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        _fs.DeleteFile(f);
                    }
                    _fs.DeleteDirectory(_mockServerConfigDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DualRoleRunner] 清理 MockServer 配置目录时异常");
            }
            _mockServerConfigDir = null;
            _configFilePath = null;
        }

        if (_stateFilePath is not null)
        {
            try
            {
                var dir = _fs.GetDirectoryName(_stateFilePath);
                if (dir is not null && _fs.DirectoryExists(dir))
                {
                    foreach (var f in _fs.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        _fs.DeleteFile(f);
                    }
                    _fs.DeleteDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DualRoleRunner] 清理状态文件时异常");
            }
            _stateFilePath = null;
        }

        if (_dumpDir is not null)
        {
            try
            {
                if (_fs.DirectoryExists(_dumpDir))
                {
                    foreach (var f in _fs.GetFiles(_dumpDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        _fs.DeleteFile(f);
                    }
                    _fs.DeleteDirectory(_dumpDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DualRoleRunner] 清理dump目录时异常");
            }
            _dumpDir = null;
        }

        _loggerFactory.Dispose();
    }

}
