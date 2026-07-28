
namespace JoinCode.Abstractions.Prompts;

public sealed record ExternalRuleEntry
{
    public required string Name { get; init; }
    public required string Content { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public bool AlwaysApply { get; init; }
    public string Globs { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
