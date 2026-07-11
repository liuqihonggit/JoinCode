
namespace Core.Bridge;

using JoinCode.Abstractions.Attributes;

public sealed record SkillOutputData
{
    [JsonPropertyName("output")]
    public required string Output { get; init; }
}

public sealed record PingTimestampData
{
    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }
}

public sealed record ServerStatusData
{
    [JsonPropertyName("toolCount")]
    public required int ToolCount { get; init; }

    [JsonPropertyName("skillCount")]
    public required int SkillCount { get; init; }

    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }
}

public sealed record CacheClearedData
{
    [JsonPropertyName("cleared")]
    public required bool Cleared { get; init; }
}

public sealed record SkillsReloadedData
{
    [JsonPropertyName("reloaded")]
    public required bool Reloaded { get; init; }
}

/// <summary>
/// 消息处理器上下文
/// 封装处理消息所需的依赖，避免参数爆炸
/// </summary>
[Register]
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
/// 模仿 Claude Code 的消息处理模式
/// </summary>
public interface IMessageHandler
{
    string MessageType { get; }
    Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 消息处理器注册表
/// 统一管理所有消息处理器
/// </summary>
public sealed class MessageHandlerRegistry
{
    private readonly Dictionary<string, IMessageHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IMessageHandler handler)
    {
        _handlers[handler.MessageType] = handler;
    }

    public void RegisterRange(IEnumerable<IMessageHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            Register(handler);
        }
    }

    public bool TryGetHandler(string messageType, out IMessageHandler? handler)
    {
        return _handlers.TryGetValue(messageType, out handler);
    }

    public IReadOnlyDictionary<string, IMessageHandler> GetAllHandlers()
    {
        return _handlers;
    }
}

/// <summary>
/// 初始化处理器
/// 处理客户端初始化请求
/// </summary>
public sealed class InitializeHandler : IMessageHandler
{
    public string MessageType => "initialize";

    public Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[InitializeHandler] 处理初始化请求");

        if (message is not InitializeRequest request)
        {
            return Task.FromResult<BridgeMessage>(new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid initialize request"
            });
        }

        var response = new InitializeResponse
        {
            Id = request.Id,
            ProtocolVersion = "1.0",
            ServerInfo = new ServerInfo
            {
                Name = "Core.Bridge",
                Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0"
            },
            Capabilities = new ServerCapabilities
            {
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
public sealed class ToolsListHandler : IMessageHandler
{
    public string MessageType => "tools/list";

    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[ToolsListHandler] 处理工具列表请求");

        var tools = context.ToolRegistry != null
            ? (await context.ToolRegistry.GetAllToolsAsync(cancellationToken)).Values
                .Select(tool => new BridgeToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = ConvertToJsonElement(tool.InputSchema)
                })
                .ToList()
            : [];

        var response = new ToolsListResponse
        {
            Id = message.Id,
            Tools = tools
        };

        context.Logger?.LogDebug("[ToolsListHandler] 返回 {Count} 个工具", tools.Count);
        return response;
    }

    private static JsonElement ConvertToJsonElement(ToolSchema schema)
    {
        return JsonSerializer.SerializeToElement(schema, BridgeJsonContext.Default.ToolSchema);
    }
}

/// <summary>
/// 工具调用处理器
/// 执行指定的工具
/// </summary>
public sealed class ToolsCallHandler : IMessageHandler
{
    public string MessageType => "tools/call";

    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[ToolsCallHandler] 处理工具调用请求");

