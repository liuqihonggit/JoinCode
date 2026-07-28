
namespace Core.Bridge;

using Core.Bridge.Gate;
using Core.Bridge.Init;
using Infrastructure.Pipeline;

/// <summary>
/// REPL 桥初始化选项 — 对齐 TS 端 initReplBridge.ts InitBridgeOptions
/// </summary>
public sealed class BridgeInitOptions
{
    /// <summary>入站消息回调</summary>
    public Action<string>? OnInboundMessage { get; init; }

    /// <summary>权限响应回调</summary>
    public Action<JsonElement>? OnPermissionResponse { get; init; }

    /// <summary>中断回调</summary>
    public Action? OnInterrupt { get; init; }

    /// <summary>设置模型回调</summary>
    public Action<string?>? OnSetModel { get; init; }

    /// <summary>设置最大思考令牌数回调</summary>
    public Action<int?>? OnSetMaxThinkingTokens { get; init; }

    /// <summary>设置权限模式回调</summary>
    public Func<string, OperationResult>? OnSetPermissionMode { get; init; }

    /// <summary>状态变更回调 — 对齐 TS 端 onStateChange(state, detail?)</summary>
    public Action<BridgeState, string?>? OnStateChange { get; init; }

    /// <summary>初始消息</summary>
    public string[]? InitialMessages { get; init; }

    /// <summary>显式会话名称 — 对齐 TS 端 initialName</summary>
    public string? InitialName { get; init; }

    /// <summary>获取当前消息列表 — 对齐 TS 端 getMessages</summary>
    public Func<string[]>? GetMessages { get; init; }

    /// <summary>是否持久模式 — 对齐 TS 端 perpetual</summary>
    public bool Perpetual { get; init; }

    /// <summary>是否仅出站模式</summary>
    public bool OutboundOnly { get; init; }

    /// <summary>标签</summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// 检查组织策略是否允许 — 对齐 TS 端 isPolicyAllowed('allow_remote_control')
    /// 返回 true 表示允许，false 表示被组织策略禁止
    /// 默认 fail-open: null 视为允许
    /// </summary>
    public Func<string, bool>? IsPolicyAllowed { get; init; }

    /// <summary>
    /// 获取受信设备令牌 — 对齐 TS 端 getTrustedDeviceToken()
    /// v1 路径: 注入 BridgeApiDeps，每次 HTTP 请求时延迟调用
    /// v2 路径: 在 fetchRemoteCredentials 调用时立即求值
    /// </summary>
    public Func<Task<string?>>? GetTrustedDeviceToken { get; init; }

    /// <summary>
    /// 获取 OAuth 令牌过期时间 — 对齐 TS 端 getClaudeAIOAuthTokens()?.expiresAt
    /// 返回 null 表示无过期时间（env-var/FD 令牌），DateTimeOffset 为实际过期时间
    /// 用于跨进程退避(2a)和过期令牌跳过(2c)
    /// </summary>
    public Func<DateTimeOffset?>? GetOAuthTokenExpiry { get; init; }

    /// <summary>
    /// 主动刷新 OAuth 令牌 — 对齐 TS 端 checkAndRefreshOAuthTokenIfNeeded()
    /// 在 bridge 初始化前调用，确保令牌未过期
    /// 返回 true 表示刷新成功或无需刷新，false 表示刷新失败
    /// null 表示未提供回调，跳过此步骤
    /// </summary>
    public Func<Task<bool>>? CheckAndRefreshOAuthToken { get; init; }

    /// <summary>
    /// 获取/设置跨进程死令牌退避状态 — 对齐 TS 端 globalConfig.bridgeOauthDeadExpiresAt/FailCount
    /// 用于跨进程退避(2a)和过期令牌跳过(2c)的持久化
    /// null 表示不使用跨进程退避
    /// </summary>
    public IBridgeOAuthDeadTokenState? DeadTokenState { get; init; }
}

/// <summary>
/// REPL 桥初始化入口 — 对齐 TS 端 initReplBridge.ts
/// 核心流程:
/// 1. 检查 bridge 是否启用
/// 2. 检查 OAuth 令牌
/// 2a. 跨进程退避 (死令牌检测)
/// 2b. 主动刷新 OAuth 令牌
/// 2c. 过期令牌跳过 (刷新后仍过期)
/// 3. 检查组织策略 (allow_remote_control)
/// 4. 检查组织 UUID
/// 5. 派生会话标题
/// 6. 选择 v2 (env-less) 路径
/// </summary>
public static class BridgeInit
{
    private const int TitleMaxLen = 50;

