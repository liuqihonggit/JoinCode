namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ToolSpec
{
    public string Name { get; }
    public string? Description { get; }
    public string? InputSchemaJson { get; }

    public ToolSpec(string name, string? description = null, string? inputSchemaJson = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        InputSchemaJson = inputSchemaJson;
    }
}
