namespace Core.Bridge;


/// <summary>技能输出数据</summary>
public sealed record SkillOutputData {
    /// <summary>技能执行输出文本</summary>
    [JsonPropertyName("output")]
    public required string Output { get; init; }
}

/// <summary>Ping 时间戳数据</summary>
public sealed record PingTimestampData {
    /// <summary>Unix 毫秒时间戳</summary>
    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }
}

/// <summary>服务器状态数据</summary>
public sealed record ServerStatusData {
    /// <summary>工具数量</summary>
    [JsonPropertyName("toolCount")]
    public required int ToolCount { get; init; }

    /// <summary>技能数量</summary>
    [JsonPropertyName("skillCount")]
    public required int SkillCount { get; init; }

    /// <summary>Unix 毫秒时间戳</summary>
    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }
}

/// <summary>缓存清理结果数据</summary>
public sealed record CacheClearedData {
    /// <summary>是否已清理</summary>
    [JsonPropertyName("cleared")]
    public required bool Cleared { get; init; }
}

/// <summary>技能重新加载结果数据</summary>
public sealed record SkillsReloadedData {
    /// <summary>是否已重新加载</summary>
    [JsonPropertyName("reloaded")]
    public required bool Reloaded { get; init; }
}

/// <summary>
/// 消息处理器上下文
/// 封装处理消息所需的依赖，避免参数爆炸
/// </summary>
[Register(typeof(MessageHandlerContext), ServiceLifetime.Singleton)]
public sealed record MessageHandlerContext(
    IPlanService? PlanService = null,
    IChatService? ChatService = null,
    ICodeService? CodeService = null,
    IToolRegistry? ToolRegistry = null,
    ISkillService? SkillService = null,
    ILogger? Logger = null,
    BridgeJwtService? JwtService = null,
    BridgeSessionRunner? SessionRunner = null,
    ITrustedDeviceStore? TrustedDeviceStore = null,
    IWorkSecretStore? WorkSecretStore = null,
    PeerSessionManager? PeerSessionManager = null,
    ICacheService? CacheService = null);

/// <summary>
/// 消息处理器接口
/// 参考 TS 原版 的消息处理模式
/// </summary>
public interface IMessageHandler {
    /// <summary>消息类型标识</summary>
    string MessageType { get; }

    /// <summary>
    /// 异步处理消息
    /// </summary>
    /// <param name="message">待处理的 Bridge 消息</param>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的响应消息</returns>
    Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 消息处理器注册表
/// 统一管理所有消息处理器
/// </summary>
public sealed class MessageHandlerRegistry {
    private readonly Dictionary<string, IMessageHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册消息处理器
    /// </summary>
    /// <param name="handler">消息处理器实例</param>
    public void Register(IMessageHandler handler) {
        _handlers[handler.MessageType] = handler;
    }

    /// <summary>
    /// 批量注册消息处理器
    /// </summary>
    /// <param name="handlers">消息处理器集合</param>
    public void RegisterRange(IEnumerable<IMessageHandler> handlers) {
        foreach (var handler in handlers) {
            Register(handler);
        }
    }

    /// <summary>
    /// 尝试获取指定消息类型的处理器
    /// </summary>
    /// <param name="messageType">消息类型</param>
    /// <param name="handler">输出的处理器实例</param>
    /// <returns>是否找到处理器</returns>
    public bool TryGetHandler(string messageType, out IMessageHandler? handler) {
        return _handlers.TryGetValue(messageType, out handler);
    }

    /// <summary>
    /// 获取所有已注册的处理器
    /// </summary>
    /// <returns>消息类型到处理器的只读字典</returns>
    public IReadOnlyDictionary<string, IMessageHandler> GetAllHandlers() {
        return _handlers;
    }
}

/// <summary>
/// 初始化处理器
/// 处理客户端初始化请求
/// </summary>
public sealed class InitializeHandler : IMessageHandler {
    /// <summary>消息类型: initialize</summary>
    public string MessageType => "initialize";

