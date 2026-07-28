namespace McpProtocol;

public interface IToolHandler
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    Task<object> ExecuteAsync(Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default);
}
