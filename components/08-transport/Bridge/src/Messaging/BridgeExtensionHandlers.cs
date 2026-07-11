
namespace Core.Bridge.Handlers;

/// <summary>
/// 认证处理器 - 处理 JWT Token 验证请求
/// </summary>
public sealed class AuthHandler : IMessageHandler
{
    private readonly BridgeJwtService _jwtService;

    public string MessageType => "auth/verify";

    public AuthHandler(BridgeJwtService jwtService)
    {
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
    }

    /// <inheritdoc />
    public Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[AuthHandler] 处理认证验证请求");

        if (message is not ControlRequest request)
        {
            return Task.FromResult<BridgeMessage>(new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid auth/verify request"
            });
        }

        var parameters = request.GetParams();
        if (!parameters.TryGetValue("token", out var tokenElement))
        {
            return Task.FromResult<BridgeMessage>(new ControlResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.Id,
                Success = false,
                Error = "Missing 'token' parameter"
            });
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<BridgeMessage>(new ControlResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.Id,
                Success = false,
                Error = "Token is empty"
            });
        }

        var validationResult = _jwtService.ValidateToken(token);

        return Task.FromResult<BridgeMessage>(new ControlResponse
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

/// <summary>
/// 会话管理处理器 - 处理会话创建、关闭和心跳
/// </summary>
public sealed class SessionHandler : IMessageHandler
{
    private readonly BridgeSessionRunner _sessionRunner;

    public string MessageType => "session/manage";

    public SessionHandler(BridgeSessionRunner sessionRunner)
    {
        _sessionRunner = sessionRunner ?? throw new ArgumentNullException(nameof(sessionRunner));
    }

    /// <inheritdoc />
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[SessionHandler] 处理会话管理请求");

        if (message is not ControlRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid session/manage request"
            };
        }

        var parameters = request.GetParams();
        var action = parameters.TryGetValue("action", out var actionElement) ? actionElement.GetString() : null;

        try
        {
            switch (action)
            {
                case "create":
                {
                    var clientId = parameters.TryGetValue("clientId", out var clientIdElement) ? clientIdElement.GetString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(clientId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'clientId' parameter"
                        };
                    }

                    var session = await _sessionRunner.StartSessionAsync(clientId, cancellationToken: cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true,
                        Result = JsonSerializer.SerializeToElement(
                            new SessionManageResultData { SessionId = session.SessionId, Status = session.Status.ToString() },
                            BridgeJsonContext.Default.SessionManageResultData)
                    };
                }

                case "close":
                {
                    var sessionId = parameters.TryGetValue("sessionId", out var sidElement) ? sidElement.GetString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'sessionId' parameter"
                        };
                    }

                    await _sessionRunner.StopSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true
                    };
                }

                case "keepAlive":
                {
                    var sessionId = parameters.TryGetValue("sessionId", out var sidElement) ? sidElement.GetString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'sessionId' parameter"
                        };
                    }

                    var keptAlive = await _sessionRunner.KeepAliveAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = keptAlive,
                        Error = keptAlive ? null : "Session not found or already closed"
                    };
                }

                default:
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = false,
                        Error = $"Unknown session action: {action}"
                    };
            }
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "[SessionHandler] 会话操作失败: {Action}", action);
            return new ControlResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// 设备信任处理器 - 处理设备验证、信任和撤销
/// </summary>
public sealed class DeviceTrustHandler : IMessageHandler
{
    private readonly ITrustedDeviceStore _trustedDeviceStore;

    public string MessageType => "device/trust";

    public DeviceTrustHandler(ITrustedDeviceStore trustedDeviceStore)
    {
        _trustedDeviceStore = trustedDeviceStore ?? throw new ArgumentNullException(nameof(trustedDeviceStore));
    }

    /// <inheritdoc />
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[DeviceTrustHandler] 处理设备信任请求");

        if (message is not ControlRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid device/trust request"
            };
        }

        var parameters = request.GetParams();
        var action = parameters.TryGetValue("action", out var actionElement) ? actionElement.GetString() : null;
        var deviceId = parameters.TryGetValue("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() : null;

        try
        {
            switch (action)
            {
                case "verify":
                {
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'deviceId' parameter"
                        };
                    }

                    var isTrusted = await _trustedDeviceStore.IsTrustedAsync(deviceId, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true,
                        Result = JsonSerializer.SerializeToElement(
                            new DeviceTrustResultData { DeviceId = deviceId, IsTrusted = isTrusted },
                            BridgeJsonContext.Default.DeviceTrustResultData)
                    };
                }

                case "trust":
                {
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'deviceId' parameter"
                        };
                    }

                    var deviceName = parameters.TryGetValue("deviceName", out var nameElement) ? nameElement.GetString() ?? deviceId : deviceId;
                    var fingerprint = parameters.TryGetValue("publicKeyFingerprint", out var fpElement) ? fpElement.GetString() ?? string.Empty : string.Empty;

                    var entry = new TrustedDeviceEntry
                    {
                        DeviceId = deviceId,
                        DeviceName = deviceName,
                        PublicKeyFingerprint = fingerprint,
                        TrustLevel = DeviceTrustLevel.Basic
                    };

                    await _trustedDeviceStore.AddAsync(entry, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true
                    };
                }

                case "revoke":
                {
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'deviceId' parameter"
                        };
                    }

                    var revoked = await _trustedDeviceStore.RevokeAsync(deviceId, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = revoked,
                        Error = revoked ? null : "Device not found or already revoked"
                    };
                }

                default:
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = false,
                        Error = $"Unknown device action: {action}"
                    };
            }
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "[DeviceTrustHandler] 设备信任操作失败: {Action}", action);
            return new ControlResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// 密钥管理处理器 - 处理密钥验证和轮换
