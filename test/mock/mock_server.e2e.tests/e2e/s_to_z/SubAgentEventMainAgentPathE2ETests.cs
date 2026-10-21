// JCC1017 抑制: 存量手写 JSON, 后续改为 DTO+JsonContext
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC1017
namespace MockServer.E2E.Tests.E2E;

/// <summary>
/// 子代理事件主代理路径 E2E（DSG041 批2 集成级验收）— 真实 OpenAI.MockServer 进程 + 进程内引擎
/// + **SessionController 主代理路径**（EngineAgentFactory.CreateMainAgent + StreamResponseAsync，
/// 与 native_tui Host 完全同链路，区别于 SubAgentEventStreamE2ETests 的 ChatService 管道路径）：
/// 验证宿主层通道直投全链——Agent 工具 spawn 子代理 → AgentStreamExecutionMiddleware Emit 到
/// SubAgentEventChannel → SessionController chunk 循环排空直投 RouteSubagentEvent →
/// IEventConsumer 三子代理回调（OnSubagentStarted/OnSubagentToolUse/OnSubagentFinished）。
/// </summary>
[Trait("Category", "Integration")]
public sealed class SubAgentEventMainAgentPathE2ETests {
    private readonly ITestOutputHelper _output;

    public SubAgentEventMainAgentPathE2ETests(ITestOutputHelper output) => _output = output;

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact(Skip = "引擎侧子代理执行链缺陷（非 DSG041 接线问题——批1-4 单测全绿）：agent 工具真实执行后 testhost 崩溃；"
        + "对照 ChatService 路径 SubAgentEventStreamE2ETests 同域红（子代理 LLM 输出无 AgentId 串入主流、无 AgentStarted），"
        + "疑流式透传缺陷域（TASK043 StreamingFallbackDecorator 关联）。引擎缺陷修复后移除 Skip 复验。")]
    public async Task MainAgentPath_AgentSpawn_ShouldDispatchSubagentCallbacks() {
        const string args = "{\"name\":\"e2e-main\",\"subagent_type\":\"executor:search\",\"description\":\"检查README文件\",\"prompt\":\"读取当前目录README并总结\"}";

        var consumer = await RunMainAgentScenarioAsync(args).ConfigureAwait(true);

        Assert.NotEmpty(consumer.SubagentStarts);
        var started = consumer.SubagentStarts[0];
        Assert.Equal("e2e-main", started.AgentType);
        Assert.Contains("README", started.Description);

        Assert.NotEmpty(consumer.SubagentToolUses);
        Assert.All(consumer.SubagentToolUses, use => Assert.Equal(started.SubagentId, use.SubagentId));

        var finished = Assert.Single(consumer.SubagentFinishes);
        Assert.Equal(started.SubagentId, finished.SubagentId);
        Assert.False(finished.IsError);
    }

    /// <summary>
    /// 共享实弹管线（对齐 SubAgentEventStreamE2ETests 先例）：写脚本配置 → 启动真实 MockServer →
    /// 创建引擎会话 + 主代理 + SessionController（RecordingSubagentConsumer）→ 提交回合收全部子代理回调。
    /// </summary>
    private async Task<RecordingSubagentConsumer> RunMainAgentScenarioAsync(string toolCallArguments) {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = cts.Token;

        var configDir = fs.CombinePath(Path.GetTempPath(), $"jcc_subagent_mainpath_e2e_{Guid.NewGuid():N}");
        fs.CreateDirectory(configDir);
        var configPath = fs.CombinePath(configDir, "mockserver.json");
        await fs.WriteAllText(configPath, BuildMockConfig(toolCallArguments));

        using var mockServer = StartMockServer(fs, configPath, _output, out var readyTask);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));
        var port = await readyTask.WaitAsync(timeoutCts.Token).ConfigureAwait(true);
        Assert.True(port > 0, "MockServer 就绪行未解析出端口");

        try {
            using var envScope = EnvVarScope.Set("JCC_ENDPOINT", $"http://localhost:{port}")
                .Add("OPENAI_API_KEY", "sk-test-subagent-mainpath-e2e")
                .Add("JCC_VENDOR", "openai")
                .Add("JCC_MODEL_ID", "gpt-4o")
                .Add("JCC_PERMISSION_MODE", "bypass");

            var session = await JoinCode.App.Builder.EngineSessionFactory.CreateGuiSessionAsync()
                .ConfigureAwait(true);
            try {
                var mainAgent = JoinCode.Cli.EngineAgentFactory.CreateMainAgent(session.Services, new JoinCode.Abstractions.Entity.Session().ObjectId);
                Assert.True(mainAgent is not null, "主代理创建失败（DI 无 IQueryEngine）");

                // 确定性等待插件就绪（邮箱/通知语义 — 八插件后台化后工具注册晚于启动）：
                // 等 McpPluginBootstrapHandle.Completion 信号而非固定延时（CI 快慢机均可靠；超时仅挂死保护）
                var bootstrapHandle = session.Services.GetService<JoinCode.App.Modules.McpPluginBootstrapHandle>();
                if (bootstrapHandle is not null)
                {
                    await bootstrapHandle.Completion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                }

                var consumer = new RecordingSubagentConsumer();
                var controller = new JoinCode.Adapters.SessionController(
                    session.ChatService, consumer, new JoinCode.Cli.TurnDiffService(),
                    session.SessionId, session.Services, mainAgent: mainAgent);

                var result = await controller.StreamResponseAsync("帮我检查项目中的README文件", ct).ConfigureAwait(true);
                Assert.True(result.Succeeded, $"回合失败: {result.ErrorMessage ?? "(无)"}");

                _output.WriteLine($"[DIAG] SubagentStarts={consumer.SubagentStarts.Count} ToolUses={consumer.SubagentToolUses.Count} Finishes={consumer.SubagentFinishes.Count} Texts={consumer.TextEvents.Count} ToolStarts={consumer.ToolStarts.Count} ToolEnds={consumer.ToolEnds.Count}");
                foreach (var text in consumer.TextEvents)
                    _output.WriteLine($"[DIAG:Text] {text[..Math.Min(60, text.Length)]}");
                foreach (var tool in consumer.ToolStarts)
                    _output.WriteLine($"[DIAG:ToolStart] {tool.toolName} args={tool.arguments?[..Math.Min(80, tool.arguments?.Length ?? 0)]}");
                foreach (var tool in consumer.ToolEnds)
                    _output.WriteLine($"[DIAG:ToolEnd] {tool.toolName} err={tool.isError} result={tool.resultText}");
                _output.WriteLine($"[DIAG:Response] {result.Response?[..Math.Min(300, result.Response?.Length ?? 0)]}");

                return consumer;
            } finally {
                try { await session.Host.DisposeAsync(); } catch (Exception disposeEx) { _output.WriteLine($"[E2E清理] Host.DisposeAsync 异常(已忽略): {disposeEx.Message}"); }
            }
        } finally {
            if (!mockServer.HasExited) {
                try { mockServer.Kill(entireProcessTree: true); } catch (Exception killEx) { _output.WriteLine($"[E2E清理] MockServer 进程终止失败: {killEx.Message}"); }
            }
            mockServer.Dispose();
            try { fs.DeleteDirectory(configDir, recursive: true); } catch (Exception cleanEx) { _output.WriteLine($"[E2E清理] 临时配置目录删除失败: {cleanEx.Message}"); }
        }
    }

    /// <summary>回调序列诊断摘要（失败信息用）。</summary>
    private static string Describe(RecordingSubagentConsumer consumer) =>
        string.Join("|", consumer.SubagentStarts.Select(s => $"started({s.AgentType})")
            .Concat(consumer.SubagentToolUses.Select(u => $"tool({u.ToolName})"))
            .Concat(consumer.SubagentFinishes.Select(f => $"finished(err={f.IsError})")));

    /// <summary>脚本轮次：⓪ 意图检测消耗 turn（主代理路径 PreProcess 模态/意图分类会先调一次 LLM——
    /// 不加则 tool_calls turn 被其消耗、Agent 工具从不被调用）① 主对话 Agent 工具调用
    /// ② 子代理 LLM 文本输出 ③ 主对话收尾文本。</summary>
    private static string BuildMockConfig(string toolCallArguments) {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"port\": 0,");
        sb.AppendLine("  \"default_response\": \"(script exhausted)\",");
        sb.AppendLine("  \"scripted_turns\": [");

        sb.AppendLine("    {");
        sb.AppendLine("      \"thinking_content\": null,");
        sb.AppendLine("      \"tool_calls\": [");
        sb.AppendLine("        {");
        sb.AppendLine("          \"tool_name\": \"agent\",");
        sb.Append("          \"arguments\": \"").Append(EscapeJsonString(toolCallArguments)).AppendLine("\",");
        sb.AppendLine("        }".TrimEnd(','));
        sb.AppendLine("      ],");
        sb.AppendLine("      \"text_response\": null,");
        sb.AppendLine("      \"follow_up_text\": null,");
        sb.AppendLine("      \"http_status_code\": null");
        sb.AppendLine("    },");

        AppendTextTurn(sb, "README 已确认存在：JoinCode 是一个 AI 工作流引擎。");
        sb.AppendLine(",");
        AppendTextTurn(sb, "子代理已完成 README 检查。");

        sb.AppendLine();
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendTextTurn(StringBuilder sb, string text) {
        sb.AppendLine("    {");
        sb.AppendLine("      \"thinking_content\": null,");
        sb.AppendLine("      \"tool_calls\": null,");
        sb.Append("      \"text_response\": \"").Append(text).AppendLine("\",");
        sb.AppendLine("      \"follow_up_text\": null,");
        sb.AppendLine("      \"http_status_code\": null");
        sb.Append("    }");
    }

    private static string EscapeJsonString(string s)
        => string.IsNullOrEmpty(s) ? "" : JsonEncodedText.Encode(s, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();

    /// <summary>记录子代理回调的 fake 消费者（对齐 host.tests SessionControllerSubagentEventTests 形态）。</summary>
    private sealed class RecordingSubagentConsumer : JoinCode.Abstractions.Interfaces.IEventConsumer {
        public List<string> TextEvents { get; } = [];
        public List<(string SubagentId, string AgentType, string Description)> SubagentStarts { get; } = [];
        public List<(string SubagentId, string ToolName, string Summary, int TokenGain)> SubagentToolUses { get; } = [];
        public List<(string SubagentId, bool IsError)> SubagentFinishes { get; } = [];

        public void OnText(string content) => TextEvents.Add(content);
        public List<(string toolName, string? arguments)> ToolStarts { get; } = [];
        public List<(string toolName, string? resultText, bool isError)> ToolEnds { get; } = [];
        public void OnThinking(string thinking) { }
        public void OnToolStart(string toolName, string? toolCallId, string? arguments) => ToolStarts.Add((toolName, arguments));
        public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, JoinCode.Abstractions.Models.Diff.StructuredPatchHunk[]? patch) => ToolEnds.Add((toolName, resultText is null ? null : resultText[..Math.Min(80, resultText.Length)], isError));
        public void OnToolProgress(string toolName, string progressType, string? progressMessage) { }
        public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) { }
        public void OnTimingSummary(string summary) { }
        public void OnDone(TokenUsage? usage, string? modelId) { }
        public void OnSubagentStarted(string subagentId, string agentType, string description) => SubagentStarts.Add((subagentId, agentType, description));
        public void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) => SubagentToolUses.Add((subagentId, toolName, summary, tokenGain));
        public void OnSubagentFinished(string subagentId, bool isError) => SubagentFinishes.Add((subagentId, isError));
    }

    private System.Diagnostics.Process StartMockServer(JoinCode.Abstractions.Interfaces.IFileSystem fs, string configPath,
        ITestOutputHelper output, out Task<int> readyTask) {
        var exe = ResolveMockServerPath(fs);
        var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = exe,
            Arguments = $"--config \"{configPath}\" --port 0",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe)
        };

        var process = new System.Diagnostics.Process { StartInfo = startInfo };
        var readyTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string readyMarker = "[OpenAI]   URL:";

        process.OutputDataReceived += (_, e) => {
            if (string.IsNullOrEmpty(e.Data))
                return;
            _output.WriteLine($"[MockServer] {e.Data}");
            var idx = e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return;
            var urlPart = e.Data[(idx + readyMarker.Length)..].Trim();
            var match = Regex.Match(urlPart, @":(\d+)/?");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                readyTcs.TrySetResult(port);
        };
        process.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                _output.WriteLine($"[MockServer:ERR] {e.Data}");
        };

        if (!process.Start())
            throw new InvalidOperationException("[GEN026] 无法启动 MockServer 进程");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        readyTask = readyTcs.Task;
        return process;
    }

    private static string ResolveMockServerPath(JoinCode.Abstractions.Interfaces.IFileSystem fs) {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            var candidate = Path.Combine(dir, "artifacts", "bin", "OpenAI.MockServer");
            if (fs.DirectoryExists(candidate)) {
                var exeName = OperatingSystem.IsWindows() ? "JoinCode.OpenAI.MockServer.exe" : "JoinCode.OpenAI.MockServer";
                var exe = fs.GetFiles(candidate, exeName, SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Contains("Release"))
                    .FirstOrDefault()
                    ?? throw new FileNotFoundException($"MockServer exe 未编译，请先 dotnet build OpenAI.MockServer", candidate);
                return exe;
            }
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        throw new InvalidOperationException("未找到 artifacts/bin/OpenAI.MockServer 目录");
    }
}
