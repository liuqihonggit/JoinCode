namespace JoinCode.Abstractions.CodeIndex;

public interface ICallGraph {
    /// <summary>异步获取指定符号的调用方列表。</summary>
    Task<IReadOnlyList<CallEdge>> GetCallersAsync(string symbolName, CancellationToken ct);
    /// <summary>异步获取指定符号的被调用方列表。</summary>
    Task<IReadOnlyList<CallEdge>> GetCalleesAsync(string symbolName, CancellationToken ct);
    /// <summary>异步获取从源符号到目标符号的调用链。</summary>
    Task<IReadOnlyList<CallEdge>> GetCallChainAsync(string from, string to, CancellationToken ct);
    /// <summary>异步获取指定符号的影响范围。</summary>
    Task<IReadOnlyList<string>> GetImpactScopeAsync(string symbolName, CancellationToken ct);
}