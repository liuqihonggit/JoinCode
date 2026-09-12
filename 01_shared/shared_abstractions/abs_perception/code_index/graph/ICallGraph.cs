namespace JoinCode.Abstractions.CodeIndex;

public interface ICallGraph
{
    Task<IReadOnlyList<CallEdge>> GetCallersAsync(string symbolName, CancellationToken ct);
    Task<IReadOnlyList<CallEdge>> GetCalleesAsync(string symbolName, CancellationToken ct);
    Task<IReadOnlyList<CallEdge>> GetCallChainAsync(string from, string to, CancellationToken ct);
    Task<IReadOnlyList<string>> GetImpactScopeAsync(string symbolName, CancellationToken ct);
}
