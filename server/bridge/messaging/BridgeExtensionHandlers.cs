
namespace Core.Bridge.Handlers;

/// <summary>
/// 认证处理器 — 处理 auth/verify 控制请求,校验 JWT 令牌有效性
/// </summary>
public sealed class AuthHandler : ControlRequestHandlerBase {
    private readonly BridgeJwtService _jwtService;

    /// <summary>当前处理器负责的消息类型标识</summary>
    public override string MessageType => "auth/verify";

    /// <summary>
    /// 构造认证处理器
    /// </summary>
    /// <param name="jwtService">JWT 令牌校验服务</param>
    public AuthHandler(BridgeJwtService jwtService) {
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
    }

    /// <summary>
    /// 处理认证校验请求 — 从参数提取 token 并校验,返回校验结果
    /// </summary>
    /// <param name="request">控制请求对象</param>
    /// <param name="parameters">请求参数字典</param>
    /// <param name="context">消息处理上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>认证校验响应</returns>
    protected override Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken) {
        if (!parameters.TryGetValue("token", out var tokenElement)) {
            return Task.FromResult(CreateErrorResponse(request, "Missing 'token' parameter"));
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token)) {
            return Task.FromResult(CreateErrorResponse(request, "Token is empty"));
        }

        var validationResult = _jwtService.ValidateToken(token);

        return Task.FromResult(new ControlResponse {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = validationResult.IsValid,
            Result = validationResult.IsValid
                ? JsonSerializer.SerializeToElement(
                    new AuthVerifyResultData { ClientId = validationResult.Payload?.Sub ?? string.Empty, IsValid = true },
                    BridgeJsonContext.Default.AuthVerifyResultData)
                : null,
            Error = validationResult.IsValid ? null : validationResult.Error
        });
    }
}

/// <summary>
/// 会话处理器 — 处理 session/manage 控制请求,支持 create/close/keepAlive 三种动作
/// </summary>
public sealed class SessionHandler : ControlRequestHandlerBase {
    private readonly BridgeSessionRunner _sessionRunner;

    /// <summary>当前处理器负责的消息类型标识</summary>
    public override string MessageType => "session/manage";

    /// <summary>
    /// 构造会话处理器
    /// </summary>
    /// <param name="sessionRunner">会话运行器,负责会话生命周期管理</param>
    public SessionHandler(BridgeSessionRunner sessionRunner) {
        _sessionRunner = sessionRunner ?? throw new ArgumentNullException(nameof(sessionRunner));
    }

    /// <summary>
    /// 处理会话管理请求 — 根据 action 参数分发到 create/close/keepAlive 分支
    /// </summary>
    /// <param name="request">控制请求对象</param>
    /// <param name="parameters">请求参数字典</param>
    /// <param name="context">消息处理上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话管理响应</returns>
    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken) {
        var action = GetOptionalString(parameters, "action");

        switch (action) {
            case "create": {
                var clientId = GetRequiredString(parameters, "clientId");
                if (string.IsNullOrWhiteSpace(clientId))
                    return CreateErrorResponse(request, "Missing 'clientId' parameter");

                var session = await _sessionRunner.StartSessionAsync(clientId, cancellationToken: cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new SessionManageResultData { SessionId = session.SessionId, Status = session.Status.ToString() },
                    BridgeJsonContext.Default.SessionManageResultData));
            }

            case "close": {
                var sessionId = GetRequiredString(parameters, "sessionId");
                if (string.IsNullOrWhiteSpace(sessionId))
                    return CreateErrorResponse(request, "Missing 'sessionId' parameter");

                await _sessionRunner.StopSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request);
            }

            case "keepAlive": {
                var sessionId = GetRequiredString(parameters, "sessionId");
                if (string.IsNullOrWhiteSpace(sessionId))
                    return CreateErrorResponse(request, "Missing 'sessionId' parameter");

                var keptAlive = await _sessionRunner.KeepAliveAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return keptAlive
                    ? CreateSuccessResponse(request)
                    : CreateErrorResponse(request, "Session not found or already closed");
            }

            default:
            return CreateErrorResponse(request, $"Unknown session action: {action}");
        }
    }
}

/// <summary>
/// 设备信任处理器 — 处理 device/trust 控制请求,支持 verify/trust/revoke 三种动作
/// </summary>
public sealed class DeviceTrustHandler : ControlRequestHandlerBase {
    private readonly ITrustedDeviceStore _trustedDeviceStore;

    /// <summary>当前处理器负责的消息类型标识</summary>
    public override string MessageType => "device/trust";

    /// <summary>
    /// 构造设备信任处理器
    /// </summary>
    /// <param name="trustedDeviceStore">可信设备存储</param>
    public DeviceTrustHandler(ITrustedDeviceStore trustedDeviceStore) {
        _trustedDeviceStore = trustedDeviceStore ?? throw new ArgumentNullException(nameof(trustedDeviceStore));
    }

