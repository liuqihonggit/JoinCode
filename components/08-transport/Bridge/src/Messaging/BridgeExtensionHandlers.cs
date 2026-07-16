
namespace Core.Bridge.Handlers;

public sealed class AuthHandler : ControlRequestHandlerBase
{
    private readonly BridgeJwtService _jwtService;

    public override string MessageType => "auth/verify";

    public AuthHandler(BridgeJwtService jwtService)
    {
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
    }

    protected override Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("token", out var tokenElement))
        {
            return Task.FromResult(CreateErrorResponse(request, "Missing 'token' parameter"));
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(CreateErrorResponse(request, "Token is empty"));
        }

        var validationResult = _jwtService.ValidateToken(token);

        return Task.FromResult(new ControlResponse
        {
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

public sealed class SessionHandler : ControlRequestHandlerBase
{
    private readonly BridgeSessionRunner _sessionRunner;

    public override string MessageType => "session/manage";

    public SessionHandler(BridgeSessionRunner sessionRunner)
    {
        _sessionRunner = sessionRunner ?? throw new ArgumentNullException(nameof(sessionRunner));
    }

    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        var action = GetOptionalString(parameters, "action");

        switch (action)
        {
            case "create":
            {
                var clientId = GetRequiredString(parameters, "clientId");
                if (string.IsNullOrWhiteSpace(clientId))
                    return CreateErrorResponse(request, "Missing 'clientId' parameter");

                var session = await _sessionRunner.StartSessionAsync(clientId, cancellationToken: cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new SessionManageResultData { SessionId = session.SessionId, Status = session.Status.ToString() },
                    BridgeJsonContext.Default.SessionManageResultData));
            }

            case "close":
            {
                var sessionId = GetRequiredString(parameters, "sessionId");
                if (string.IsNullOrWhiteSpace(sessionId))
                    return CreateErrorResponse(request, "Missing 'sessionId' parameter");

                await _sessionRunner.StopSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request);
            }

            case "keepAlive":
            {
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

public sealed class DeviceTrustHandler : ControlRequestHandlerBase
{
    private readonly ITrustedDeviceStore _trustedDeviceStore;

    public override string MessageType => "device/trust";

    public DeviceTrustHandler(ITrustedDeviceStore trustedDeviceStore)
    {
        _trustedDeviceStore = trustedDeviceStore ?? throw new ArgumentNullException(nameof(trustedDeviceStore));
    }

    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        var action = GetOptionalString(parameters, "action");
        var deviceId = GetOptionalString(parameters, "deviceId");

        switch (action)
        {
            case "verify":
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                    return CreateErrorResponse(request, "Missing 'deviceId' parameter");

                var isTrusted = await _trustedDeviceStore.IsTrustedAsync(deviceId, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new DeviceTrustResultData { DeviceId = deviceId, IsTrusted = isTrusted },
                    BridgeJsonContext.Default.DeviceTrustResultData));
            }

            case "trust":
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                    return CreateErrorResponse(request, "Missing 'deviceId' parameter");

                var deviceName = GetOptionalString(parameters, "deviceName") ?? deviceId;
                var fingerprint = GetOptionalString(parameters, "publicKeyFingerprint") ?? string.Empty;

                var entry = new TrustedDeviceEntry
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    PublicKeyFingerprint = fingerprint,
                    TrustLevel = DeviceTrustLevel.Basic
                };

                await _trustedDeviceStore.AddAsync(entry, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request);
            }

            case "revoke":
            {
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

public sealed class SecretHandler : ControlRequestHandlerBase
{
    private readonly IWorkSecretStore _workSecretStore;

    public override string MessageType => "secret/manage";

    public SecretHandler(IWorkSecretStore workSecretStore)
    {
        _workSecretStore = workSecretStore ?? throw new ArgumentNullException(nameof(workSecretStore));
    }

    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        var action = GetOptionalString(parameters, "action");

        switch (action)
        {
            case "validate":
            {
                var secretId = GetRequiredString(parameters, "secretId");
                var plainValue = GetRequiredString(parameters, "plainValue");

                if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(plainValue))
                    return CreateErrorResponse(request, "Missing 'secretId' or 'plainValue' parameter");

                var isValid = await _workSecretStore.ValidateAsync(secretId, plainValue, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new SecretValidateResultData { SecretId = secretId, IsValid = isValid },
                    BridgeJsonContext.Default.SecretValidateResultData));
            }

            case "rotate":
            {
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

public sealed class PeerHandler : ControlRequestHandlerBase
{
    private readonly PeerSessionManager _peerSessionManager;

    public override string MessageType => "peer/manage";

    public PeerHandler(PeerSessionManager peerSessionManager)
    {
        _peerSessionManager = peerSessionManager ?? throw new ArgumentNullException(nameof(peerSessionManager));
    }

    protected override async Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        var action = GetOptionalString(parameters, "action");

        switch (action)
        {
            case "connect":
            {
                var localPeerId = GetOptionalString(parameters, "localPeerId");
                var remotePeerId = GetOptionalString(parameters, "remotePeerId");

                if (string.IsNullOrWhiteSpace(localPeerId) || string.IsNullOrWhiteSpace(remotePeerId))
                    return CreateErrorResponse(request, "Missing 'localPeerId' or 'remotePeerId' parameter");

                var session = await _peerSessionManager.CreatePeerSessionAsync(localPeerId, remotePeerId, cancellationToken).ConfigureAwait(false);
                return CreateSuccessResponse(request, JsonSerializer.SerializeToElement(
                    new PeerManageResultData { SessionId = session.SessionId, Status = session.Status.ToString() },
                    BridgeJsonContext.Default.PeerManageResultData));
            }

            case "disconnect":
            {
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
