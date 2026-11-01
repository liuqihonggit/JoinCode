// JCC1017 抑制: 存量手写 JSON, 后续改为 DTO+JsonContext
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC1017
namespace MockServer.E2E.Tests.E2E;

/// <summary>
/// 流式增量透传实弹 E2E（TASK043 / ADR 0146 验收）— 真实 OpenAI.MockServer 进程 +
/// GUI 同源引擎会话（EngineSessionFactory.CreateGuiSessionAsync）：
/// MockServer 文本流按 word 逐分片下发（每片 20ms — KestrelMockServer 流式路径），
/// 断言消费方（ChatService.StreamWithEventsAsync）逐段收到 Content 事件——
/// 首末分片到达时间差反映真实流时长（全量收集缺陷形态下全部事件同刻整发，时间差≈0）。
/// </summary>
[Collection("SubAgentEventSerial")]
[Trait("Category", "Integration")]
public sealed class StreamingIncrementalE2ETests {
    private readonly ITestOutputHelper _output;

    public StreamingIncrementalE2ETests(ITestOutputHelper output) => _output = output;

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task LongTextStream_ContentArrivesIncrementally_NotBatchedAtEnd() {
        var timings = await RunLongTextScenarioAsync().ConfigureAwait(true);

        timings.Count.Should().BeGreaterThan(1,
            "长文本流应逐分片到达（整块单事件 = 透传断裂）");

        var span = timings[^1] - timings[0];
        timings[^1].Should().BeGreaterThanOrEqualTo(timings[0]);
        span.Should().BeGreaterThanOrEqualTo(200,
            $"首末 Content 分片到达时间差应反映真实流时长（50 分片×20ms≈1000ms），实际 {span}ms — " +
            "时间差≈0 意味全量收集后整发（TASK043 缺陷形态回归）");
        _output.WriteLine($"[STREAM] 分片数={timings.Count} 首分片={timings[0]}ms 末分片={timings[^1]}ms 跨度={span}ms");
    }

    /// <summary>
    /// 共享实弹管线（对齐 SubAgentEventStreamE2ETests 先例）：长文本脚本 → 真实 MockServer →
    /// GUI 同源引擎会话 → 逐事件记录 Content 到达时刻（Stopwatch 毫秒）。
    /// </summary>
    private async Task<List<long>> RunLongTextScenarioAsync() {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = cts.Token;

        var configDir = fs.CombinePath(Path.GetTempPath(), $"jcc_stream_incremental_e2e_{Guid.NewGuid():N}");
        fs.CreateDirectory(configDir);
        var configPath = fs.CombinePath(configDir, "mockserver.json");
        await fs.WriteAllText(configPath, BuildMockConfig());

        using var mockServer = StartMockServer(fs, configPath, out var readyTask);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));
        var port = await readyTask.WaitAsync(timeoutCts.Token).ConfigureAwait(true);
        Assert.True(port > 0, "MockServer 就绪行未解析出端口");

        try {
            using var envScope = EnvVarScope.Set("JCC_ENDPOINT", $"http://localhost:{port}")
                .Add("OPENAI_API_KEY", "sk-test-stream-e2e")
                .Add("JCC_VENDOR", "openai")
                .Add("JCC_MODEL_ID", "gpt-4o")
                .Add("JCC_PERMISSION_MODE", "bypass");

            var session = await JoinCode.App.Builder.EngineSessionFactory.CreateGuiSessionAsync()
                .ConfigureAwait(true);
            try {
                var bootstrapHandle = session.Services.GetService<JoinCode.App.Modules.McpPluginBootstrapHandle>();
                if (bootstrapHandle is not null)
                {
                    await bootstrapHandle.Completion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var timings = new List<long>();
                await foreach (var evt in session.ChatService.StreamWithEventsAsync(
                    "给我讲一个很长的故事", ct).ConfigureAwait(true)) {
                    if (evt.Type == ChatStreamEventType.Content) {
                        timings.Add(stopwatch.ElapsedMilliseconds);
                    }
                }
                return timings;
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

    /// <summary>长文本脚本（50 词 — MockServer 按 word 逐分片×20ms 下发 ≈1s 流）。</summary>
    private static string BuildMockConfig() {
        var words = Enumerable.Range(1, 50).Select(i => $"word{i}");
        var longText = string.Join(" ", words);
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"port\": 0,");
        sb.AppendLine("  \"default_response\": \"(script exhausted)\",");
        sb.AppendLine("  \"scripted_turns\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"thinking_content\": null,");
        sb.AppendLine("      \"tool_calls\": null,");
        sb.Append("      \"text_response\": \"").Append(longText).AppendLine("\",");
        sb.AppendLine("      \"follow_up_text\": null,");
        sb.AppendLine("      \"http_status_code\": null");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private Process StartMockServer(JoinCode.Abstractions.Interfaces.IFileSystem fs, string configPath,
        out Task<int> readyTask) {
        var exe = ResolveMockServerPath(fs);
        var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = exe,
            Arguments = $"--config \"{configPath}\" --port 0 --no-open-browser",
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
