namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 可短路的管道上下文基类 — 提供 IsShortCircuited / ShortCircuit() 的默认实现
/// </summary>
public abstract class ShortCircuitableContext : IShortCircuitableContext {
    /// <summary>获取是否已短路。</summary>
    public bool IsShortCircuited { get; private set; }

    /// <summary>标记上下文为短路状态。</summary>
    public void ShortCircuit() => IsShortCircuited = true;
}
