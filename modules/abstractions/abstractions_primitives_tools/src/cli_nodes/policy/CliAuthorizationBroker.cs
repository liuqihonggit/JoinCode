namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 分组 exe 与 jcc 共享的 CLI 授权环境变量名。
/// <para>变量只携带 broker 管道名与随机会话令牌；权限真相始终在 jcc 宿主的 broker 内。</para>
/// </summary>
public static class CliAuthorizationEnvironment {
    /// <summary>本机授权 broker 管道名。</summary>
    public const string PipeVariable = "JCC_CLI_AUTH_PIPE";

    /// <summary>本次 shell/工具调用的随机会话令牌。</summary>
    public const string TokenVariable = "JCC_CLI_AUTH_TOKEN";
}

/// <summary>broker 签发给一次 shell/工具链的管道位置与令牌。</summary>
internal sealed record CliAuthorizationSession(string PipeName, string Token);

/// <summary>broker 授权请求 — 由工具 exe 或 jcc 启动器发送。</summary>
internal sealed record CliAuthorizationRequest {
    /// <summary>随机会话令牌。</summary>
    public required string Token { get; init; }

    /// <summary>工具组 ID。</summary>
    public required string GroupId { get; init; }

    /// <summary>请求的工具 ID 或别名。</summary>
    public required string ToolId { get; init; }

    /// <summary>承载内嵌策略的镜像路径。</summary>
    public required string PolicyImagePath { get; init; }

    /// <summary>用于策略 executable 校验的可执行文件名。</summary>
    public required string ExecutableName { get; init; }

    /// <summary>已规范化的工具参数。</summary>
    public required Dictionary<string, string> Arguments { get; init; }

    /// <summary>
    /// 只按 agent 作用域判定（host 工具路径）— 宿主工具没有组内嵌策略，
    /// 但仍必须受 AllowedTools/DeniedTools 约束，否则子代理可用 mcp_call 绕过工具域。
    /// </summary>
    public bool ScopeOnly { get; init; }

    /// <summary>读取当前 token 的工具域快照（供 tree/find/help 发现裁剪）；不执行任何工具。</summary>
    public bool ScopeSnapshot { get; init; }
}

/// <summary>broker 授权响应。</summary>
internal sealed record CliAuthorizationResponse {
    /// <summary>是否允许。</summary>
    public required bool Allowed { get; init; }

    /// <summary>拒绝原因或 allowed。</summary>
    public required string Reason { get; init; }

    /// <summary>判定层。</summary>
    public required string Layer { get; init; }

    /// <summary>作用域允许工具（仅 <see cref="CliAuthorizationRequest.ScopeSnapshot"/> 填充）。</summary>
    public string[]? AllowedTools { get; init; }

    /// <summary>作用域拒绝工具（仅 <see cref="CliAuthorizationRequest.ScopeSnapshot"/> 填充）。</summary>
    public string[]? DeniedTools { get; init; }
}

/// <summary>
/// jcc 宿主内的 CLI 工具授权 broker — 命名管道只接受当前用户，令牌映射到 agent 作用域。
/// <para>工具 exe 只发请求；角色工具域、策略 effect、参数白名单全在这里判定。</para>
/// </summary>
internal sealed class CliAuthorizationBroker : IAsyncDisposable {
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly Dictionary<string, CliAuthorizationScope> _sessions = new(StringComparer.Ordinal);
    private readonly object _sessionGate = new();
    private readonly Dictionary<string, CliToolPolicyManifest> _policyCache = new(StringComparer.OrdinalIgnoreCase);

    public CliAuthorizationBroker() {
        _pipeName = "jcc-cli-auth-" + Guid.NewGuid().ToString("N");
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public CliAuthorizationSession CreateSession(CliAuthorizationScope scope) {
        ArgumentNullException.ThrowIfNull(scope);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_sessionGate) {
            _sessions.Add(token, scope);
        }
        return new CliAuthorizationSession(_pipeName, token);
    }

