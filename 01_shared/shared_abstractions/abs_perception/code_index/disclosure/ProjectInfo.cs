namespace JoinCode.Abstractions.CodeIndex;

public sealed record ProjectInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public string? TargetFramework { get; init; }
    public string? OutputType { get; init; }
    public string? ProjectGuid { get; init; }
}
