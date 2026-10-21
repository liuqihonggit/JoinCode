namespace NativeTui.Host;

/// <summary>
/// 子代理事件链 selftest（DSG041 集成验收第②项）— 真实引擎组装下的主代理路径子代理事件冒烟：
/// 启动真实 OpenAI.MockServer 假 LLM → 引擎会话（JCC_ENDPOINT 指向）→ 插件就绪等待（Completion 通知语义）
/// → 主代理提交 agent 工具回合 → 断言 OnSubagentStarted/OnSubagentToolUse/OnSubagentFinished 三回调到达
/// （引擎通道 Emit → SessionController Drain 直投 → RouteSubagentEvent 分流 → 消费者映射全链）。
/// MockServer exe 未找到时返回 Skipped（CI 由 ci-e2e subagent-events 三用例兜底，本地全链验证用）。
/// </summary>
internal static class SubagentSelftest
{
    /// <summary>运行结果。</summary>
    public enum Result
    {
        /// <summary>全链通过（三回调全部到达）。</summary>
        Passed,

        /// <summary>MockServer 未构建 — 跳过（非失败，CI 由 E2E 兜底）。</summary>
        Skipped,

        /// <summary>链路断言失败（failures 携带明细）。</summary>
        Failed,
    }

    /// <summary>运行子代理事件链 selftest。</summary>
    public static async Task<(Result Status, IReadOnlyList<string> Failures)> RunAsync()
    {
        var mockServerPath = ResolveMockServerPath();
        if (mockServerPath is null)
        {
            return (Result.Skipped, ["MockServer exe 未构建（artifacts/bin/OpenAI.MockServer）— 跳过子代理链冒烟，CI 由 ci-e2e subagent-events 兜底"]);
        }

        var failures = new List<string>();
        var configDir = Path.Combine(Path.GetTempPath(), $"jcc_subagent_selftest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "mockserver.json");
        await File.WriteAllTextAsync(configPath, BuildMockConfig()).ConfigureAwait(false);

        using var mockServer = StartMockServer(mockServerPath, configPath, out var readyTask);
        using var envScope = EnvVarScope.Set("JCC_ENDPOINT", await readyTask.WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false))
            .Add("OPENAI_API_KEY", "sk-test-subagent-selftest")
            .Add("JCC_VENDOR", "openai")
            .Add("JCC_MODEL_ID", "gpt-4o")
            .Add("JCC_PERMISSION_MODE", "bypass");
        TestEnvironmentDetector.ForceNonInteractive = true;

        try
        {
            await using var session = await EngineSession.CreateAsync().ConfigureAwait(false);
            var chatService = session.Services.GetService<JoinCode.Abstractions.Interfaces.IChatService>()
                ?? throw new InvalidOperationException("IChatService 未在引擎 DI 注册");
            var mainAgent = JoinCode.Cli.EngineAgentFactory.CreateMainAgent(
                session.Services, new JoinCode.Abstractions.Entity.Session().ObjectId);
            if (mainAgent is null)
            {
                return (Result.Failed, ["主代理创建失败（DI 无 IQueryEngine）"]);
            }

            // 插件就绪等待（通知语义 — 八插件后台化后 kernel.Plugins 挂载晚于启动，
            // 不等则 agent 工具 not found 短路、子代理不执行 — E2E 同款根因）
            var bootstrapHandle = session.Services.GetService<JoinCode.App.Modules.McpPluginBootstrapHandle>();
            if (bootstrapHandle is not null)
            {
                await bootstrapHandle.Completion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }

            var consumer = new RecordingSubagentConsumer();
            var controller = new JoinCode.Adapters.SessionController(
                chatService, consumer, new JoinCode.Cli.TurnDiffService(),
                session.SessionId, session.Services, mainAgent: mainAgent);
            var turn = await controller.StreamResponseAsync("帮我检查项目中的README文件", CancellationToken.None).ConfigureAwait(false);
            if (!turn.Succeeded)
            {
                return (Result.Failed, [$"回合失败: {turn.ErrorMessage ?? "(无)"}"]);
            }

            if (consumer.SubagentStarts.Count == 0)
            {
                failures.Add("OnSubagentStarted 未到达（引擎通道→Drain→分流→映射链断裂）");
            }

            if (consumer.SubagentToolUses.Count == 0)
            {
                failures.Add("OnSubagentToolUse 未到达（子代理工具活动事件缺失）");
            }

            if (consumer.SubagentFinishes.Count == 0)
            {
                failures.Add("OnSubagentFinished 未到达（终态事件缺失）");
            }

            return (failures.Count == 0 ? Result.Passed : Result.Failed, failures);
        }
        finally
        {
            if (!mockServer.HasExited)
            {
                try { mockServer.Kill(entireProcessTree: true); } catch { }
            }

            try { Directory.Delete(configDir, recursive: true); } catch { }
        }
    }

