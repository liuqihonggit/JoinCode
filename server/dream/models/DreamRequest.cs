namespace JoinCode.Dream;

/// <summary>
/// 做梦执行请求
/// </summary>
/// <param name="Force">是否强制触发（忽略门控检查）</param>
/// <param name="SessionIds">指定要处理的会话ID列表（null表示自动扫描）</param>
public sealed record DreamRequest(
    bool Force = false,
    IReadOnlyList<string>? SessionIds = null);
