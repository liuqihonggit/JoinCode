namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 参数验证上下文 — 支持通用 NullCheckValidationMiddleware 复用
/// </summary>
public interface INullCheckContext : IPipelineContext {
    /// <summary>需要验证非空的参数名和值对</summary>
    /// <remarks>中间件遍历此集合，发现 null 值则调用 Fail</remarks>
    IEnumerable<RequiredParameter> RequiredParameters { get; }
}

/// <summary>
/// 必填参数项 — 参数名与实际值，供空值检查中间件逐项校验。
/// </summary>
/// <param name="Name">参数名（通常取 <c>nameof</c>）。</param>
/// <param name="Value">参数实际值；为 null 即校验失败。</param>
public sealed record RequiredParameter(string Name, object? Value);