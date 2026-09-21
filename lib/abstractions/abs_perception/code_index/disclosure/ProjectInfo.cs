namespace JoinCode.Abstractions.CodeIndex;

public sealed record ProjectInfo {
    /// <summary>获取项目名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取项目文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取目标框架。</summary>
    public string? TargetFramework { get; init; }
    /// <summary>获取输出类型。</summary>
    public string? OutputType { get; init; }
    /// <summary>获取项目 GUID。</summary>
    public string? ProjectGuid { get; init; }
}
