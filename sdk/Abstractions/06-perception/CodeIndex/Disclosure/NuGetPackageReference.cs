namespace JoinCode.Abstractions.CodeIndex;

public sealed record NuGetPackageReference
{
    public required string ProjectPath { get; init; }
    public required string PackageName { get; init; }
    public string? Version { get; init; }
}
