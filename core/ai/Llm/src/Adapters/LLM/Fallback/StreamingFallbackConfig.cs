namespace Api.LLM.Fallback;

/// <summary>
/// 流式→非流式 fallback 配置 — 对齐 TS claude.ts 的 fallback 机制
/// 当流式请求失败（529过载/超时/不完整流/看门狗超时）时，自动降级为非流式请求
/// </summary>
public sealed class StreamingFallbackConfig
{
    /// <summary>
    /// 是否启用流式→非流式 fallback（默认 true）
    /// 对齐 TS: CLAUDE_CODE_DISABLE_NONSTREAMING_FALLBACK 环境变量
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 流式空闲看门狗超时（毫秒）— 超过此时间无 chunk 到达则触发 fallback
    /// 对齐 TS: CLUDE_STREAM_IDLE_TIMEOUT_MS，默认 90_000
    /// </summary>
    public int StreamIdleTimeoutMs { get; set; } = 90_000;

    /// <summary>
    /// 是否启用流式空闲看门狗（默认 true）
    /// 对齐 TS: CLAUDE_ENABLE_STREAM_WATCHDOG
    /// </summary>
    public bool StreamWatchdogEnabled { get; set; } = true;

    /// <summary>
    /// 非流式 fallback 请求超时（毫秒）
    /// 对齐 TS: getNonstreamingFallbackTimeoutMs — 远程 120s，本地 300s
    /// </summary>
    public int NonStreamingTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// 非流式请求 max_tokens 上限 — API 有 10min 非流式限制
    /// 对齐 TS: MAX_NON_STREAMING_TOKENS = 64_000
    /// </summary>
    public int MaxNonStreamingTokens { get; set; } = 64_000;

    /// <summary>
    /// 触发 fallback 的 HTTP 状态码集合（默认 529 过载 + 503 服务不可用）
    /// 对齐 TS: is529Error + withRetry 中的 529 处理
    /// </summary>
    public HashSet<int> FallbackStatusCodes { get; set; } = [529, 503, 502];

    /// <summary>
    /// 流式失败后 fallback 前的最大重试次数（同供应商同模型重试，默认 0 — 直接 fallback）
    /// 对齐 TS: 流式失败直接 fallback，不重试流式
    /// </summary>
    public int MaxStreamingRetriesBeforeFallback { get; set; } = 0;

    /// <summary>
    /// 从环境变量创建配置 — 对齐 TS 环境变量命名
    /// JCC_DISABLE_STREAMING_FALLBACK=1 禁用 fallback
    /// JCC_STREAM_IDLE_TIMEOUT_MS=90000 看门狗超时
    /// JCC_ENABLE_STREAM_WATCHDOG=0 禁用看门狗
    /// JCC_NON_STREAMING_TIMEOUT_MS=300000 非流式超时
    /// </summary>
    public static StreamingFallbackConfig FromEnvironment()
    {
        var config = new StreamingFallbackConfig();

        var disableFallback = Environment.GetEnvironmentVariable("JCC_DISABLE_STREAMING_FALLBACK");
        if (disableFallback is "1" or "true" or "yes")
            config.Enabled = false;

        var idleTimeout = Environment.GetEnvironmentVariable("JCC_STREAM_IDLE_TIMEOUT_MS");
        if (int.TryParse(idleTimeout, out var timeoutMs) && timeoutMs > 0)
            config.StreamIdleTimeoutMs = timeoutMs;

        var enableWatchdog = Environment.GetEnvironmentVariable("JCC_ENABLE_STREAM_WATCHDOG");
        if (enableWatchdog is "0" or "false" or "no")
            config.StreamWatchdogEnabled = false;

        var nonStreamingTimeout = Environment.GetEnvironmentVariable("JCC_NON_STREAMING_TIMEOUT_MS");
        if (int.TryParse(nonStreamingTimeout, out var nsTimeoutMs) && nsTimeoutMs > 0)
            config.NonStreamingTimeoutMs = nsTimeoutMs;

        return config;
    }
}