    /// <summary>
    /// 处理初始化请求 — 返回协议版本、服务器信息和能力声明
    /// </summary>
    /// <param name="message">Bridge 消息</param>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>初始化响应消息</returns>
    public Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default) {
        context.Logger?.LogInformation("[InitializeHandler] 处理初始化请求");

        if (message is not InitializeRequest request) {
            return Task.FromResult<BridgeMessage>(new ErrorMessage {
                Code = -32600,
                Message = "Invalid initialize request"
            });
        }

        var response = new InitializeResponse {
            Id = request.Id,
            ProtocolVersion = "1.0",
            ServerInfo = new ServerInfo {
                Name = "Core.Bridge",
                Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0"
            },
            Capabilities = new ServerCapabilities {
                Tools = new ToolCapabilities { ListChanged = true },
                Skills = new SkillCapabilities { ListChanged = true }
            }
        };

        return Task.FromResult<BridgeMessage>(response);
    }
}

/// <summary>
/// 工具列表处理器
/// 返回可用的工具列表
/// </summary>
public sealed class ToolsListHandler : IMessageHandler {
    /// <summary>消息类型: tools/list</summary>
    public string MessageType => "tools/list";

    /// <summary>
    /// 处理工具列表请求 — 从 ToolRegistry 获取所有工具并返回定义列表
    /// </summary>
    /// <param name="message">Bridge 消息</param>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具列表响应消息</returns>
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default) {
        context.Logger?.LogInformation("[ToolsListHandler] 处理工具列表请求");

        var tools = context.ToolRegistry != null
            ? (await context.ToolRegistry.GetAllToolsAsync(cancellationToken).ConfigureAwait(false)).Values
                .Select(tool => new BridgeToolDefinition {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = ConvertToJsonElement(tool.InputSchema)
                })
                .ToList()
            : [];

        var response = new ToolsListResponse {
            Id = message.Id,
            Tools = tools
        };

        context.Logger?.LogDebug("[ToolsListHandler] 返回 {Count} 个工具", tools.Count);
        return response;
    }

    private static JsonElement ConvertToJsonElement(ToolSchema schema) {
        return JsonSerializer.SerializeToElement(schema, BridgeJsonContext.Default.ToolSchema);
    }
}

/// <summary>
/// 工具调用处理器
/// 执行指定的工具
/// </summary>
public sealed class ToolsCallHandler : IMessageHandler {
    /// <summary>消息类型: tools/call</summary>
    public string MessageType => "tools/call";

