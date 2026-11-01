namespace MockAiResponder;

/// <summary>
/// mock_ai_responder — 轮询 MockServer 控制面，按 jcc 请求内容生成模拟 AI 响应并注入。
/// 用法：dotnet run -- --mock-url http://localhost:8080
/// </summary>
internal static class Program {
    private static async Task Main(string[] args) {
        var mockUrl = ParseArg(args, "--mock-url", "http://localhost:8080").TrimEnd('/');
        var controlUrl = mockUrl + "/__control__";
        var pollMs = int.Parse(ParseArg(args, "--poll-ms", "100"));
        var pollInterval = TimeSpan.FromMilliseconds(pollMs);

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var lastSeenIndex = -1;

        Console.WriteLine($"[Responder] MockServer: {mockUrl}");
        Console.WriteLine($"[Responder] 控制面:   {controlUrl}");
        Console.WriteLine($"[Responder] 轮询间隔: {pollMs}ms");
        Console.WriteLine("[Responder] 就绪，等待 jcc 请求... (Ctrl+C 退出)");
        Console.WriteLine(new string('-', 60));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // 睡眠/唤醒自动恢复 — 唤醒后取消卡住的 HTTP 请求，重建 HttpClient
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var resumeCts = new CancellationTokenSource();
        var resumeRequested = false;

        if (OperatingSystem.IsWindows()) {
            RegisterPowerResumeHandler(() => {
                Console.WriteLine("[Responder] 检测到系统唤醒，重建连接...");
                resumeRequested = true;
                resumeCts.Cancel();
            });
        }

        while (!cts.Token.IsCancellationRequested) {
            try {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, resumeCts.Token);
                var requests = await http.GetFromJsonAsync<List<CapturedRequestDto>>($"{controlUrl}/requests", jsonOpts, linked.Token);
                if (requests is null) continue;

                var newRequests = requests.Where(r => r.Index > lastSeenIndex).OrderBy(r => r.Index).ToList();
                foreach (var req in newRequests) {
                    lastSeenIndex = Math.Max(lastSeenIndex, req.Index);

                    var (response, description) = IntentMatcher.Generate(req.Body);
                    var payload = JsonSerializer.Serialize(response, jsonOpts);

                    await http.PostAsync($"{controlUrl}/inject",
                        new StringContent(payload, Encoding.UTF8, "application/json"), linked.Token);

                    var userMsg = IntentMatcher.ExtractLastUserMessage(req.Body);
                    var preview = userMsg.Length > 80 ? userMsg[..80] + "..." : userMsg;
                    Console.WriteLine($"[#{req.Index}] 用户: {preview}");
                    Console.WriteLine($"[#{req.Index}] 回应: {description}");
                    Console.WriteLine();
                }
            } catch (OperationCanceledException) when (resumeRequested && !cts.Token.IsCancellationRequested) {
                // 系统唤醒 — 重建 HttpClient（旧连接池有坏连接），重置状态
                http.Dispose();
                http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                resumeCts.Dispose();
                resumeCts = new CancellationTokenSource();
                resumeRequested = false;
                lastSeenIndex = -1;
                Console.WriteLine("[Responder] 连接已重建，继续轮询...");
                continue;
            } catch (OperationCanceledException) {
                break;
            } catch (HttpRequestException ex) {
                Console.Error.WriteLine($"[Responder] 连接失败: {ex.Message} (MockServer 未启动?)");
            } catch (Exception ex) {
                Console.Error.WriteLine($"[Responder] 错误: {ex.Message}");
            }
            try {
                await Task.Delay(pollInterval, cts.Token);
            } catch (OperationCanceledException) {
                break;
            }
        }

        http.Dispose();
        resumeCts.Dispose();
        Console.WriteLine("[Responder] 已退出。");
    }

    private static string ParseArg(string[] args, string name, string defaultValue) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] == name) return args[i + 1];
        }
        return defaultValue;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RegisterPowerResumeHandler(Action onResume) {
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) => {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                onResume();
        };
    }
}
