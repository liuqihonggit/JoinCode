using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using JoinCode.Abstractions.LLM.Chat;

namespace MockServer.E2E.Tests.E2E;

/// <summary>
/// 子代理事件流实弹 E2E — 真实 OpenAI.MockServer 进程 + 进程内 GUI 同源引擎
/// （EngineSessionFactory.CreateGuiSessionAsync，与 JoinCodeGui.JccChatSession 完全同链路），
/// 验证 Agent 工具触发的子代理事件（AgentStarted/带身份活动/AgentFinished）
/// 真能从引擎主事件流 yield 出来。这是 GUI 多 subAgent 显示的端到端数据源契约。
/// </summary>
[Trait("Category", "Integration")]
public sealed class SubAgentEventStreamE2ETests
{
    private readonly ITestOutputHelper _output;

    public SubAgentEventStreamE2ETests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AgentToolSpawn_ShouldEmitAgentEventsThroughMainStream()
    {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token;

        // 1. 写 MockServer 脚本配置：主对话触发 Agent 工具 → 子代理 LLM 轮次 → 主对话收尾文本
        var configDir = fs.CombinePath(Path.GetTempPath(), $"jcc_subagent_e2e_{Guid.NewGuid():N}");
        fs.CreateDirectory(configDir);
        var configPath = fs.CombinePath(configDir, "mockserver.json");
        fs.WriteAllText(configPath, BuildMockConfig());

        // 2. 启动真实 MockServer 进程（--port 0 自动分配，从就绪行解析端口）
        using var mockServer = StartMockServer(fs, configPath, _output, out var readyTask);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));
        var port = await readyTask.WaitAsync(timeoutCts.Token).ConfigureAwait(true);
        Assert.True(port > 0, "MockServer 就绪行未解析出端口");

        try
        {
            // 3. 注入引擎环境变量（对齐 AGENTS.md jcc 环境变量表）+ 权限 bypass 免弹窗
            Environment.SetEnvironmentVariable("JCC_ENDPOINT", $"http://localhost:{port}");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-test-subagent-e2e");
            Environment.SetEnvironmentVariable("JCC_VENDOR", "openai");
            Environment.SetEnvironmentVariable("JCC_MODEL_ID", "gpt-4o");
            Environment.SetEnvironmentVariable("JCC_PERMISSION_MODE", "bypass");

            // 4. 创建与 GUI 完全同源的引擎会话（无 Avalonia 模块，headless 安全）。
            // 工厂级令牌传 None：Host 内部服务（BuildQueueService 等）会注册该令牌，
            // 外部短命 CTS 先于 Host 处置会导致 Dispose 时 ObjectDisposedException
            var session = await JoinCode.App.Builder.EngineSessionFactory.CreateGuiSessionAsync()
                .ConfigureAwait(true);
            try
            {
                // 5. 消费主事件流 — 断言子代理事件序列
                var events = new List<ChatStreamEvent>();
                await foreach (var evt in session.ChatService.StreamWithEventsAsync(
                    "帮我检查项目中的README文件", ct).ConfigureAwait(true))
                {
                    events.Add(evt);
                    if (evt.Type == ChatStreamEventType.Content)
                        _output.WriteLine($"[EVT:Content] {evt.Content?[..Math.Min(60, evt.Content?.Length ?? 0)]}");
                    else
                        _output.WriteLine($"[EVT:{evt.Type}] agent={evt.AgentId} tool={evt.ToolName}");
                }

                var started = events.FirstOrDefault(e => e.Type == ChatStreamEventType.AgentStarted);
                Assert.True(started is not null,
                    "主事件流应包含 AgentStarted 事件。" +
                    $"实际事件类型序列: {string.Join(",", events.Select(e => e.Type))}");

                started!.AgentName.Should().Be("e2e-sub");
                started.AgentDescription.Should().Contain("README");
                started.IsSubAgentActivity.Should().BeTrue();

                var agentActivities = events.Where(e =>
                    e.IsSubAgentActivity &&
                    e.Type is not (ChatStreamEventType.AgentStarted or ChatStreamEventType.AgentFinished)).ToList();
                agentActivities.Should().NotBeEmpty("子代理内部活动（Content/工具调用）应以 AgentId 标记流出");
                agentActivities.Select(a => a.AgentId).Should().OnlyContain(id => id == started.AgentId);
                agentActivities.Should().Contain(e => e.Type == ChatStreamEventType.Content,
                    "子代理的文本输出应作为带身份的活动事件出现");

                var finished = events.LastOrDefault(e => e.Type == ChatStreamEventType.AgentFinished);
                Assert.True(finished is not null, "主事件流应包含 AgentFinished 终态事件");
                finished!.AgentId.Should().Be(started.AgentId);
                finished.AgentSuccess.Should().BeTrue();

                // 主对话流不受污染：存在 AgentId 为 null 的正文/工具事件
                events.Should().Contain(e => !e.IsSubAgentActivity && e.Type == ChatStreamEventType.ToolCallStart,
                    "主对话应有自己的 Agent 工具调用事件（AgentId 为 null）");
            }
            finally
            {
                // Host 内部服务（BuildQueueService）的 Dispose 链路存在二次处置敏感，
                // 此处仅尽力清理，不得掩盖 try 块中的原始断言/运行时异常
                try { session.Host.Dispose(); }
                catch (Exception disposeEx) { _output.WriteLine($"[E2E清理] Host.Dispose 异常(已忽略): {disposeEx.Message}"); }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_ENDPOINT", null);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("JCC_VENDOR", null);
            Environment.SetEnvironmentVariable("JCC_MODEL_ID", null);
            Environment.SetEnvironmentVariable("JCC_PERMISSION_MODE", null);

            if (!mockServer.HasExited)
            {
                try { mockServer.Kill(entireProcessTree: true); }
                catch (Exception killEx) { _output.WriteLine($"[E2E清理] MockServer 进程终止失败: {killEx.Message}"); }
            }
            mockServer.Dispose();
            try { fs.DeleteDirectory(configDir, recursive: true); }
            catch (Exception cleanEx) { _output.WriteLine($"[E2E清理] 临时配置目录删除失败: {cleanEx.Message}"); }
        }
    }

    /// <summary>
    /// 构建脚本轮次：① 主对话返回 Agent 工具调用 ② 子代理 LLM 文本输出 ③ 主对话收尾文本。
    /// MockServer 按请求顺序消耗 scripted_turns。
    /// </summary>
    private static string BuildMockConfig()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"port\": 0,");
        sb.AppendLine("  \"default_response\": \"(script exhausted)\",");
        sb.AppendLine("  \"scripted_turns\": [");

        // Turn 1: 主对话 → Agent 工具调用。
        // 必须显式传 subagent_type：为空时走 Fork 后台短路路径（AgentForkMiddleware），
        // 不经过流式中间件，GUI 收不到子代理事件
        sb.AppendLine("    {");
        sb.AppendLine("      \"thinking_content\": null,");
        sb.AppendLine("      \"tool_calls\": [");
        sb.AppendLine("        {");
        sb.AppendLine("          \"tool_name\": \"Agent\",");
        sb.AppendLine("          \"arguments\": \"{\\\"name\\\":\\\"e2e-sub\\\",\\\"subagent_type\\\":\\\"executor:search\\\",\\\"description\\\":\\\"检查README文件\\\",\\\"prompt\\\":\\\"读取当前目录README并总结\\\"}\"");
        sb.AppendLine("        }");
        sb.AppendLine("      ],");
        sb.AppendLine("      \"text_response\": null,");
        sb.AppendLine("      \"follow_up_text\": null,");
        sb.AppendLine("      \"http_status_code\": null");
        sb.AppendLine("    },");

        // Turn 2: 子代理自己的 LLM 调用 → 文本输出
        sb.AppendLine("    {");
        sb.AppendLine("      \"thinking_content\": null,");
        sb.AppendLine("      \"tool_calls\": null,");
        sb.AppendLine("      \"text_response\": \"README 已确认存在：JoinCode 是一个 AI 工作流引擎。\",");
        sb.AppendLine("      \"follow_up_text\": null,");
        sb.AppendLine("      \"http_status_code\": null");
        sb.AppendLine("    },");

        // Turn 3: 主对话拿到工具结果后的收尾文本
        sb.AppendLine("    {");
        sb.AppendLine("      \"thinking_content\": null,");
        sb.AppendLine("      \"tool_calls\": null,");
        sb.AppendLine("      \"text_response\": \"子代理已完成 README 检查。\",");
        sb.AppendLine("      \"follow_up_text\": null,");
        sb.AppendLine("      \"http_status_code\": null");
        sb.AppendLine("    }");

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static Process StartMockServer(JoinCode.Abstractions.Interfaces.IFileSystem fs, string configPath,
        ITestOutputHelper output, out Task<int> readyTask)
    {
        var exe = ResolveMockServerPath(fs);
        var startInfo = new ProcessStartInfo
        {
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

        var process = new Process { StartInfo = startInfo };
        var readyTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string readyMarker = "[OpenAI]   URL:";

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            output.WriteLine($"[MockServer] {e.Data}");
            var idx = e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return;
            var urlPart = e.Data[(idx + readyMarker.Length)..].Trim();
            var match = Regex.Match(urlPart, @":(\d+)/?");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                readyTcs.TrySetResult(port);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                output.WriteLine($"[MockServer:ERR] {e.Data}");
        };

        if (!process.Start())
            throw new InvalidOperationException("[GEN026] 无法启动 MockServer 进程");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        readyTask = readyTcs.Task;
        return process;
    }

    private static string ResolveMockServerPath(JoinCode.Abstractions.Interfaces.IFileSystem fs)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "artifacts", "bin", "OpenAI.MockServer");
            if (fs.DirectoryExists(candidate))
            {
                var exe = fs.GetFiles(candidate, "JoinCode.OpenAI.MockServer.exe", SearchOption.AllDirectories)
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