    public async ValueTask DisposeAsync() {
        _cts.Cancel();
        await _acceptLoop.ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync() {
        while (!_cts.IsCancellationRequested) {
            try {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                await HandleConnectionAsync(server, _cts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                return;
            } catch (IOException) {
                // 管道被调用方提前关闭：下一条连接继续服务，不能让 broker 整体退出。
            } catch (UnauthorizedAccessException) {
                // CurrentUserOnly 拒绝其他用户：不泄露内部细节，继续等待合法连接。
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct) {
        using var reader = new StreamReader(server, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        CliAuthorizationResponse response;
        try {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) {
                response = ToResponse(CliAuthorizationDecision.Deny("Protocol", "授权请求为空；调用方必须发送单行 JSON。"));
            } else {
                var request = JsonSerializer.Deserialize(line, CliNodeJsonContext.Default.CliAuthorizationRequest);
                response = request is null
                    ? ToResponse(CliAuthorizationDecision.Deny("Protocol", "授权请求 JSON 为空；检查调用方序列化 DTO。"))
                    : request.ScopeSnapshot
                        ? Snapshot(request)
                        : ToResponse(Evaluate(request));
            }
        } catch (JsonException ex) {
            response = ToResponse(CliAuthorizationDecision.Deny(
                "Protocol",
                $"授权请求 JSON 无效：{ex.Message}；调用方必须使用 CliAuthorizationRequest DTO。"));
        } catch (Exception ex) when (ex is IOException or InvalidOperationException) {
            response = ToResponse(CliAuthorizationDecision.Deny(
                "Protocol",
                $"授权请求处理失败：{ex.Message}；检查策略镜像与协议版本。"));
        }

        var json = JsonSerializer.Serialize(response, CliNodeJsonContext.Default.CliAuthorizationResponse);
        await writer.WriteLineAsync(json).ConfigureAwait(false);
    }

    private CliAuthorizationResponse Snapshot(CliAuthorizationRequest request) {
        var scope = ResolveSession(request.Token);
        if (scope is null) {
            return ToResponse(CliAuthorizationDecision.Deny(
                "Auth.Token",
                "CLI 授权 token 无效或已过期；通过 jcc tool call 重新发起，不要手工拼接环境变量。"));
        }

        return new CliAuthorizationResponse {
            Allowed = true,
            Reason = "Scope.Snapshot",
            Layer = "Host.Scope",
            AllowedTools = [.. scope.AllowedTools],
            DeniedTools = [.. scope.DeniedTools]
        };
    }

    private CliAuthorizationDecision Evaluate(CliAuthorizationRequest request) {
        var scope = ResolveSession(request.Token);
        if (scope is null) {
            return CliAuthorizationDecision.Deny(
                "Auth.Token",
                "CLI 授权 token 无效或已过期；通过 jcc tool call 重新发起，不要手工拼接环境变量。");
        }

        if (request.ScopeOnly) {
            return CliToolPolicyEvaluator.EvaluateScopeOnly(scope, request.ToolId);
        }

        CliToolPolicyManifest policy;
        try {
            var cacheKey = request.PolicyImagePath + "|" + request.ExecutableName;
            policy = _policyCache.TryGetValue(cacheKey, out var cached)
                ? cached
                : LoadAndCachePolicy(cacheKey, request.PolicyImagePath, request.ExecutableName);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                         or InvalidOperationException or BadImageFormatException) {
            return CliAuthorizationDecision.Deny(
                "Policy.Load",
                $"无法读取目标 exe 的内嵌权限策略：{ex.Message}；重新编译工具组并检查 policy.yaml 是否嵌入。");
        }

        return CliToolPolicyEvaluator.Evaluate(
            policy,
            scope,
            request.GroupId,
            request.ExecutableName,
            request.ToolId,
            request.Arguments);
    }

    private CliToolPolicyManifest LoadAndCachePolicy(string cacheKey, string path, string executableName) {
        var policy = CliToolPolicyResourceReader.LoadFromExecutable(path, executableName);
        _policyCache[cacheKey] = policy;
        return policy;
    }

    private CliAuthorizationScope? ResolveSession(string candidateToken) {
        if (string.IsNullOrEmpty(candidateToken)) return null;
        var candidateBytes = Encoding.UTF8.GetBytes(candidateToken);
        lock (_sessionGate) {
            foreach (var pair in _sessions) {
                var expectedBytes = Encoding.UTF8.GetBytes(pair.Key);
                if (candidateBytes.Length == expectedBytes.Length
                    && CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes)) {
                    return pair.Value;
                }
            }
        }
        return null;
    }

    private static CliAuthorizationResponse ToResponse(CliAuthorizationDecision decision) => new() {
        Allowed = decision.Allowed,
        Reason = decision.Reason,
        Layer = decision.Layer
    };
}

/// <summary>工具 exe / jcc 启动器使用的 broker 客户端。</summary>
internal static class CliToolAuthorizationClient {
    public static async Task<CliAuthorizationDecision> AuthorizeAsync(
        CliAuthorizationSession session,
        string groupId,
        string toolId,
        string policyImagePath,
        string executableName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken,
        bool scopeOnly = false) {
        var request = new CliAuthorizationRequest {
            Token = session.Token,
            GroupId = groupId,
            ToolId = toolId,
            PolicyImagePath = policyImagePath,
            ExecutableName = executableName,
            Arguments = new Dictionary<string, string>(arguments, StringComparer.Ordinal),
            ScopeOnly = scopeOnly
        };
        var reply = await SendAsync(session, request, cancellationToken).ConfigureAwait(false);
        return reply.Response is null
            ? CliAuthorizationDecision.Deny("Auth.Transport", reply.Error ?? "CLI 授权 broker 未返回响应。")
            : new CliAuthorizationDecision {
                Allowed = reply.Response.Allowed,
                Reason = reply.Response.Reason,
                Layer = reply.Response.Layer
            };
    }

    /// <summary>读取当前会话的工具域快照；transport 失败返回 null（发现裁剪是尽力而为，执行判定仍 fail-closed）。</summary>
    public static async Task<CliToolScopeSnapshot?> GetScopeSnapshotAsync(
        CliAuthorizationSession session,
        CancellationToken cancellationToken) {
        var request = new CliAuthorizationRequest {
            Token = session.Token,
            GroupId = "scope",
            ToolId = "",
            PolicyImagePath = "",
            ExecutableName = "",
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal),
            ScopeSnapshot = true
        };
        var reply = await SendAsync(session, request, cancellationToken).ConfigureAwait(false);
        if (reply.Response is not { Allowed: true }) return null;
        return new CliToolScopeSnapshot {
            AllowedTools = reply.Response.AllowedTools ?? [],
            DeniedTools = reply.Response.DeniedTools ?? []
        };
    }

    private sealed record BrokerReply(CliAuthorizationResponse? Response, string? Error);

    private static async Task<BrokerReply> SendAsync(
        CliAuthorizationSession session,
        CliAuthorizationRequest request,
        CancellationToken cancellationToken) {
        try {
            await using var pipe = new NamedPipeClientStream(
                ".",
                session.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            var line = JsonSerializer.Serialize(request, CliNodeJsonContext.Default.CliAuthorizationRequest);
            await writer.WriteLineAsync(line).ConfigureAwait(false);

            var responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine)) {
                return new BrokerReply(null,
                    "CLI 授权 broker 未返回响应；确认 jcc 宿主仍在运行，并改用 jcc tool call 重试。");
            }
            var response = JsonSerializer.Deserialize(responseLine, CliNodeJsonContext.Default.CliAuthorizationResponse);
            return response is null
                ? new BrokerReply(null, "CLI 授权 broker 返回空响应；改用 jcc tool call 重试。")
                : new BrokerReply(response, null);
        } catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) {
            return new BrokerReply(null,
                $"无法连接 CLI 授权 broker：{ex.Message}；确认通过 jcc tool call 调用，或检查宿主进程是否已退出。");
        }
    }

