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

    /// <summary>可选审计落盘路径（JSONL）；未设置时不写审计。审计失败不影响授权判定。</summary>
    public const string AuditLogVariable = "JCC_CLI_AUDIT_LOG";

    /// <summary>显式允许 agent shell 嵌套启动新的 jcc 会话（默认拒绝，防止绕过父 broker 工具域）。</summary>
    public const string AllowNestedLaunchVariable = "JCC_ALLOW_NESTED_LAUNCH";
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

    /// <summary>
    /// 兑换此前签发的单次 grant（执行前必须兑换）；与 <see cref="GrantId"/> 配合使用。
    /// </summary>
    public bool Redeem { get; init; }

    /// <summary>待兑换的 grant 标识（仅 <see cref="Redeem"/> 为 true 时携带）。</summary>
    public string? GrantId { get; init; }
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

    /// <summary>放行时签发的一次性 grant（执行前必须兑换；重放 fail-closed）。</summary>
    public string? GrantId { get; init; }
}

/// <summary>
/// CLI 授权审计条目 — 每次工具执行判定写一行 JSONL（ADR 0168 阶段 5）。
/// <para>参数只存摘要，避免把文件内容 / token 等敏感值落盘。审计由
/// <see cref="CliAuthorizationEnvironment.AuditLogVariable"/> 显式开启。</para>
/// </summary>
internal sealed record CliAuthorizationAuditEntry {
    /// <summary>UTC 时间戳（ISO 8601）。</summary>
    public required string Timestamp { get; init; }

    /// <summary>来源 agent；token 无效时为空。</summary>
    public string? AgentId { get; init; }

    /// <summary>来源会话。</summary>
    public string? SessionId { get; init; }

    /// <summary>权限模式快照。</summary>
    public string? PermissionMode { get; init; }

    /// <summary>工具组 ID。</summary>
    public required string GroupId { get; init; }

    /// <summary>工具 ID。</summary>
    public required string ToolId { get; init; }

    /// <summary>执行镜像文件名。</summary>
    public required string ExecutableName { get; init; }

    /// <summary>命中的策略 ID；scope-only / 加载失败时为空。</summary>
    public string? PolicyId { get; init; }

    /// <summary>命中的策略版本。</summary>
    public int? PolicyVersion { get; init; }

    /// <summary>规范化参数摘要（SHA-256 十六进制）；无参数时为空。</summary>
    public string? ArgumentDigest { get; init; }

    /// <summary>参数名清单（不含值，避免敏感内容落盘）；无参数时为空。</summary>
    public string[]? ParameterNames { get; init; }

    /// <summary>是否放行。</summary>
    public required bool Allowed { get; init; }

    /// <summary>判定层。</summary>
    public required string Layer { get; init; }

    /// <summary>判定原因。</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// jcc 宿主内的 CLI 工具授权 broker — 命名管道只接受当前用户，令牌映射到 agent 作用域。
/// <para>工具 exe 只发请求；角色工具域、策略 effect、参数白名单全在这里判定。</para>
/// </summary>
internal sealed class CliAuthorizationBroker : IAsyncDisposable {
    /// <summary>一次性 grant 的存活窗口 — 授权与执行之间只隔一次往返，30 秒足够且限制重放面。</summary>
    internal static readonly TimeSpan GrantLifetime = TimeSpan.FromSeconds(30);

    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly ConcurrentDictionary<string, CliAuthorizationScope> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GrantRecord> _grants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LoadedCliToolPolicy> _policyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _enforcePolicyTrust;
    private readonly TimeProvider _timeProvider;

