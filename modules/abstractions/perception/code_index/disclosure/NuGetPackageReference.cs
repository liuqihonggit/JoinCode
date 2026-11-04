namespace JoinCode.Abstractions.CodeIndex;

public sealed record NuGetPackageReference {
    /// <summary>获取项目路径。</summary>
    public required string ProjectPath { get; init; }
    /// <summary>获取包名称。</summary>
    public required string PackageName { get; init; }
    /// <summary>获取版本号。</summary>
    public string? Version { get; init; }
}