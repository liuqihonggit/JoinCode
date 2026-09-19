namespace Core.Bridge;

/// <summary>
/// Bridge 环境注册器 — 环境注册、初始会话创建、注册错误处理。
/// 从 BridgeMain 提取为单一职责类型。
/// </summary>
internal sealed class BridgeEnvironmentRegistrar {
    private readonly BridgeMain _owner;

    internal BridgeEnvironmentRegistrar(BridgeMain owner)
        => _owner = owner;

    /// <summary>
    /// 注册 Bridge 环境 — RunAsync/RunHeadlessAsync 共享
    /// 成功时设置 EnvironmentId/EnvironmentSecret 并返回响应
    /// </summary>
    internal async Task<BridgeEnvironmentRegistrationResponse> RegisterEnvironmentAsync(
        BridgeConfig config, CancellationToken ct) {
        var registration = new BridgeEnvironmentRegistration {
            BridgeId = config.BridgeId,
            MachineName = config.MachineName,
            Dir = config.Dir,
            Branch = config.Branch,
            GitRepoUrl = config.GitRepoUrl,
            MaxSessions = config.MaxSessions,
            SpawnMode = config.SpawnMode.ToValue(),
            WorkerType = config.WorkerType,
            ReuseEnvironmentId = config.ReuseEnvironmentId,
        };

        var response = await _owner._deps.ApiClient.RegisterBridgeEnvironmentAsync(
            registration, ct).ConfigureAwait(false);

        if (response is null)
            throw new InvalidOperationException("Registration returned null response.");

        _owner.EnvironmentId = response.EnvironmentId;
        _owner.EnvironmentSecret = response.BridgeId;

        return response;
    }

    /// <summary>
    /// 尝试创建初始会话 — RunAsync/RunHeadlessAsync 共享
    /// </summary>
    internal async Task<string?> TryCreateInitialSessionAsync(
        string? name, string? permissionMode, BridgeConfig config, CancellationToken ct) {
        if (_owner._deps.CreateBridgeSession is null) return null;

        try {
            var createRequest = new BridgeCreateSessionRequest {
                EnvironmentId = _owner.GetEnvironmentId(),
                Title = name,
                GitRepoUrl = config.GitRepoUrl,
                Branch = config.Branch,
                PermissionMode = permissionMode,
            };
            var createdSessionId = await _owner._deps.CreateBridgeSession(
                createRequest, ct).ConfigureAwait(false);
            if (createdSessionId is not null) {
                _owner._logger?.LogInformation("BridgeMain: created initial session {SessionId}", createdSessionId);
            }
            return createdSessionId;
        } catch (Exception ex) {
            _owner._logger?.LogDebug(ex, "BridgeMain: session creation failed (non-fatal)");
            return null;
        }
    }

    internal BridgeMainResult HandleRegistrationError(Exception ex) {
        if (ex is BridgeFatalError fatal) {
            _owner.TelemetryCount("tengu_bridge_registration_failed", new Dictionary<string, string> {
                ["status"] = fatal.StatusCode?.ToString() ?? "0",
            });
            if (BridgeApiClient.IsExpiredErrorType(fatal.ErrorType)) {
                _owner._logger?.LogWarning("BridgeMain: registration expired: {Message}", fatal.Message);
            } else if (BridgeApiClient.IsSuppressible403(fatal)) {
                _owner._logger?.LogDebug("BridgeMain: suppressed 403 during registration: {Message}", fatal.Message);
            } else {
                _owner._logger?.LogError(fatal, "BridgeMain: environment registration failed (fatal)");
            }
            return new BridgeMainResult { Error = $"Registration failed: {fatal.Message}" };
        }

        if (ex is InvalidOperationException { Message: var msg } && msg.StartsWith("Registration returned null")) {
            return new BridgeMainResult { Error = msg };
        }

        _owner._logger?.LogError(ex, "BridgeMain: environment registration failed");
        return new BridgeMainResult { Error = $"Registration failed: {ex.Message}" };
    }
}