/// </summary>
public sealed class SecretHandler : IMessageHandler
{
    private readonly IWorkSecretStore _workSecretStore;

    public string MessageType => "secret/manage";

    public SecretHandler(IWorkSecretStore workSecretStore)
    {
        _workSecretStore = workSecretStore ?? throw new ArgumentNullException(nameof(workSecretStore));
    }

    /// <inheritdoc />
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[SecretHandler] 处理密钥管理请求");

        if (message is not ControlRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid secret/manage request"
            };
        }

        var parameters = request.GetParams();
        var action = parameters.TryGetValue("action", out var actionElement) ? actionElement.GetString() : null;

        try
        {
            switch (action)
            {
                case "validate":
                {
                    var secretId = parameters.TryGetValue("secretId", out var sidElement) ? sidElement.GetString() : null;
                    var plainValue = parameters.TryGetValue("plainValue", out var pvElement) ? pvElement.GetString() : null;

                    if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(plainValue))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'secretId' or 'plainValue' parameter"
                        };
                    }

                    var isValid = await _workSecretStore.ValidateAsync(secretId, plainValue, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true,
                        Result = JsonSerializer.SerializeToElement(
                            new SecretValidateResultData { SecretId = secretId, IsValid = isValid },
                            BridgeJsonContext.Default.SecretValidateResultData)
                    };
                }

                case "rotate":
                {
                    var secretId = parameters.TryGetValue("secretId", out var sidElement) ? sidElement.GetString() : null;
                    var newPlainValue = parameters.TryGetValue("newPlainValue", out var npvElement) ? npvElement.GetString() : null;

                    if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(newPlainValue))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'secretId' or 'newPlainValue' parameter"
                        };
                    }

                    var newEntry = await _workSecretStore.RotateAsync(secretId, newPlainValue, ct: cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true,
                        Result = JsonSerializer.SerializeToElement(
                            new SecretRotateResultData { NewSecretId = newEntry.SecretId, Name = newEntry.Name },
                            BridgeJsonContext.Default.SecretRotateResultData)
                    };
                }

                default:
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = false,
                        Error = $"Unknown secret action: {action}"
                    };
            }
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "[SecretHandler] 密钥操作失败: {Action}", action);
            return new ControlResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// 对等会话处理器 - 处理对等节点连接和断开
/// </summary>
public sealed class PeerHandler : IMessageHandler
{
    private readonly PeerSessionManager _peerSessionManager;

    public string MessageType => "peer/manage";

    public PeerHandler(PeerSessionManager peerSessionManager)
    {
        _peerSessionManager = peerSessionManager ?? throw new ArgumentNullException(nameof(peerSessionManager));
    }

    /// <inheritdoc />
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[PeerHandler] 处理对等会话请求");

        if (message is not ControlRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid peer/manage request"
            };
        }

        var parameters = request.GetParams();
        var action = parameters.TryGetValue("action", out var actionElement) ? actionElement.GetString() : null;

        try
        {
            switch (action)
            {
                case "connect":
                {
                    var localPeerId = parameters.TryGetValue("localPeerId", out var localElement) ? localElement.GetString() : null;
                    var remotePeerId = parameters.TryGetValue("remotePeerId", out var remoteElement) ? remoteElement.GetString() : null;

                    if (string.IsNullOrWhiteSpace(localPeerId) || string.IsNullOrWhiteSpace(remotePeerId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'localPeerId' or 'remotePeerId' parameter"
                        };
                    }

                    var session = await _peerSessionManager.CreatePeerSessionAsync(localPeerId, remotePeerId, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true,
                        Result = JsonSerializer.SerializeToElement(
                            new PeerManageResultData { SessionId = session.SessionId, Status = session.Status.ToString() },
                            BridgeJsonContext.Default.PeerManageResultData)
                    };
                }

                case "disconnect":
                {
                    var sessionId = parameters.TryGetValue("sessionId", out var sidElement) ? sidElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        return new ControlResponse
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            RequestId = request.Id,
                            Success = false,
                            Error = "Missing 'sessionId' parameter"
                        };
                    }

                    await _peerSessionManager.ClosePeerSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = true
                    };
                }

                default:
                    return new ControlResponse
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RequestId = request.Id,
                        Success = false,
                        Error = $"Unknown peer action: {action}"
                    };
            }
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "[PeerHandler] 对等会话操作失败: {Action}", action);
            return new ControlResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }
}