    /// <summary>
    /// 处理设备信任请求 — 根据 action 参数分发到 verify/trust/revoke 分支
    /// </summary>
    /// <param name="request">控制请求对象</param>
    /// <param name="parameters">请求参数字典</param>
    /// <param name="context">消息处理上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备信任响应</returns>
    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken) {
        var action = GetOptionalString(parameters, "action");
        var deviceId = GetOptionalString(parameters, "deviceId");

        switch (action) {
            case "verify": {
                if (string.IsNullOrWhiteSpace(deviceId))
                    return CreateErrorResponse(request, "Missing 'deviceId' parameter");

                var isTrusted = await _trustedDeviceStore.IsTrustedAsync(deviceId, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new DeviceTrustResultData { DeviceId = deviceId, IsTrusted = isTrusted },
                    BridgeJsonContext.Default.DeviceTrustResultData));
            }

            case "trust": {
                if (string.IsNullOrWhiteSpace(deviceId))
                    return CreateErrorResponse(request, "Missing 'deviceId' parameter");

                var deviceName = GetOptionalString(parameters, "deviceName") ?? deviceId;
                var fingerprint = GetOptionalString(parameters, "publicKeyFingerprint") ?? string.Empty;

                var entry = new TrustedDeviceEntry {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    PublicKeyFingerprint = fingerprint,
                    TrustLevel = DeviceTrustLevel.Basic
                };

                await _trustedDeviceStore.AddAsync(entry, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request);
            }

            case "revoke": {
                if (string.IsNullOrWhiteSpace(deviceId))
                    return CreateErrorResponse(request, "Missing 'deviceId' parameter");

                var revoked = await _trustedDeviceStore.RevokeAsync(deviceId, cancellationToken).ConfigureAwait(false);
                return revoked
                    ? CreateSuccessResponse(request)
                    : CreateErrorResponse(request, "Device not found or already revoked");
            }

            default:
            return CreateErrorResponse(request, $"Unknown device action: {action}");
        }
    }
}

/// <summary>
/// 密钥处理器 — 处理 secret/manage 控制请求,支持 validate/rotate 两种动作
/// </summary>
public sealed class SecretHandler : ControlRequestHandlerBase {
    private readonly IWorkSecretStore _workSecretStore;

    /// <summary>当前处理器负责的消息类型标识</summary>
    public override string MessageType => "secret/manage";

    /// <summary>
    /// 构造密钥处理器
    /// </summary>
    /// <param name="workSecretStore">工作密钥存储</param>
    public SecretHandler(IWorkSecretStore workSecretStore) {
        _workSecretStore = workSecretStore ?? throw new ArgumentNullException(nameof(workSecretStore));
    }

    /// <summary>
    /// 处理密钥管理请求 — 根据 action 参数分发到 validate/rotate 分支
    /// </summary>
    /// <param name="request">控制请求对象</param>
    /// <param name="parameters">请求参数字典</param>
    /// <param name="context">消息处理上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>密钥管理响应</returns>
    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken) {
        var action = GetOptionalString(parameters, "action");

        switch (action) {
            case "validate": {
                var secretId = GetRequiredString(parameters, "secretId");
                var plainValue = GetRequiredString(parameters, "plainValue");

                if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(plainValue))
                    return CreateErrorResponse(request, "Missing 'secretId' or 'plainValue' parameter");

                var isValid = await _workSecretStore.ValidateAsync(secretId, plainValue, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new SecretValidateResultData { SecretId = secretId, IsValid = isValid },
                    BridgeJsonContext.Default.SecretValidateResultData));
            }

            case "rotate": {
                var secretId = GetRequiredString(parameters, "secretId");
                var newPlainValue = GetRequiredString(parameters, "newPlainValue");

                if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(newPlainValue))
                    return CreateErrorResponse(request, "Missing 'secretId' or 'newPlainValue' parameter");

                var newEntry = await _workSecretStore.RotateAsync(secretId, newPlainValue, ct: cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new SecretRotateResultData { NewSecretId = newEntry.SecretId, Name = newEntry.Name },
                    BridgeJsonContext.Default.SecretRotateResultData));
            }

            default:
            return CreateErrorResponse(request, $"Unknown secret action: {action}");
        }
    }
}

/// <summary>
/// 对端处理器 — 处理 peer/manage 控制请求,支持 connect/disconnect 两种动作
/// </summary>
public sealed class PeerHandler : ControlRequestHandlerBase {
    private readonly PeerSessionManager _peerSessionManager;

    /// <summary>当前处理器负责的消息类型标识</summary>
    public override string MessageType => "peer/manage";

    /// <summary>
    /// 构造对端处理器
    /// </summary>
    /// <param name="peerSessionManager">对端会话管理器</param>
    public PeerHandler(PeerSessionManager peerSessionManager) {
        _peerSessionManager = peerSessionManager ?? throw new ArgumentNullException(nameof(peerSessionManager));
    }

    /// <summary>
    /// 处理对端管理请求 — 根据 action 参数分发到 connect/disconnect 分支
    /// </summary>
    /// <param name="request">控制请求对象</param>
    /// <param name="parameters">请求参数字典</param>
    /// <param name="context">消息处理上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>对端管理响应</returns>
    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken) {
        var action = GetOptionalString(parameters, "action");

        switch (action) {
            case "connect": {
                var localPeerId = GetOptionalString(parameters, "localPeerId");
                var remotePeerId = GetOptionalString(parameters, "remotePeerId");

                if (string.IsNullOrWhiteSpace(localPeerId) || string.IsNullOrWhiteSpace(remotePeerId))
                    return CreateErrorResponse(request, "Missing 'localPeerId' or 'remotePeerId' parameter");

                var session = await _peerSessionManager.CreatePeerSessionAsync(localPeerId, remotePeerId, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new PeerManageResultData { SessionId = session.SessionId, Status = session.Status.ToString() },
                    BridgeJsonContext.Default.PeerManageResultData));
            }

            case "disconnect": {
                var sessionId = GetRequiredString(parameters, "sessionId");
                if (string.IsNullOrWhiteSpace(sessionId))
                    return CreateErrorResponse(request, "Missing 'sessionId' parameter");

                await _peerSessionManager.ClosePeerSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request);
            }

            default:
            return CreateErrorResponse(request, $"Unknown peer action: {action}");
        }
    }
}