    /// <summary>
    /// 初始化 REPL 桥 — 对齐 TS 端 initReplBridge
    /// </summary>
    /// <param name="options">初始化选项</param>
    /// <param name="bridgeEnabled">bridge 是否启用</param>
    /// <param name="getAccessToken">获取 OAuth 令牌</param>
    /// <param name="getOrgUUID">获取组织 UUID</param>
    /// <param name="getBaseUrl">获取 API 基础 URL</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="transportFactory">传输层工厂</param>
    /// <param name="logger">日志</param>
    /// <param name="v1Pipeline">V1 初始化管道（DI 注入）</param>
    /// <param name="v2Pipeline">V2 初始化管道（DI 注入）</param>
    /// <param name="clock">时钟服务（可选，默认使用系统时钟）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>桥句柄，失败返回 null</returns>
    public static async Task<IReplBridgeHandle?> InitReplBridgeAsync(
        BridgeInitOptions options,
        bool bridgeEnabled,
        Func<string?> getAccessToken,
        Func<string?> getOrgUUID,
        Func<string> getBaseUrl,
        IFileSystem fs,
        HttpClient? httpClient = null,
        IReplBridgeTransportFactory? transportFactory = null,
        ILogger? logger = null,
        MiddlewarePipeline<V1BridgeInitContext>? v1Pipeline = null,
        MiddlewarePipeline<V2BridgeInitContext>? v2Pipeline = null,
        IClockService? clock = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var gatePipeline = new PipelineBuilder<BridgeInitGateContext>()
            .WithShortCircuit(ctx => ctx.Failed)
            .Use(new BridgeGateEnabledMiddleware())
            .Use(new BridgeGateOAuthMiddleware())
            .Use(new BridgeGateDeadTokenBackoffMiddleware())
            .Use(new BridgeGateTokenRefreshMiddleware())
            .Use(new BridgeGateExpiredTokenMiddleware())
            .Use(new BridgeGatePolicyMiddleware())
            .Use(new BridgeGateOrgUUIDMiddleware())
            .Use(new BridgeGateCoreDispatchMiddleware())
            .Build();

        var gateCtx = new BridgeInitGateContext
        {
            Options = options,
            BridgeEnabled = bridgeEnabled,
            GetAccessToken = getAccessToken,
            GetOrgUUID = getOrgUUID,
            GetBaseUrl = getBaseUrl,
            FileSystem = fs,
            HttpClient = httpClient,
            TransportFactory = transportFactory,
            Logger = logger,
            V1Pipeline = v1Pipeline,
            V2Pipeline = v2Pipeline,
            Clock = clock,
            CancellationToken = ct,
        };

        await gatePipeline.ExecuteAsync(gateCtx, ct).ConfigureAwait(false);
        return gateCtx.Handle;
    }