    public CliAuthorizationBroker(bool enforcePolicyTrust = false, TimeProvider? timeProvider = null) {
        _enforcePolicyTrust = enforcePolicyTrust;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pipeName = "jcc-cli-auth-" + Guid.NewGuid().ToString("N");
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public CliAuthorizationSession CreateSession(CliAuthorizationScope scope) {
        ArgumentNullException.ThrowIfNull(scope);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = scope;
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
                if (request is null) {
                    response = ToResponse(CliAuthorizationDecision.Deny("Protocol", "授权请求 JSON 为空；检查调用方序列化 DTO。"));
                } else if (request.ScopeSnapshot) {
                    response = Snapshot(request);
                } else if (request.Redeem) {
                    var redemption = EvaluateRedemption(request);
                    response = ToResponse(redemption.Decision);
                    if (!response.Allowed) WriteAudit(request, redemption, response);
                } else {
                    var evaluation = Evaluate(request);
                    response = ToResponse(evaluation.Decision);
                    if (response.Allowed) {
                        response = response with { GrantId = IssueGrant(request, evaluation) };
                    }
                    WriteAudit(request, evaluation, response);
                }
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

    private sealed record Evaluation(
        CliAuthorizationDecision Decision,
        CliToolPolicyManifest? Policy = null,
        string? PolicySha256Hex = null);

    /// <summary>一次性 grant 的绑定内容 — 兑换时必须逐项相等，任一项不符即拒绝。</summary>
    private sealed record GrantRecord {
        public required string TokenDigest { get; init; }
        public required string GroupId { get; init; }
        public required string ToolId { get; init; }
        public required string ExecutableName { get; init; }
        public required string ArgumentDigest { get; init; }
        public string? PolicySha256Hex { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }

    private Evaluation Evaluate(CliAuthorizationRequest request) {
        var scope = ResolveSession(request.Token);
        if (scope is null) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Auth.Token",
                "CLI 授权 token 无效或已过期；通过 jcc tool call 重新发起，不要手工拼接环境变量。"));
        }

        if (request.ScopeOnly) {
            return new Evaluation(CliToolPolicyEvaluator.EvaluateScopeOnly(scope, request.ToolId));
        }

        LoadedCliToolPolicy loaded;
        try {
            var cacheKey = request.PolicyImagePath + "|" + request.ExecutableName;
            loaded = _policyCache.TryGetValue(cacheKey, out var cached)
                ? cached
                : LoadAndCachePolicy(cacheKey, request.PolicyImagePath, request.ExecutableName);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                         or InvalidOperationException or BadImageFormatException) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Policy.Load",
                $"无法读取目标 exe 的内嵌权限策略：{ex.Message}；重新编译工具组并检查 policy.yaml 是否嵌入。"));
        }

        if (_enforcePolicyTrust) {
            string? trustError;
            try {
                trustError = CliToolPolicyTrustCatalog.Default.Verify(
                    loaded.Manifest.PolicyId, loaded.Sha256Hex, request.ExecutableName);
            } catch (Exception ex) when (ex is IOException or InvalidOperationException or YamlException) {
                trustError = $"信任目录不可用：{ex.Message}";
            }
            if (trustError is not null) {
                return new Evaluation(CliAuthorizationDecision.Deny(
                    "Policy.Trust",
                    $"{trustError}；重新编译工具组并运行 Generate-CliToolPolicies.py 刷新 policy_trust.yaml。"));
            }
        }

        var decision = CliToolPolicyEvaluator.Evaluate(
            loaded.Manifest,
            scope,
            request.GroupId,
            request.ExecutableName,
            request.ToolId,
            request.Arguments);
        return new Evaluation(decision, loaded.Manifest, loaded.Sha256Hex);
    }

    /// <summary>
    /// 签发一次性 grant — 绑定参数摘要、exe 身份、令牌摘要与策略哈希；兑换即失效。
    /// </summary>
    private string IssueGrant(CliAuthorizationRequest request, Evaluation evaluation) {
        PruneExpiredGrants();
        var grantId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _grants[grantId] = new GrantRecord {
            TokenDigest = DigestToken(request.Token),
            GroupId = request.GroupId,
            ToolId = request.ToolId,
            ExecutableName = request.ExecutableName,
            ArgumentDigest = DigestArguments(request.Arguments) ?? "",
            PolicySha256Hex = evaluation.PolicySha256Hex,
            ExpiresAt = _timeProvider.GetUtcNow() + GrantLifetime
        };
        return grantId;
    }

    /// <summary>
    /// 兑换 grant — 先摘除再校验（重放/绑定不符都会消耗 grant），任一步不符立即 fail-closed。
    /// </summary>
    private Evaluation EvaluateRedemption(CliAuthorizationRequest request) {
        var scope = ResolveSession(request.Token);
        if (scope is null) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Auth.Token",
                "CLI 授权 token 无效或已过期；通过 jcc tool call 重新发起，不要手工拼接环境变量。"));
        }
        if (string.IsNullOrWhiteSpace(request.GrantId)
            || !_grants.TryRemove(request.GrantId, out var grant)) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Grant.Unknown",
                "grant 不存在或已被使用；每次执行都必须重新申请授权，禁止重放旧凭据。"));
        }
        if (_timeProvider.GetUtcNow() > grant.ExpiresAt) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Grant.Expired",
                $"grant 已过期（有效期 {GrantLifetime.TotalSeconds:0} 秒）；重新发起授权。"));
        }
        if (!FixedTimeEquals(grant.TokenDigest, DigestToken(request.Token))) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Grant.Mismatch",
                "grant 不属于当前会话；跨会话复用凭据已拒绝。"));
        }
        if (!string.Equals(grant.GroupId, request.GroupId, StringComparison.Ordinal)
            || !string.Equals(grant.ToolId, request.ToolId, StringComparison.Ordinal)
            || !string.Equals(grant.ExecutableName, request.ExecutableName, StringComparison.OrdinalIgnoreCase)) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Grant.Mismatch",
                "grant 绑定的工具或 exe 与本次调用不一致；重新发起授权。"));
        }
        if (!string.Equals(grant.ArgumentDigest, DigestArguments(request.Arguments) ?? "", StringComparison.Ordinal)) {
            return new Evaluation(CliAuthorizationDecision.Deny(
                "Grant.Mismatch",
                "grant 绑定的参数与本次调用不一致；授权后不得改参，重新发起授权。"));
        }
        if (grant.PolicySha256Hex is not null) {
            string actualHash;
            try {
                var cacheKey = request.PolicyImagePath + "|" + request.ExecutableName;
                var loaded = _policyCache.TryGetValue(cacheKey, out var cached)
                    ? cached
                    : LoadAndCachePolicy(cacheKey, request.PolicyImagePath, request.ExecutableName);
                actualHash = loaded.Sha256Hex;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                             or InvalidOperationException or BadImageFormatException) {
                return new Evaluation(CliAuthorizationDecision.Deny(
                    "Grant.Policy",
                    $"兑换时无法复核策略哈希：{ex.Message}；重新编译工具组后再执行。"));
            }
            if (!FixedTimeEquals(grant.PolicySha256Hex, actualHash)) {
                return new Evaluation(CliAuthorizationDecision.Deny(
                    "Grant.Policy",
                    "策略内容在授权与执行之间发生变化；grant 已作废，重新发起授权。"));
            }
        }
        return new Evaluation(CliAuthorizationDecision.Allow("Grant.Redeemed"));
    }

    private void PruneExpiredGrants() {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _grants) {
            if (now > pair.Value.ExpiresAt) _grants.TryRemove(pair.Key, out _);
        }
    }

    private static string DigestToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeEquals(string expectedHex, string actualHex) {
        var expected = Encoding.UTF8.GetBytes(expectedHex);
        var actual = Encoding.UTF8.GetBytes(actualHex);
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>写一行审计；未配置路径或写盘失败都不改变授权判定（审计是旁路，不是门禁）。</summary>
    private void WriteAudit(CliAuthorizationRequest request, Evaluation evaluation, CliAuthorizationResponse response) {
        var path = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.AuditLogVariable);
        if (string.IsNullOrWhiteSpace(path)) return;

        var scope = ResolveSession(request.Token);
        var entry = new CliAuthorizationAuditEntry {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            AgentId = scope?.AgentId,
            SessionId = scope?.SessionId,
            PermissionMode = scope?.PermissionMode,
            GroupId = request.GroupId,
            ToolId = request.ToolId,
            ExecutableName = request.ExecutableName,
            PolicyId = evaluation.Policy?.PolicyId,
            PolicyVersion = evaluation.Policy?.PolicyVersion,
            ArgumentDigest = DigestArguments(request.Arguments),
            ParameterNames = request.Arguments.Count == 0
                ? null
                : request.Arguments.Keys.Order(StringComparer.Ordinal).ToArray(),
            Allowed = response.Allowed,
            Layer = response.Layer,
            Reason = response.Reason
        };
        try {
            var line = JsonSerializer.Serialize(entry, CliNodeJsonContext.Default.CliAuthorizationAuditEntry);
            File.AppendAllText(path, line + Environment.NewLine);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Debug.WriteLine($"[CLI-AUDIT] 审计写入失败（不影响判定）: {ex.Message}");
        }
    }

    private static string? DigestArguments(Dictionary<string, string> arguments) {
        if (arguments.Count == 0) return null;
        var canonical = string.Join('\n', arguments
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private LoadedCliToolPolicy LoadAndCachePolicy(string cacheKey, string path, string executableName) {
        var loaded = CliToolPolicyResourceReader.LoadWithHashFromExecutable(path, executableName);
        _policyCache[cacheKey] = loaded;
        return loaded;
    }

    private CliAuthorizationScope? ResolveSession(string candidateToken) {
        if (string.IsNullOrEmpty(candidateToken)) return null;
        var candidateBytes = Encoding.UTF8.GetBytes(candidateToken);
        foreach (var pair in _sessions) {
            var expectedBytes = Encoding.UTF8.GetBytes(pair.Key);
            if (candidateBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes)) {
                return pair.Value;
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
    /// <summary>
    /// 申请授权并默认立即兑换一次性 grant — 兑换失败按拒绝处理（fail-closed）。
    /// <paramref name="redeemGrant"/> 仅供测试/诊断跳过兑换，执行路径不得关闭。
    /// </summary>
    public static async Task<CliAuthorizationDecision> AuthorizeAsync(
        CliAuthorizationSession session,
        string groupId,
        string toolId,
        string policyImagePath,
        string executableName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken,
        bool scopeOnly = false,
        bool redeemGrant = true) {
        var decision = await RequestDecisionAsync(
            session, groupId, toolId, policyImagePath, executableName, arguments,
            cancellationToken, scopeOnly).ConfigureAwait(false);
        if (!decision.Allowed || !redeemGrant || decision.GrantId is null) return decision;
        var redeemed = await RedeemGrantAsync(
            session, decision.GrantId, groupId, toolId, policyImagePath, executableName,
            arguments, cancellationToken, scopeOnly).ConfigureAwait(false);
        // 兑换成功不改变放行层（Host.Allow/Policy.Allow 仍是对外语义），只清掉 grant。
        return redeemed.Allowed ? decision with { GrantId = null } : redeemed;
    }

    /// <summary>兑换一次 grant；单次有效，重放或绑定不符一律拒绝。</summary>
    public static async Task<CliAuthorizationDecision> RedeemGrantAsync(
        CliAuthorizationSession session,
        string grantId,
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
            ScopeOnly = scopeOnly,
            Redeem = true,
            GrantId = grantId
        };
        var reply = await SendAsync(session, request, cancellationToken).ConfigureAwait(false);
        return ToDecision(reply);
    }

    private static async Task<CliAuthorizationDecision> RequestDecisionAsync(
        CliAuthorizationSession session,
        string groupId,
        string toolId,
        string policyImagePath,
        string executableName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken,
        bool scopeOnly) {
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
        return ToDecision(reply);
    }

    private static CliAuthorizationDecision ToDecision(BrokerReply reply) =>
        reply.Response is null
            ? CliAuthorizationDecision.Deny("Auth.Transport", reply.Error ?? "CLI 授权 broker 未返回响应。")
            : new CliAuthorizationDecision {
                Allowed = reply.Response.Allowed,
                Reason = reply.Response.Reason,
                Layer = reply.Response.Layer,
                GrantId = reply.Response.GrantId
            };

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
    internal static string? ResolveEntryImagePath() {
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
public static class CliAuthorizationBrokerRuntime {
    private static readonly Lazy<CliAuthorizationBroker> Broker = new(() => new CliAuthorizationBroker(enforcePolicyTrust: true));
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

/// <summary>内嵌策略及其原始 YAML 的 SHA-256（信任目录比对用）。</summary>
internal sealed record LoadedCliToolPolicy(CliToolPolicyManifest Manifest, string Sha256Hex);

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

    public static CliToolPolicyManifest LoadFromExecutable(string path, string? expectedExecutableName = null) =>
        LoadWithHashFromExecutable(path, expectedExecutableName).Manifest;

    /// <param name="path">目标镜像路径。</param>
    /// <param name="expectedExecutableName">期望的策略 executables 成员；为空则不校验。</param>
    /// <param name="expectedPolicyId">期望的 policy_id；jcc 同时内嵌 slash/base 策略时必须指定。</param>
    public static LoadedCliToolPolicy LoadWithHashFromExecutable(
        string path,
        string? expectedExecutableName = null,
        string? expectedPolicyId = null) {
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

        LoadedCliToolPolicy? firstPolicy = null;
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
            var bytes = reader.ReadBytes(size);
            var policy = CliToolPolicyLoader.Parse(Encoding.UTF8.GetString(bytes), $"{path}!{name}");
            if (expectedPolicyId is not null
                && !string.Equals(policy.PolicyId, expectedPolicyId, StringComparison.Ordinal)) {
                continue;
            }
            var loaded = new LoadedCliToolPolicy(policy, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            firstPolicy ??= loaded;
            if (string.IsNullOrWhiteSpace(expectedExecutableName)
                || policy.Executables.Any(candidate =>
                    string.Equals(candidate, expectedExecutableName, StringComparison.OrdinalIgnoreCase))) {
                return loaded;
            }
        }

        if (firstPolicy is not null) {
            throw new InvalidOperationException(
                $"可执行文件 '{path}' 的内嵌策略均不适用于 '{expectedExecutableName}'；检查 policy.yaml 的 executables 字段");
        }
        if (expectedPolicyId is not null) {
            throw new InvalidOperationException(
                $"可执行文件 '{path}' 未嵌入 policy_id='{expectedPolicyId}' 的策略资源");
        }
        throw new InvalidOperationException($"可执行文件 '{path}' 未嵌入 '*{PolicyResourceMarker}*.yaml' 权限策略");
    }
}
