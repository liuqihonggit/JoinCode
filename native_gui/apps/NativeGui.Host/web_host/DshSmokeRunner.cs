namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 宿主无窗口冒烟 — 启动真实 HttpListener/WS 宿主并走完整主路径：
/// 首页注入校验 → session/create → $events + session/follow 双流 → goals 创建/暂停（激活翻转事件）
/// → 并发 RPC 压测；全部通过退出 0，失败打印原因并退出 1。供 CI/无桌面环境验证 web_host 装配链路。
/// </summary>
internal static class DshSmokeRunner {
    private const string EventsStreamId = "smoke-events";
    private const string FollowStreamId = "smoke-follow";
    private const string Objective = "TASK099 无锁宿主冒烟目标";

    /// <summary>执行冒烟；0=全部通过，1=失败。</summary>
    internal static async Task<int> RunAsync() {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jcc-dsh-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try {
            return await RunCoreAsync(tempRoot);
        } catch (Exception ex) {
            Console.WriteLine($"[dsh-smoke] FAIL {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>冒烟主路径：隔离数据目录 → 真实宿主 + WS 客户端 → 逐步校验。</summary>
    /// <param name="tempRoot">隔离数据根目录（宿主设置/目标/会话日志全部落在这里）</param>
    private static async Task<int> RunCoreAsync(string tempRoot) {
        var fileSystem = new PhysicalFileSystem();
        var appData = AppDataPaths.CreateForTest(appDataFolder: tempRoot);
        using var transcripts = new TranscriptStore(fileSystem, Path.Combine(tempRoot, "sessions"));
        var services = new DshHostServices(
            fileSystem,
            workspaceFilePath: Path.Combine(tempRoot, "dsh-workspaces.json"),
            journalDirectory: Path.Combine(tempRoot, "journal"),
            appDataPaths: appData);
        services.Sessions.AttachTranscripts(transcripts);
        await using var host = DshWebHost.Start(services);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{host.Port}/api/remote.mux"), lifetime.Token);

        var pendingQueue = new SmokePendingQueue();
        services.PendingConfirmations.Attach(pendingQueue, new SmokePendingResolver(pendingQueue));

        await CheckHomepageAsync(http, host.BaseUrl, lifetime.Token);
        await CheckPendingConfirmationsAsync(http, host.BaseUrl, pendingQueue, lifetime.Token);
        var sessionId = await CreateSessionAsync(http, tempRoot, lifetime.Token);
        await CheckSessionListedAsync(http, sessionId, lifetime.Token);
        await OpenEventStreamAsync(socket, lifetime.Token);
        await OpenSessionFollowAsync(socket, sessionId, lifetime.Token);
        var reference = await CreateGoalAsync(http, sessionId, lifetime.Token);
        await PauseGoalAsync(http, sessionId, reference, lifetime.Token);
        await ExpectActivationFrameAsync(socket, sessionId, lifetime.Token);
        await CheckConcurrentReadsAsync(http, sessionId, lifetime.Token);
        Console.WriteLine("[dsh-smoke] PASS");
        return 0;
    }

    /// <summary>首页校验：返回 HTML 且包含宿主注入标记。</summary>
    private static async Task CheckHomepageAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken) {
        var html = await http.GetStringAsync(baseUrl, cancellationToken);
        var hasHtml = html.Contains("<html", StringComparison.OrdinalIgnoreCase);
        var hasHostInjection = html.Contains("dsh", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"[dsh-smoke] GET {baseUrl} len={html.Length} html={hasHtml} injection={hasHostInjection}");
        if (!hasHtml || !hasHostInjection) {
            throw new InvalidOperationException("首页缺少 HTML 或宿主注入标记，资产解包/注入链路异常");
        }
    }

    /// <summary>待确认收件箱真实 HTTP 链路：列表 → 单条批准 → 列表清空 → UI 脚本可注入。</summary>
    private static async Task CheckPendingConfirmationsAsync(
        HttpClient http, string baseUrl, SmokePendingQueue queue, CancellationToken cancellationToken) {
        var ticket = queue.Enqueue(
            "smoke-agent",
            "Bash",
            "冒烟：后台任务请求确认",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
                ["command"] = JsonDocument.Parse("\"npm test\"").RootElement.Clone(),
            });

        var listValue = await CallAsync(http, "pendingConfirmations/list", null, cancellationToken);
        Console.WriteLine("[dsh-smoke] pendingConfirmations/list ok");
        var list = listValue.Deserialize(DshJsonContext.Default.DshPendingConfirmationList)
            ?? throw new InvalidOperationException("pendingConfirmations/list 返回空结果");
        if (!list.Available || list.Items.Count != 1 || list.Items[0].Id != ticket.Id) {
            throw new InvalidOperationException($"pendingConfirmations/list 异常：available={list.Available}, count={list.Items.Count}");
        }

        var resolveArgs = JsonSerializer.SerializeToElement(
            new DshPendingConfirmationResolveArgs { Id = ticket.Id, Decision = "approve" },
            DshJsonContext.Default.DshPendingConfirmationResolveArgs);
        var resolveValue = await CallAsync(http, "pendingConfirmations/resolve", resolveArgs, cancellationToken);
        Console.WriteLine("[dsh-smoke] pendingConfirmations/resolve ok");
        var resolve = resolveValue.Deserialize(DshJsonContext.Default.DshPendingConfirmationResolveResult)
            ?? throw new InvalidOperationException("pendingConfirmations/resolve 返回空结果");
        if (!resolve.Resolved || resolve.Remaining != 0) {
            throw new InvalidOperationException($"pendingConfirmations/resolve 未结算：resolved={resolve.Resolved}, remaining={resolve.Remaining}");
        }

        var afterValue = await CallAsync(http, "pendingConfirmations/list", null, cancellationToken);
        Console.WriteLine("[dsh-smoke] pendingConfirmations/list after ok");
        var after = afterValue.Deserialize(DshJsonContext.Default.DshPendingConfirmationList)
            ?? throw new InvalidOperationException("pendingConfirmations/list 二次返回空结果");
        if (!after.Available || after.Items.Count != 0) {
            throw new InvalidOperationException($"结算后收件箱未清空：available={after.Available}, count={after.Items.Count}");
        }

        var uiScript = await http.GetStringAsync($"{baseUrl}api/pendingConfirmations/ui.js", cancellationToken);
        if (!uiScript.Contains("jcc-pending", StringComparison.Ordinal)
            || !uiScript.Contains("复核通过", StringComparison.Ordinal)) {
            throw new InvalidOperationException("待确认收件箱前端脚本缺少面板/按钮标记");
        }
        Console.WriteLine($"[dsh-smoke] pendingConfirmations list/resolve ok ticket={ticket.Id}, ui.js len={uiScript.Length}");
    }

    /// <summary>session/create：在隔离目录建会话，返回会话 ID。</summary>
    private static async Task<string> CreateSessionAsync(HttpClient http, string cwd, CancellationToken cancellationToken) {
        var args = JsonSerializer.SerializeToElement(
            new DshSessionCreateArgs { Cwd = cwd },
            DshJsonContext.Default.DshSessionCreateArgs);
        var value = await CallAsync(http, "session/create", args, cancellationToken);
        var created = value.Deserialize(DshJsonContext.Default.DshSessionCreateResult)
            ?? throw new InvalidOperationException("session/create 返回空结果");
        Console.WriteLine($"[dsh-smoke] rpc session/create ok session={created.SessionId}");
        return created.SessionId;
    }

    /// <summary>session/list：新会话必须出现在列表里（会话注册表读写闭环）。</summary>
    private static async Task CheckSessionListedAsync(HttpClient http, string sessionId, CancellationToken cancellationToken) {
        var value = await CallAsync(http, "session/list", null, cancellationToken);
        var list = value.Deserialize(DshJsonContext.Default.DshSessionList)
            ?? throw new InvalidOperationException("session/list 返回空结果");
        if (!list.Items.Exists(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"session/list 未包含刚创建的会话 {sessionId}");
        }
        Console.WriteLine($"[dsh-smoke] rpc session/list ok items={list.Items.Count}");
    }

    /// <summary>打开 $events 流并等 ready 帧（权限确认/提问/目标事件通道）。</summary>
    private static async Task OpenEventStreamAsync(ClientWebSocket socket, CancellationToken cancellationToken) {
        await SendOpenAsync(socket, new DshStreamOpen {
            Type = "open",
            StreamId = EventsStreamId,
            Endpoint = "$events",
        }, cancellationToken);
        var item = await ReceiveItemAsync(socket, cancellationToken);
        if (!string.Equals(item.StreamId, EventsStreamId, StringComparison.Ordinal)) {
            throw new InvalidOperationException($"$events 首帧流 ID 异常：{item.StreamId}");
        }
        var ready = item.Value.Deserialize(DshJsonContext.Default.DshEventsReady)
            ?? throw new InvalidOperationException("$events 首帧不是 ready");
        GuardNotEmpty(ready.ClientId, "$events ready 缺 clientId");
        Console.WriteLine($"[dsh-smoke] stream $events ready client={ready.ClientId}");
    }

    /// <summary>打开 session/follow 流并等 snapshot 帧（会话事件流基线）。</summary>
    private static async Task OpenSessionFollowAsync(ClientWebSocket socket, string sessionId, CancellationToken cancellationToken) {
        var args = JsonSerializer.SerializeToElement(
            new DshSessionFollowArgs {
                Address = new DshSessionAddress { Kind = "session", SessionId = sessionId },
                AssistantStream = true,
            },
            DshJsonContext.Default.DshSessionFollowArgs);
        await SendOpenAsync(socket, new DshStreamOpen {
            Type = "open",
            StreamId = FollowStreamId,
            Endpoint = "session/follow",
            Payload = new DshRpcRequestPayload { Args = args },
        }, cancellationToken);
        var item = await ReceiveItemAsync(socket, cancellationToken);
        if (!string.Equals(item.StreamId, FollowStreamId, StringComparison.Ordinal)) {
            throw new InvalidOperationException($"session/follow 首帧流 ID 异常：{item.StreamId} value={item.Value}");
        }
        var snapshot = item.Value.Deserialize(DshJsonContext.Default.DshSessionFollowSnapshot)
            ?? throw new InvalidOperationException("session/follow 首帧不是 snapshot");
        Console.WriteLine($"[dsh-smoke] stream session/follow snapshot cursor={snapshot.Cursor} records={snapshot.Records.Count}");
    }

    /// <summary>goals/create：返回目标 CAS 引用。</summary>
    private static async Task<DshGoalRef> CreateGoalAsync(HttpClient http, string sessionId, CancellationToken cancellationToken) {
        var args = JsonSerializer.SerializeToElement(
            new DshGoalsCreateArgs {
                AgentId = sessionId,
                Request = new DshGoalsCreateRequest { Objective = Objective },
            },
            DshJsonContext.Default.DshGoalsCreateArgs);
        var value = await CallAsync(http, "goals/create", args, cancellationToken);
        var created = value.Deserialize(DshJsonContext.Default.DshGoalsCreateResult)
            ?? throw new InvalidOperationException("goals/create 返回空结果");
        GuardNotEmpty(created.Ref.Id, "goals/create 未返回目标 id");
        Console.WriteLine($"[dsh-smoke] rpc goals/create ok revision={created.Ref.Revision}");
        return created.Ref;
    }

    /// <summary>goals/pause：armed→disarmed，触发 activation 翻转事件。</summary>
    private static async Task PauseGoalAsync(HttpClient http, string sessionId, DshGoalRef reference, CancellationToken cancellationToken) {
        var args = JsonSerializer.SerializeToElement(
            new DshGoalsRefArgs { AgentId = sessionId, Ref = reference },
            DshJsonContext.Default.DshGoalsRefArgs);
        await CallAsync(http, "goals/pause", args, cancellationToken);
        Console.WriteLine($"[dsh-smoke] rpc goals/pause ok revision={reference.Revision}");
    }

    /// <summary>等待 $events 上的 goal/activation-changed 帧并校验载荷。</summary>
    private static async Task ExpectActivationFrameAsync(ClientWebSocket socket, string sessionId, CancellationToken cancellationToken) {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(15));
        while (true) {
            var item = await ReceiveItemAsync(socket, wait.Token);
            if (!string.Equals(item.StreamId, EventsStreamId, StringComparison.Ordinal)
                || !item.Value.TryGetProperty("event", out var eventName)
                || !string.Equals(eventName.GetString(), "goal/activation-changed", StringComparison.Ordinal)) {
                continue;
            }
            var agentId = item.Value.TryGetProperty("agentId", out var agent) ? agent.GetString() : null;
            if (!string.Equals(agentId, sessionId, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"goal 事件的 agentId 不匹配：{agentId} != {sessionId}");
            }
            var request = item.Value.GetProperty("request");
            var goal = request.GetProperty("goal");
            var activation = goal.GetProperty("activation").GetString();
            var revision = goal.GetProperty("revision").GetInt32();
            var changedSession = request.GetProperty("sessionId").GetString();
            if (!string.Equals(changedSession, sessionId, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"goal 事件 sessionId 不匹配：{changedSession} != {sessionId}");
            }
            if (!string.Equals(activation, "disarmed", StringComparison.Ordinal)) {
                throw new InvalidOperationException($"pause 后 activation 应为 disarmed，实际 {activation}");
            }
            Console.WriteLine($"[dsh-smoke] $events goal/activation-changed activation={activation} revision={revision}");
            return;
        }
    }

