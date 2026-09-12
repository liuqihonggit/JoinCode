
namespace Core.Skills.Mcp;

public interface IMcpSkillProvider : IAsyncDisposable
{
    Task<IReadOnlyList<SkillDefinition>> GetMcpSkillsAsync(CancellationToken cancellationToken = default);

    Task<SkillResult> ExecuteMcpSkillAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        ExecutionContext ctx,
        CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    bool IsSkillAvailable(string skillName);
}
