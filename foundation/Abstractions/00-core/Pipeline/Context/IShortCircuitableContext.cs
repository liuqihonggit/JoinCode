namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 可短路的管道上下文 — 中间件调用 ShortCircuit() 后，管道自动跳过后续中间件
/// </summary>
public interface IShortCircuitableContext
{
    /// <summary>
    /// 是否已短路 — 管道在每个中间件执行前检查此属性
    /// </summary>
    bool IsShortCircuited { get; }

    /// <summary>
    /// 标记短路 — 调用后管道将跳过所有后续中间件
    /// </summary>
    void ShortCircuit();
}