    /// <summary>并发读压测：32 路 goals/get + 16 路 settings/describe，校验结果一致（Actor 串行语义）。</summary>
    private static async Task CheckConcurrentReadsAsync(HttpClient http, string sessionId, CancellationToken cancellationToken) {
        var goalArgs = JsonSerializer.SerializeToElement(
            new DshGoalsAgentArgs { AgentId = sessionId },
            DshJsonContext.Default.DshGoalsAgentArgs);
        var goalReads = Enumerable.Range(0, 32)
            .Select(_ => CallAsync(http, "goals/get", goalArgs, cancellationToken));
        var goalValues = await Task.WhenAll(goalReads);
        var objectives = goalValues
            .Select(value => value.Deserialize(DshJsonContext.Default.DshGoal)?.Objective ?? "")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (objectives.Count != 1 || !string.Equals(objectives[0], Objective, StringComparison.Ordinal)) {
            throw new InvalidOperationException($"并发 goals/get 结果不一致：{string.Join(" | ", objectives)}");
        }
        var settingsReads = Enumerable.Range(0, 16)
            .Select(_ => CallAsync(http, "settings/describe", null, cancellationToken));
        await Task.WhenAll(settingsReads);
        Console.WriteLine("[dsh-smoke] concurrent goals/get x32 + settings/describe x16 ok");
    }

