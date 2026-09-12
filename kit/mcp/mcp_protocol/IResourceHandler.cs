namespace McpProtocol;

public interface IResourceHandler
{
    string Uri { get; }
    string Name { get; }
    string? Description { get; }
    string? MimeType { get; }
    Task<McpResourceContent> ReadAsync(CancellationToken cancellationToken = default);
}
