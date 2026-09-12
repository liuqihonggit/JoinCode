namespace McpProtocol;

public interface IPromptHandler
{
    string Name { get; }
    string? Description { get; }
    List<McpPromptArgument>? Arguments { get; }
    Task<McpPromptMessage> GetAsync(Dictionary<string, string>? arguments = null, CancellationToken cancellationToken = default);
}
