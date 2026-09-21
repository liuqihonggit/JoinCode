namespace JoinCode.Abstractions.CodeIndex;

public sealed record ProjectReferenceEdge {
    /// <summary>获取源项目路径。</summary>
    public required string SourceProjectPath { get; init; }
    /// <summary>获取目标项目路径。</summary>
    public required string TargetProjectPath { get; init; }
}