    /// <summary>脚本轮次：① 主对话 agent 工具调用 ② 子代理 read 工具 ③ 子代理文本 ④ 主对话收尾。</summary>
    private static string BuildMockConfig()
    {
        const string agentArgs = "{\"name\":\"selftest-sub\",\"subagent_type\":\"executor:search\",\"description\":\"检查README文件\",\"prompt\":\"读取当前目录README并总结\"}";
        return $$"""
{
  "port": 0,
  "default_response": "(script exhausted)",
  "scripted_turns": [
    {
      "thinking_content": null,
      "tool_calls": [
        { "tool_name": "agent", "arguments": "{{agentArgs.Replace("\\", "\\\\").Replace("\"", "\\\"")}}" }
      ],
      "text_response": null,
      "follow_up_text": null,
      "http_status_code": null
    },
    {
      "thinking_content": null,
      "tool_calls": [
        { "tool_name": "read", "arguments": "{\"file_path\":\"README.md\"}" }
      ],
      "text_response": null,
      "follow_up_text": null,
      "http_status_code": null
    },
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "README 已确认存在：JoinCode 是一个 AI 工作流引擎。",
      "follow_up_text": null,
      "http_status_code": null
    },
    {
      "thinking_content": null,
      "tool_calls": null,
      "text_response": "子代理已完成 README 检查。",
      "follow_up_text": null,
      "http_status_code": null
    }
  ]
}
""";
    }

    /// <summary>启动 MockServer 进程并解析就绪 URL（对齐 E2E StartMockServer 先例）。</summary>
    private static System.Diagnostics.Process StartMockServer(string exe, string configPath, out Task<string> readyUrl)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--config \"{configPath}\" --port 0",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };

        var process = new System.Diagnostics.Process { StartInfo = startInfo };
        var readyTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string readyMarker = "[OpenAI]   URL:";
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data) || !e.Data.Contains(readyMarker, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var urlPart = e.Data[(e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase) + readyMarker.Length)..].Trim();
            readyTcs.TrySetResult(urlPart.TrimEnd('/'));
        };
        process.ErrorDataReceived += (_, e) => { };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 MockServer 进程");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        readyUrl = readyTcs.Task;
        return process;
    }

    /// <summary>从 Host 程序集位置向上找主仓 artifacts/bin/OpenAI.MockServer（跨 native_tui→主仓两棵 artifacts 树；
    /// exe 在配置/TFM 子目录 — 对齐 E2E ResolveMockServerPath 递归先例）。</summary>
    private static string? ResolveMockServerPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "JoinCode.OpenAI.MockServer.exe" : "JoinCode.OpenAI.MockServer";
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "artifacts", "bin", "OpenAI.MockServer");
            if (Directory.Exists(candidate))
            {
                return Directory.EnumerateFiles(candidate, exeName, SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Contains("Debug"))
                    .FirstOrDefault();
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    /// <summary>记录子代理三回调的消费面（selftest 断言用 — 对齐 E2E RecordingSubagentConsumer 形态）。</summary>
    private sealed class RecordingSubagentConsumer : JoinCode.Abstractions.Interfaces.IEventConsumer
    {
        public List<(string SubagentId, string AgentType, string Description)> SubagentStarts { get; } = [];
        public List<(string SubagentId, string ToolName, string Summary, int TokenGain)> SubagentToolUses { get; } = [];
        public List<(string SubagentId, bool IsError, int TokenTotal)> SubagentFinishes { get; } = [];

        public void OnText(string content) { }
        public void OnThinking(string thinking) { }
        public void OnToolStart(string toolName, string? toolCallId, string? arguments) { }
        public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, JoinCode.Abstractions.Models.Diff.StructuredPatchHunk[]? patch) { }
        public void OnToolProgress(string toolName, string progressType, string? progressMessage) { }
        public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) { }
        public void OnTimingSummary(string summary) { }
        public void OnDone(TokenUsage? usage, string? modelId) { }
        public void OnSubagentStarted(string subagentId, string agentType, string description) => SubagentStarts.Add((subagentId, agentType, description));
        public void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) => SubagentToolUses.Add((subagentId, toolName, summary, tokenGain));
        public void OnSubagentFinished(string subagentId, bool isError, int tokenTotal) => SubagentFinishes.Add((subagentId, isError, tokenTotal));
    }
}
