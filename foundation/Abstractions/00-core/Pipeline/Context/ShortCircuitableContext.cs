namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 可短路的管道上下文基类 — 提供 IsShortCircuited / ShortCircuit() 的默认实现
/// </summary>
public abstract class ShortCircuitableContext : IShortCircuitableContext
{
    public bool IsShortCircuited { get; private set; }

    public void ShortCircuit() => IsShortCircuited = true;
}