    public static Task<CliAuthorizationDecision> AuthorizeCurrentProcessAsync(
        string groupId,
        string toolId,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken) {
        var policyImagePath = ResolveEntryImagePath();
        if (string.IsNullOrWhiteSpace(policyImagePath)) {
            return Task.FromResult(CliAuthorizationDecision.Deny(
                "Auth.Identity",
                "无法定位当前工具 exe 的镜像路径；请通过 jcc tool call 启动分组 exe。"));
        }
        var executableName = Path.GetFileName(Environment.ProcessPath ?? policyImagePath);
        return AuthorizeCoreAsync(
            groupId,
            toolId,
            arguments,
            policyImagePath,
            executableName,
            Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable),
            Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable),
            cancellationToken);
    }

    /// <summary>
    /// 授权判定核心 — exe 未嵌入策略时保持旧行为（迁移期的唯一放行口），
    /// 一旦嵌入策略就必须有 broker 会话，缺会话按 fail-closed 拒绝。
    /// </summary>
    /// <param name="groupId">工具组 ID。</param>
    /// <param name="toolId">工具 ID。</param>
    /// <param name="arguments">已规范化参数。</param>
    /// <param name="policyImagePath">承载内嵌策略的镜像路径。</param>
    /// <param name="executableName">目标可执行文件名。</param>
    /// <param name="pipeName">broker 管道名，可为 null。</param>
    /// <param name="token">broker 会话令牌，可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>授权判定。</returns>
    internal static Task<CliAuthorizationDecision> AuthorizeCoreAsync(
        string groupId,
        string toolId,
        IReadOnlyDictionary<string, string> arguments,
        string policyImagePath,
        string executableName,
        string? pipeName,
        string? token,
        CancellationToken cancellationToken) {
        bool hasPolicy;
        try {
            hasPolicy = CliToolPolicyResourceReader.HasEmbeddedPolicy(policyImagePath);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException) {
            return Task.FromResult(CliAuthorizationDecision.Deny(
                "Auth.Identity",
                $"无法检查工具 exe 是否嵌入权限策略：{ex.Message}；重新编译工具组后再调用。"));
        }
        if (!hasPolicy) {
            return Task.FromResult(CliAuthorizationDecision.Allow("Policy.NotEnforced"));
        }

        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(token)) {
            return Task.FromResult(CliAuthorizationDecision.Deny(
                "Auth.Missing",
                "未找到 CLI 授权会话；该工具组已嵌入权限策略，直接运行 exe 已被禁止。正确写法: jcc tool call <分组> <工具> --参数 值"));
        }

        return AuthorizeAsync(
            new CliAuthorizationSession(pipeName, token),
            groupId,
            toolId,
            policyImagePath,
            executableName,
            arguments,
            cancellationToken);
    }

    public static Task<CliAuthorizationDecision> AuthorizeHostToolAsync(
        string pipeName,
        string token,
        string toolName,
        CancellationToken cancellationToken) {
        var imagePath = ResolveEntryImagePath() ?? Environment.ProcessPath ?? "jcc";
        var executableName = Path.GetFileName(Environment.ProcessPath ?? imagePath);
        return AuthorizeAsync(
            new CliAuthorizationSession(pipeName, token),
            groupId: "host",
            toolId: toolName,
            policyImagePath: imagePath,
            executableName: executableName,
            arguments: new Dictionary<string, string>(StringComparer.Ordinal),
            cancellationToken,
            scopeOnly: true);
    }

    /// <summary>
    /// 解析承载内嵌策略的镜像：常规构建取同目录程序集 DLL；AOT 单文件回退到宿主 exe。
    /// <para>不使用 Assembly.Location —— 单文件发布下它恒为空且触发 IL3000。</para>
    /// </summary>
    private static string? ResolveEntryImagePath() {
        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName)) {
            var candidate = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
            if (File.Exists(candidate)) return candidate;
        }
        return Environment.ProcessPath;
    }
}

