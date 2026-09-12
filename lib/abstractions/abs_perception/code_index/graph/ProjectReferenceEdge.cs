namespace JoinCode.Abstractions.CodeIndex;

public sealed record ProjectReferenceEdge
{
    public required string SourceProjectPath { get; init; }
    public required string TargetProjectPath { get; init; }
}
