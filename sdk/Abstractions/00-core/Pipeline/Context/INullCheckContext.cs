namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 参数验证上下文 — 支持通用 NullCheckValidationMiddleware 复用
/// </summary>
public interface INullCheckContext : IPipelineContext
{
    /// <summary>需要验证非空的参数名和值对</summary>
    /// <remarks>中间件遍历此集合，发现 null 值则调用 Fail</remarks>
    IEnumerable<(string Name, object? Value)> RequiredParameters { get; }
}