/// <summary>
/// 宿主任意工具执行的集中授权入口 — <c>jcc mcp_call</c> / <c>jcc tool call</c> 的 host 路径共用。
/// <para>没有授权会话（用户直接在终端调用）时放行，由宿主既有权限管线处理；
/// 有会话（agent shell 内）时只允许当前 agent 工具域内的工具，封住 mcp_call 绕过。</para>
/// </summary>
public static class CliHostToolAuthorization {
    /// <summary>宿主任意工具执行的集中授权入口。</summary>
    /// <param name="toolName">协议工具名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>授权判定。</returns>
    public static Task<CliAuthorizationDecision> AuthorizeAsync(
        string toolName,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var pipeName = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable);
        var token = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable);
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(token)) {
            return Task.FromResult(CliAuthorizationDecision.Allow("Host.Manual"));
        }
        return CliToolAuthorizationClient.AuthorizeHostToolAsync(pipeName, token, toolName, cancellationToken);
    }

    /// <summary>
    /// 读取当前 agent 的工具域快照 — tree/find/help 用它裁剪发现结果。
    /// <para>没有会话或 broker 不可达时返回 null（不裁剪）：发现是只读辅助，执行门禁仍独立生效。</para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具域快照；无会话时 null。</returns>
    public static Task<CliToolScopeSnapshot?> GetScopeAsync(CancellationToken cancellationToken = default) {
        var pipeName = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable);
        var token = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable);
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(token)) {
            return Task.FromResult<CliToolScopeSnapshot?>(null);
        }
        return CliToolAuthorizationClient.GetScopeSnapshotAsync(
            new CliAuthorizationSession(pipeName, token),
            cancellationToken);
    }
}

