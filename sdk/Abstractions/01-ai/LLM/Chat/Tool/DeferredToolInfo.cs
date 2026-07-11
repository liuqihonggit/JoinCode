namespace JoinCode.Abstractions.LLM.Chat;

public sealed class DeferredToolInfo
{
    public string Name { get; }
    public string? Description { get; }
    public string? InputSchemaJson { get; }
    public bool IsMcp { get; }

    public DeferredToolInfo(string name, string? description = null, string? inputSchemaJson = null, bool isMcp = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        InputSchemaJson = inputSchemaJson;
        IsMcp = isMcp;
    }
}