        if (message is not ToolsCallRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid tools/call request"
            };
        }

        if (context.ToolRegistry == null)
        {
            return new ErrorMessage
            {
                Code = -32603,
                Message = "Tool registry not available"
            };
        }

        try
        {
            var handler = await context.ToolRegistry.GetToolAsync(request.ToolName, cancellationToken).ConfigureAwait(false);
            if (handler == null)
            {
                return new ToolsCallResponse
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ToolCallId = request.Id,
                    Success = false,
                    Error = $"Tool not found: {request.ToolName}"
                };
            }

            var arguments = request.Arguments ?? new Dictionary<string, JsonElement>();
            var result = await handler.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false);

            return new ToolsCallResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                ToolCallId = request.Id,
                Success = result.Content?.Any(c => c.Type == ToolContentType.Error) != true,
                Result = result.Content != null ? JsonSerializer.SerializeToElement(result.Content, BridgeJsonContext.Default.ListToolContent) : null,
                Error = result.Content?.FirstOrDefault(c => c.Type == ToolContentType.Error)?.Text
            };
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "[ToolsCallHandler] 工具调用失败: {ToolName}", request.ToolName);
            return new ToolsCallResponse
            {
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
public sealed class SkillExecuteHandler : IMessageHandler
{
    public string MessageType => "skill/execute";

    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[SkillExecuteHandler] 处理技能执行请求");

        if (message is not SkillExecuteRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid skill/execute request"
            };
        }

        if (context.SkillService == null)
        {
            return new ErrorMessage
            {
                Code = -32603,
                Message = "Skill service not available"
            };
        }

        try
        {
            var skill = await context.SkillService.GetSkillAsync(request.SkillName, cancellationToken).ConfigureAwait(false);
            if (skill == null)
            {
                return new SkillExecuteResponse
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Success = false,
                    Error = $"Skill not found: {request.SkillName}"
                };
            }

            var parameters = request.Parameters ?? new Dictionary<string, JsonElement>();
            var ctx = new SkillExecutionContext(cancellationToken);
            var result = await context.SkillService.ExecuteAsync(request.SkillName, parameters, ctx).ConfigureAwait(false);

            return new SkillExecuteResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                Success = result.Success,
                Result = result.Success ? JsonSerializer.SerializeToElement(new SkillOutputData { Output = result.Output }, BridgeJsonContext.Default.SkillOutputData) : null,
                Error = result.ErrorMessage,
                ExecutionTimeMs = result.DurationMs ?? 0
            };
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "[SkillExecuteHandler] 技能执行失败: {SkillName}", request.SkillName);
            return new SkillExecuteResponse
            {
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
public sealed class ControlRequestHandler : IMessageHandler
{
    public string MessageType => "control_request";

    private static readonly Dictionary<string, Func<ControlRequest, MessageHandlerContext, CancellationToken, Task<ControlResponse>>> CommandHandlers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ping"] = HandlePingAsync,
        ["getStatus"] = HandleGetStatusAsync,
        ["clearCache"] = HandleClearCacheAsync,
        ["reloadSkills"] = HandleReloadSkillsAsync
    };

    public async Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        context.Logger?.LogInformation("[ControlRequestHandler] 处理控制请求");

        if (message is not ControlRequest request)
        {
            return new ErrorMessage
            {
                Code = -32600,
                Message = "Invalid control_request"
            };
        }

        if (CommandHandlers.TryGetValue(request.Command, out var handler))
        {
            return await handler(request, context, cancellationToken).ConfigureAwait(false);
        }

        return new ControlResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = false,
            Error = $"Unknown command: {request.Command}"
        };
    }

    private static Task<ControlResponse> HandlePingAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ControlResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = JsonSerializer.SerializeToElement(new PingTimestampData { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, BridgeJsonContext.Default.PingTimestampData)
        });
    }

    private static async Task<ControlResponse> HandleGetStatusAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        var toolCount = context.ToolRegistry != null
            ? await context.ToolRegistry.GetCountAsync(cancellationToken).ConfigureAwait(false)
            : 0;
        var skills = context.SkillService is not null
            ? await context.SkillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var skillCount = skills?.Count ?? 0;
        var status = new ServerStatusData
        {
            ToolCount = toolCount,
            SkillCount = skillCount,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return new ControlResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = JsonSerializer.SerializeToElement(status, BridgeJsonContext.Default.ServerStatusData)
        };
    }

    private static async Task<ControlResponse> HandleClearCacheAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        // 接入 ICacheService 清理缓存
        if (context.CacheService != null)
        {
            await context.CacheService.ClearAsync(cancellationToken).ConfigureAwait(false);
            context.Logger?.LogInformation("[ControlRequestHandler] 缓存已清理");
        }
        else
        {
            context.Logger?.LogWarning("[ControlRequestHandler] 缓存服务不可用，跳过清理");
        }

        return new ControlResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            RequestId = request.Id,
            Success = true,
            Result = JsonSerializer.SerializeToElement(new CacheClearedData { Cleared = true }, BridgeJsonContext.Default.CacheClearedData)
        };
    }

    private static async Task<ControlResponse> HandleReloadSkillsAsync(ControlRequest request, MessageHandlerContext context, CancellationToken cancellationToken)
    {
        // 接入 ISkillService 重新加载技能
        if (context.SkillService != null)
        {
            var ctx = new SkillExecutionContext(cancellationToken);
            var reloaded = await context.SkillService.ReloadAsync(skillName: null, ctx, cancellationToken).ConfigureAwait(false);
            context.Logger?.LogInformation("[ControlRequestHandler] 技能重新加载: {Result}", reloaded ? "成功" : "失败");
        }
        else
        {
            context.Logger?.LogWarning("[ControlRequestHandler] 技能服务不可用，跳过重新加载");
        }

        return new ControlResponse
        {
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
public sealed class PingHandler : IMessageHandler
{
    public string MessageType => "ping";

    public Task<BridgeMessage> HandleAsync(BridgeMessage message, MessageHandlerContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<BridgeMessage>(new PongMessage
        {
            Id = message.Id
        });
    }
}

/// <summary>
/// 消息处理器协调器
/// 统一协调所有消息的处理
/// </summary>
[Register]
public sealed partial class MessageHandlerCoordinator
{
    private readonly MessageHandlerRegistry _registry;
    private readonly MessageHandlerContext _context;
    private readonly ILogger? _logger;

    public MessageHandlerCoordinator(MessageHandlerContext context, ILogger? logger = null)
    {
        _context = context;
        _logger = logger;
        _registry = new MessageHandlerRegistry();
        RegisterDefaultHandlers();
    }

    private void RegisterDefaultHandlers()
    {
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
    public async Task<BridgeMessage?> HandleAsync(BridgeMessage message, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("[MessageHandlerCoordinator] 处理消息: {MessageType}", message.Type);

        if (_registry.TryGetHandler(message.Type, out var handler) && handler != null)
        {
            try
            {
                return await handler.HandleAsync(message, _context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[MessageHandlerCoordinator] 处理消息失败: {MessageType}", message.Type);
                return new ErrorMessage
                {
                    Code = -32603,
                    Message = $"Internal error: {ex.Message}"
                };
            }
        }

        _logger?.LogWarning("[MessageHandlerCoordinator] 未找到处理器: {MessageType}", message.Type);
        return new ErrorMessage
        {
            Code = -32601,
            Message = $"Method not found: {message.Type}"
        };
    }

    /// <summary>
    /// 注册自定义处理器
    /// </summary>
    public void RegisterHandler(IMessageHandler handler)
    {
        _registry.Register(handler);
        _logger?.LogDebug("[MessageHandlerCoordinator] 注册处理器: {MessageType}", handler.MessageType);
    }
}

/// <summary>
/// 消息处理结果
/// </summary>
public sealed class MessageProcessResult
{
    public bool Success { get; init; }
    public BridgeMessage? Response { get; init; }
    public string? Error { get; init; }
    public long ProcessingTimeMs { get; init; }
}

#region Extension Handler Response Data Models

/// <summary>认证验证结果数据</summary>
public sealed class AuthVerifyResultData
{
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    [JsonPropertyName("isValid")]
    public required bool IsValid { get; init; }
}

/// <summary>会话管理结果数据</summary>
public sealed class SessionManageResultData
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>设备信任结果数据</summary>
public sealed class DeviceTrustResultData
{
    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("isTrusted")]
    public required bool IsTrusted { get; init; }
}

/// <summary>密钥验证结果数据</summary>
public sealed class SecretValidateResultData
{
    [JsonPropertyName("secretId")]
    public required string SecretId { get; init; }

    [JsonPropertyName("isValid")]
    public required bool IsValid { get; init; }
}

/// <summary>密钥轮换结果数据</summary>
public sealed class SecretRotateResultData
{
    [JsonPropertyName("newSecretId")]
    public required string NewSecretId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>对等会话管理结果数据</summary>
public sealed class PeerManageResultData
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

#endregion