/// <summary>
/// 分组 exe 与 delegate 实现的自证入口 — 读取当前进程环境里的 broker 会话，按所属组策略判定。
/// <para>delegate exe（如 jcc-gh）不走 <c>CliNodeRunner</c>，必须自己调用本入口；
/// 镜像已内嵌策略但缺会话时 fail-closed，避免被 agent shell 直接执行绕过工具域。</para>
/// </summary>
public static class CliGroupToolAuthorization {
    /// <summary>按当前进程会话与内嵌策略判定一次分组工具调用。</summary>
    /// <param name="groupId">工具组 ID。</param>
    /// <param name="toolId">工具 ID。</param>
    /// <param name="arguments">已绑定的规范参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>授权判定。</returns>
    public static Task<CliAuthorizationDecision> AuthorizeAsync(
        string groupId,
        string toolId,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentNullException.ThrowIfNull(arguments);
        return CliToolAuthorizationClient.AuthorizeCurrentProcessAsync(
            groupId,
            toolId,
            arguments,
            cancellationToken);
    }
}

/// <summary>jcc 宿主侧的 broker 单例与 shell 环境注入。</summary>
public static class CliAuthorizationBrokerRuntime {    private static readonly Lazy<CliAuthorizationBroker> Broker = new(() => new CliAuthorizationBroker());
    private static readonly ConcurrentDictionary<string, CliAuthorizationSession> Sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// 为当前 shell 作用域创建或复用授权会话，返回应注入子进程的环境变量。
    /// </summary>
    /// <param name="agentId">agent 标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="allowedTools">允许工具集合；空表示未收紧。</param>
    /// <param name="deniedTools">拒绝工具集合。</param>
    /// <param name="permissionMode">权限模式快照。</param>
    /// <returns>broker 管道名与 token 环境变量。</returns>
    public static IReadOnlyDictionary<string, string> EnsureEnvironment(
        string agentId,
        string sessionId,
        IEnumerable<string> allowedTools,
        IEnumerable<string> deniedTools,
        string? permissionMode) {
        var allowed = allowedTools.Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var denied = deniedTools.Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var key = string.Join('\u001f', [agentId, sessionId, permissionMode ?? "", string.Join(',', allowed), string.Join(',', denied)]);
        var session = Sessions.GetOrAdd(key, _ => Broker.Value.CreateSession(new CliAuthorizationScope {
            AgentId = agentId,
            SessionId = sessionId,
            AllowedTools = allowed,
            DeniedTools = denied,
            PermissionMode = permissionMode
        }));
        return new Dictionary<string, string>(StringComparer.Ordinal) {
            [CliAuthorizationEnvironment.PipeVariable] = session.PipeName,
            [CliAuthorizationEnvironment.TokenVariable] = session.Token
        };
    }
}