    /// <summary>发送 WS 流打开帧。</summary>
    private static Task SendOpenAsync(ClientWebSocket socket, DshStreamOpen open, CancellationToken cancellationToken) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(open, DshJsonContext.Default.DshStreamOpen),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    /// <summary>接收一条完整 WS 文本帧并解析为流项。</summary>
    private static async Task<DshStreamItem> ReceiveItemAsync(ClientWebSocket socket, CancellationToken cancellationToken) {
        var buffer = new byte[64 * 1024];
        var builder = new StringBuilder();
        while (true) {
            var received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close) {
                throw new InvalidOperationException("WS 在冒烟完成前被对端关闭");
            }
            builder.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
            if (received.EndOfMessage) {
                break;
            }
        }
        return JsonSerializer.Deserialize(builder.ToString(), DshJsonContext.Default.DshStreamItem)
            ?? throw new InvalidOperationException($"WS 帧解析失败：{builder}");
    }

    /// <summary>POST 一元 RPC 信封；失败时抛出带引导信息的异常，成功返回 value（可能为 undefined）。</summary>
    private static async Task<JsonElement> CallAsync(HttpClient http, string method, JsonElement? args, CancellationToken cancellationToken) {
        var envelope = new DshRpcRequest {
            Type = "client-request",
            RpcId = Guid.NewGuid().ToString("N"),
            Method = method,
            Payload = args is { } value ? new DshRpcRequestPayload { Args = value } : null,
        };
        using var content = new StringContent(
            JsonSerializer.Serialize(envelope, DshJsonContext.Default.DshRpcRequest));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await http.PostAsync($"api/{method}", content, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"{method} HTTP {(int)response.StatusCode} {response.ReasonPhrase}；响应体: {body[..Math.Min(body.Length, 200)]}");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var reply = await JsonSerializer.DeserializeAsync(stream, DshJsonContext.Default.DshRpcResponse, cancellationToken)
            ?? throw new InvalidOperationException($"{method} 响应为空");
        if (!reply.Result.Ok) {
            var error = reply.Result.Error;
            throw new InvalidOperationException($"{method} 失败：{error?.Code}: {error?.Message}");
        }
        return reply.Result.Value ?? default;
    }

    /// <summary>非空字符串守卫（协议关键字段缺失即失败）。</summary>
    private static void GuardNotEmpty(string value, string message) {
        if (string.IsNullOrEmpty(value)) {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>冒烟队列 — 单线程串行调用，只保留一张票据，结算后移除，验证列表/清空真实路由。</summary>
    private sealed class SmokePendingQueue : IPendingConfirmationQueue {
        private readonly List<(PendingConfirmationItem Item, TaskCompletionSource<PendingConfirmationDecision> Completion)> _items = [];

        /// <inheritdoc />
        public bool HasPending => _items.Count > 0;

        /// <inheritdoc />
        public PendingConfirmationItem Enqueue(
            string? agentId, string toolName, string reason,
            IReadOnlyDictionary<string, JsonElement> arguments) {
            var completion = new TaskCompletionSource<PendingConfirmationDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new PendingConfirmationItem {
                Id = $"smoke-{Guid.NewGuid():N}",
                AgentId = agentId,
                ToolName = toolName,
                Reason = reason,
                Arguments = arguments,
                CreatedAtUtc = DateTime.UtcNow,
                Decision = completion.Task,
            };
            _items.Add((item, completion));
            return item;
        }

        /// <inheritdoc />
        public IReadOnlyList<PendingConfirmationItem> Snapshot() => [.. _items.Select(pair => pair.Item)];

        /// <inheritdoc />
        public bool TryResolve(string id, PendingConfirmationDecision decision) {
            var index = _items.FindIndex(pair => pair.Item.Id == id);
            if (index < 0) {
                return false;
            }
            var completion = _items[index].Completion;
            _items.RemoveAt(index);
            completion.TrySetResult(decision);
            return true;
        }
    }

    /// <summary>冒烟结算器 — 直接委托队列，验证前端决策到票据的完整链路。</summary>
    private sealed class SmokePendingResolver(SmokePendingQueue queue) : IPendingConfirmationResolver {
        /// <inheritdoc />
        public IReadOnlyList<PendingConfirmationItem> ListPending() => queue.Snapshot();

        /// <inheritdoc />
        public bool Resolve(string id, PendingConfirmationDecision decision) => queue.TryResolve(id, decision);

        /// <inheritdoc />
        public int ResolveAll(PendingConfirmationDecision decision) {
            var count = 0;
            foreach (var item in queue.Snapshot()) {
                if (queue.TryResolve(item.Id, decision)) {
                    count++;
                }
            }
            return count;
        }
    }
}