    /// <summary>
    /// 从原始文本派生占位标题 — 对齐 TS 端 deriveTitle
    /// 去标签、取首句、折叠空白、截断50字符
    /// </summary>
    public static string? DeriveTitle(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        // 去除标签（XML 标签）
        var text = raw.Trim();

        // 取第一句 — 对齐 TS 端: /^(.*?[.!?])\s/.exec(clean)?.[1] ?? clean
        var firstSentence = text.AsSpan();
        for (var i = 0; i < firstSentence.Length; i++)
        {
            var c = firstSentence[i];
            if ((c == '.' || c == '!' || c == '?') && i + 1 < firstSentence.Length && char.IsWhiteSpace(firstSentence[i + 1]))
            {
                firstSentence = firstSentence[..(i + 1)];
                break;
            }
        }

        // 折叠空白 — 对齐 TS 端: replace(/\s+/g, ' ')
        var flat = string.Create(firstSentence.Length, firstSentence, (span, src) =>
        {
            var j = 0;
            var prevWasSpace = false;
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!prevWasSpace)
                    {
                        span[j++] = ' ';
                        prevWasSpace = true;
                    }
                }
                else
                {
                    span[j++] = c;
                    prevWasSpace = false;
                }
            }
            // 截断到实际长度
            for (var k = j; k < span.Length; k++) span[k] = '\0';
        }).TrimEnd('\0');

        flat = flat.Trim();
        if (string.IsNullOrEmpty(flat)) return null;

        // 截断到 50 字符 — 对齐 TS 端: flat.length > TITLE_MAX_LEN ? flat.slice(0, -1) + '\u2026'
        if (flat.Length > TitleMaxLen)
        {
            return string.Concat(flat.AsSpan(0, TitleMaxLen - 1), "\u2026");
        }

        return flat;
    }

    /// <summary>
    /// 解析 sessionIngressUrl — 对齐 TS 端: USER_TYPE=ant + CLAUDE_BRIDGE_SESSION_INGRESS_URL
    /// 生产环境下与 baseUrl 相同，ant 开发环境可独立配置
    /// </summary>
    internal static string ResolveSessionIngressUrl(string baseUrl)
    {
        var userType = Environment.GetEnvironmentVariable("USER_TYPE");
        var ingressOverride = Environment.GetEnvironmentVariable("CLAUDE_BRIDGE_SESSION_INGRESS_URL");
        if (string.Equals(userType, "ant", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ingressOverride))
        {
            return ingressOverride;
        }
        return baseUrl;
    }

    /// <summary>
    /// 派生会话标题 — 对齐 TS 端标题优先级:
    /// initialName → 消息推导 → 默认 slug
    /// </summary>
    internal static string DeriveSessionTitle(BridgeInitOptions options)
    {
        // 优先级 1: 显式名称 — 对齐 TS 端: if (initialName)
        if (!string.IsNullOrEmpty(options.InitialName))
        {
            return options.InitialName ?? throw new InvalidOperationException("InitialName should not be null after null check");
        }

        // 优先级 2: 从初始消息推导 — 对齐 TS 端: if (initialMessages && initialMessages.length > 0)
        if (options.InitialMessages is { Length: > 0 })
        {
            for (var i = options.InitialMessages.Length - 1; i >= 0; i--)
            {
                var derived = DeriveTitle(options.InitialMessages[i]);
                if (derived is not null)
                {
                    return derived;
                }
            }
        }

        // 优先级 3: 默认 slug — 对齐 TS 端: `remote-control-${generateShortWordSlug()}`
        return $"remote-control-{Environment.MachineName}";
    }

    /// <summary>
    /// 创建 onUserMessage 回调 — 对齐 TS 端 onUserMessage 逻辑
    /// 在 count-1 和 count-3 时派生标题
    /// </summary>
    internal static Func<string, string, bool>? CreateOnUserMessage(
        BridgeInitOptions options, string baseUrl, Func<string?> getAccessToken)
    {
        // 如果有显式名称，直接返回 done
        if (!string.IsNullOrEmpty(options.InitialName))
        {
            return (_, _) => true;
        }

        var userMessageCount = 0;
        var hasTitle = false;

        return (text, sessionId) =>
        {
            userMessageCount++;

            // count-1: 立即派生占位标题
            if (userMessageCount == 1 && !hasTitle)
            {
                var placeholder = DeriveTitle(text);
                if (placeholder is not null)
                {
                    hasTitle = true;
                }
            }

            // count-3: 标记完成 — 对齐 TS 端: return userMessageCount >= 3
            return userMessageCount >= 3;
        };
    }

    /// <summary>
    /// 通过 API 创建会话 — 对齐 TS 端 createSession 回调
    /// 使用 BridgeCodeSessionApi 创建 code session 并返回 sessionId
    /// </summary>
    internal static async Task<string?> CreateSessionViaApiAsync(
        string baseUrl, string? accessToken, string environmentId,
        string title, HttpClient httpClient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken)) return null;

        return await BridgeCodeSessionApi.CreateCodeSessionAsync(
            baseUrl, accessToken, title, 30000, httpClient, ct: ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 跨进程死令牌退避状态 — 对齐 TS 端 globalConfig.bridgeOauthDeadExpiresAt/FailCount
/// 内容寻址键: 使用 expiresAt 标识死令牌，/login → 新令牌 → 新 expiresAt → 退避自动失效
/// </summary>
public interface IBridgeOAuthDeadTokenState
{
    /// <summary>死令牌的过期时间（内容寻址键）</summary>
    DateTimeOffset? DeadExpiresAt { get; }

    /// <summary>同一死令牌的连续失败次数</summary>
    int DeadFailCount { get; }

    /// <summary>记录死令牌 — 对齐 TS 端 saveGlobalConfig({ bridgeOauthDeadExpiresAt, bridgeOauthDeadFailCount })</summary>
    /// <param name="expiresAt">死令牌的过期时间</param>
    Task RecordDeadTokenAsync(DateTimeOffset expiresAt);
}