/// <summary>从目标 exe 的 PE manifest resource 只读提取内嵌 policy.yaml。</summary>
internal static class CliToolPolicyResourceReader {
    private const string PolicyResourceMarker = ".policy.";

    /// <summary>
    /// 判断镜像是否嵌入了策略资源 — 迁移期按此决定强制鉴权还是保持旧行为。
    /// <para>只匹配资源名不解析 YAML：资源存在但内容损坏时仍交给 broker 报 <c>Policy.Load</c>，保持 fail-closed。</para>
    /// </summary>
    /// <param name="path">目标镜像路径。</param>
    /// <returns>存在策略资源则 true。</returns>
    public static bool HasEmbeddedPolicy(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
        if (!peReader.HasMetadata) return false;

        var metadata = peReader.GetMetadataReader();
        foreach (var handle in metadata.ManifestResources) {
            var name = metadata.GetString(metadata.GetManifestResource(handle).Name);
            if (name.EndsWith(".yaml", StringComparison.Ordinal)
                && name.IndexOf(PolicyResourceMarker, StringComparison.Ordinal) >= 0) {
                return true;
            }
        }
        return false;
    }

    public static CliToolPolicyManifest LoadFromExecutable(string path, string? expectedExecutableName = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
        if (!peReader.HasMetadata) {
            throw new InvalidOperationException($"'{path}' 不包含 .NET 元数据，无法读取内嵌权限策略");
        }

        var metadata = peReader.GetMetadataReader();
        var resourcesRva = peReader.PEHeaders.CorHeader?.ResourcesDirectory.RelativeVirtualAddress ?? 0;
        if (resourcesRva == 0) {
            throw new InvalidOperationException($"可执行文件 '{path}' 没有 CLI 资源目录");
        }

        CliToolPolicyManifest? firstPolicy = null;
        foreach (var handle in metadata.ManifestResources) {
            var resource = metadata.GetManifestResource(handle);
            var name = metadata.GetString(resource.Name);
            if (!name.EndsWith(".yaml", StringComparison.Ordinal)
                || name.IndexOf(PolicyResourceMarker, StringComparison.Ordinal) < 0) continue;

            var block = peReader.GetSectionData(resourcesRva + (int)resource.Offset);
            var reader = block.GetReader();
            if (reader.Length < sizeof(int)) {
                throw new InvalidOperationException($"策略资源 '{name}' 长度头越界");
            }
            var size = reader.ReadInt32();
            if (size < 0 || size > reader.RemainingBytes) {
                throw new InvalidOperationException($"策略资源 '{name}' 长度越界");
            }
            var yaml = Encoding.UTF8.GetString(reader.ReadBytes(size));
            var policy = CliToolPolicyLoader.Parse(yaml, $"{path}!{name}");
            firstPolicy ??= policy;
            if (string.IsNullOrWhiteSpace(expectedExecutableName)
                || policy.Executables.Any(candidate =>
                    string.Equals(candidate, expectedExecutableName, StringComparison.OrdinalIgnoreCase))) {
                return policy;
            }
        }

        if (firstPolicy is not null) {
            throw new InvalidOperationException(
                $"可执行文件 '{path}' 的内嵌策略均不适用于 '{expectedExecutableName}'；检查 policy.yaml 的 executables 字段");
        }
        throw new InvalidOperationException($"可执行文件 '{path}' 未嵌入 '*{PolicyResourceMarker}*.yaml' 权限策略");
    }
}