    /// <summary>
    /// 处理工具调用请求 — 从 ToolRegistry 查找并执行指定工具
    /// </summary>
    /// <param name="message">Bridge 消息</param>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具调用响应消息</returns>
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default) {
        context.Logger?.LogInformation("[ToolsCallHandler] 处理工具调用请求");

        if (message is not ToolsCallRequest request) {
            return new ErrorMessage {
                Code = -32600,
                Message = "Invalid tools/call request"
            };
        }

        if (context.ToolRegistry == null) {
            return new ErrorMessage {
                Code = -32603,
                Message = "Tool registry not available"
            };
        }

        try {
            var handler = await context.ToolRegistry.GetToolAsync(request.ToolName, cancellationToken).ConfigureAwait(false);
            if (handler == null) {
                return new ToolsCallResponse {
                    Id = Guid.NewGuid().ToString("N"),
                    ToolCallId = request.Id,
                    Success = false,
                    Error = $"Tool not found: {request.ToolName}"
                };
            }

            var arguments = request.Arguments ?? new Dictionary<string, JsonElement>();
            var result = await handler.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false);

            return new ToolsCallResponse {
                Id = Guid.NewGuid().ToString("N"),
                ToolCallId = request.Id,
                Success = result.Content?.Any(c => c.Type == ToolContentType.Error) != true,
                Result = result.Content != null ? JsonSerializer.SerializeToElement(result.Content, BridgeJsonContext.Default.ListToolContent) : null,
                Error = result.Content?.FirstOrDefault(c => c.Type == ToolContentType.Error)?.Text
            };
        } catch (Exception ex) {
            context.Logger?.LogError(ex, "[ToolsCallHandler] 工具调用失败: {ToolName}", request.ToolName);
            return new ToolsCallResponse {
                Id = Guid.NewGuid().ToString("N"),
                ToolCallId = request.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// 技能执行处理器
/// 执行指定的技能
/// </summary>
public sealed class SkillExecuteHandler : IMessageHandler {
    /// <summary>消息类型: skill/execute</summary>
    public string MessageType => "skill/execute";

    /// <summary>
    /// 处理技能执行请求 — 从 SkillService 查找并执行指定技能
    /// </summary>
    /// <param name="message">Bridge 消息</param>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能执行响应消息</returns>
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default) {
        context.Logger?.LogInformation("[SkillExecuteHandler] 处理技能执行请求");

        if (message is not SkillExecuteRequest request) {
            return new ErrorMessage {
                Code = -32600,
                Message = "Invalid skill/execute request"
            };
        }

        if (context.SkillService == null) {
            return new ErrorMessage {
                Code = -32603,
                Message = "Skill service not available"
            };
        }

        try {
            var skill = await context.SkillService.GetSkillAsync(request.SkillName, cancellationToken).ConfigureAwait(false);
            if (skill == null) {
                return new SkillExecuteResponse {
                    Id = Guid.NewGuid().ToString("N"),
                    Success = false,
                    Error = $"Skill not found: {request.SkillName}"
                };
            }

            var parameters = request.Parameters ?? new Dictionary<string, JsonElement>();
            var ctx = new ExecutionContext(cancellationToken);
            var result = await context.SkillService.ExecuteAsync(request.SkillName, parameters, ctx).ConfigureAwait(false);

            return new SkillExecuteResponse {
                Id = Guid.NewGuid().ToString("N"),
                Success = result.Success,
                Result = result.Success ? JsonSerializer.SerializeToElement(new SkillOutputData { Output = result.Output }, BridgeJsonContext.Default.SkillOutputData) : null,
                Error = result.ErrorMessage,
                ExecutionTimeMs = result.DurationMs ?? 0
            };
        } catch (Exception ex) {
            context.Logger?.LogError(ex, "[SkillExecuteHandler] 技能执行失败: {SkillName}", request.SkillName);
            return new SkillExecuteResponse {
                Id = Guid.NewGuid().ToString("N"),
                Success = false,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// 控制请求处理器
/// 处理来自 IDE 的控制命令
/// </summary>
public sealed class ControlRequestHandler : IMessageHandler {
    /// <summary>消息类型: control_request</summary>
    public string MessageType => "control_request";

    private static readonly Dictionary<string, Func<ControlRequest, MessageHandlerContext, CancellationToken, Task<ControlResponse>>> CommandHandlers = new(StringComparer.OrdinalIgnoreCase) {
        ["ping"] = HandlePingAsync,
        ["getStatus"] = HandleGetStatusAsync,
        ["clearCache"] = HandleClearCacheAsync,
        ["reloadSkills"] = HandleReloadSkillsAsync
    };

    /// <summary>
    /// 处理控制请求 — 分发到 ping/getStatus/clearCache/reloadSkills 子命令
    /// </summary>
    /// <param name="message">Bridge 消息</param>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>控制响应消息</returns>
    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default) {
        context.Logger?.LogInformation("[ControlRequestHandler] 处理控制请求");

        if (message is not ControlRequest request) {
            return new ErrorMessage {
                Code = -32600,
                Message = "Invalid control_request"
            };
        }

        if (CommandHandlers.TryGetValue(request.Command, out var handler)) {
            return await handler(request, context, cancellationToken).ConfigureAwait(false);
        }

        return new ControlResponse {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = false,
            Error = $"Unknown command: {request.Command}"
        };
    }

    private static Task<ControlResponse> HandlePingAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken) {
        return Task.FromResult(new ControlResponse {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = JsonSerializer.SerializeToElement(new PingTimestampData { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, BridgeJsonContext.Default.PingTimestampData)
        });
    }

    private static async Task<ControlResponse> HandleGetStatusAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken) {
        var toolCount = context.ToolRegistry != null
            ? await context.ToolRegistry.GetCountAsync(cancellationToken).ConfigureAwait(false)
            : 0;
        var skills = context.SkillService is not null
            ? await context.SkillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var skillCount = skills?.Count ?? 0;
        var status = new ServerStatusData {
            ToolCount = toolCount,
            SkillCount = skillCount,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return new ControlResponse {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = JsonSerializer.SerializeToElement(status, BridgeJsonContext.Default.ServerStatusData)
        };
    }

    private static async Task<ControlResponse> HandleClearCacheAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken) {
        // 接入 ICacheService 清理缓存
        if (context.CacheService != null) {
            await context.CacheService.ClearAsync(cancellationToken).ConfigureAwait(false);
            context.Logger?.LogInformation("[ControlRequestHandler] 缓存已清理");
        } else {
            context.Logger?.LogWarning("[ControlRequestHandler] 缓存服务不可用，跳过清理");
        }

        return new ControlResponse {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = JsonSerializer.SerializeToElement(new CacheClearedData { Cleared = true }, BridgeJsonContext.Default.CacheClearedData)
        };
    }

    private static async Task<ControlResponse> HandleReloadSkillsAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken) {
        // 接入 ISkillService 重新加载技能
        if (context.SkillService != null) {
            var ctx = new ExecutionContext(cancellationToken);
            var reloaded = await context.SkillService.ReloadAsync(skillName: null, ctx, cancellationToken).ConfigureAwait(false);
            context.Logger?.LogInformation("[ControlRequestHandler] 技能重新加载: {Result}", reloaded ? "成功" : "失败");
        } else {
            context.Logger?.LogWarning("[ControlRequestHandler] 技能服务不可用，跳过重新加载");
        }

        return new ControlResponse {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = JsonSerializer.SerializeToElement(new SkillsReloadedData { Reloaded = true }, BridgeJsonContext.Default.SkillsReloadedData)
        };
    }
}

/// <summary>
/// 心跳处理器
/// </summary>
public sealed class PingHandler : IMessageHandler {
    /// <summary>消息类型: ping</summary>
    public string MessageType => "ping";

    /// <summary>
    /// 处理心跳请求 — 返回 Pong 消息
    /// </summary>
    /// <param name="message">Bridge 消息</param>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Pong 响应消息</returns>
    public Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default) {
        return Task.FromResult<BridgeMessage>(new PongMessage {
            Id = message.Id
        });
    }
}

/// <summary>
/// 消息处理器协调器
/// 统一协调所有消息的处理
/// </summary>
[Register(typeof(MessageHandlerCoordinator), ServiceLifetime.Singleton)]
public sealed partial class MessageHandlerCoordinator : ServiceEntity {
    private readonly MessageHandlerRegistry _registry;
    private readonly MessageHandlerContext _context;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造消息处理器协调器 — 注册默认处理器和条件扩展处理器
    /// </summary>
    /// <param name="context">消息处理器上下文</param>
    /// <param name="logger">日志记录器（可选）</param>
    public MessageHandlerCoordinator(MessageHandlerContext context, ILogger? logger = null) {
        _context = context;
        _logger = logger;
        _registry = new MessageHandlerRegistry();
        RegisterDefaultHandlers();
    }

    private void RegisterDefaultHandlers() {
        _registry.RegisterRange(new IMessageHandler[]
        {
            new InitializeHandler(),
            new ToolsListHandler(),
            new ToolsCallHandler(),
            new SkillExecuteHandler(),
            new ControlRequestHandler(),
            new PingHandler()
        });

        // 条件注册扩展处理器 - 仅当对应服务可用时注册
        if (_context.JwtService is not null)
            _registry.Register(new AuthHandler(_context.JwtService));
        if (_context.SessionRunner is not null)
            _registry.Register(new SessionHandler(_context.SessionRunner));
        if (_context.TrustedDeviceStore is not null)
            _registry.Register(new DeviceTrustHandler(_context.TrustedDeviceStore));
        if (_context.WorkSecretStore is not null)
            _registry.Register(new SecretHandler(_context.WorkSecretStore));
        if (_context.PeerSessionManager is not null)
            _registry.Register(new PeerHandler(_context.PeerSessionManager));
    }

    /// <summary>
    /// 处理消息
    /// </summary>
    public async Task<BridgeMessage?> HandleAsync(BridgeMessage message, CancellationToken cancellationToken = default) {
        _logger?.LogDebug("[MessageHandlerCoordinator] 处理消息: {MessageType}", message.Type);

        if (_registry.TryGetHandler(message.Type, out var handler) && handler != null) {
            try {
                return await handler.HandleAsync(message, _context, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogError(ex, "[MessageHandlerCoordinator] 处理消息失败: {MessageType}", message.Type);
                return new ErrorMessage {
                    Code = -32603,
                    Message = $"Internal error: {ex.Message}"
                };
            }
        }

        _logger?.LogWarning("[MessageHandlerCoordinator] 未找到处理器: {MessageType}", message.Type);
        return new ErrorMessage {
            Code = -32601,
            Message = $"Method not found: {message.Type}"
        };
    }

    /// <summary>
    /// 注册自定义处理器
    /// </summary>
    public void RegisterHandler(IMessageHandler handler) {
        _registry.Register(handler);
        _logger?.LogDebug("[MessageHandlerCoordinator] 注册处理器: {MessageType}", handler.MessageType);
    }
}

/// <summary>
/// 消息处理结果
/// </summary>
public sealed class MessageProcessResult {
    /// <summary>是否处理成功</summary>
    public bool Success { get; init; }
    /// <summary>响应消息</summary>
    public BridgeMessage? Response { get; init; }
    /// <summary>错误信息</summary>
    public string? Error { get; init; }
    /// <summary>处理耗时（毫秒）</summary>
    public long ProcessingTimeMs { get; init; }
}

#region Extension Handler Response Data Models

/// <summary>认证验证结果数据</summary>
public sealed class AuthVerifyResultData {
    /// <summary>客户端 ID</summary>
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    /// <summary>是否验证通过</summary>
    [JsonPropertyName("isValid")]
    public required bool IsValid { get; init; }
}

/// <summary>会话管理结果数据</summary>
public sealed class SessionManageResultData {
    /// <summary>会话 ID</summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>会话状态</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>设备信任结果数据</summary>
public sealed class DeviceTrustResultData {
    /// <summary>设备 ID</summary>
    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    /// <summary>是否受信任</summary>
    [JsonPropertyName("isTrusted")]
    public required bool IsTrusted { get; init; }
}

/// <summary>密钥验证结果数据</summary>
public sealed class SecretValidateResultData {
    /// <summary>密钥 ID</summary>
    [JsonPropertyName("secretId")]
    public required string SecretId { get; init; }

    /// <summary>是否验证通过</summary>
    [JsonPropertyName("isValid")]
    public required bool IsValid { get; init; }
}

/// <summary>密钥轮换结果数据</summary>
public sealed class SecretRotateResultData {
    /// <summary>新密钥 ID</summary>
    [JsonPropertyName("newSecretId")]
    public required string NewSecretId { get; init; }

    /// <summary>密钥名称</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>对等会话管理结果数据</summary>
public sealed class PeerManageResultData {
    /// <summary>会话 ID</summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>会话状态</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

#endregion