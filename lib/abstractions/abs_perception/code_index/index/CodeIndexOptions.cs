namespace JoinCode.Abstractions.CodeIndex;

[Register(typeof(CodeIndexOptions), ServiceLifetime.Singleton)]
public sealed record CodeIndexOptions {
    /// <summary>获取工作区根路径。</summary>
    public string WorkspaceRoot { get; init; } = Environment.CurrentDirectory;
    /// <summary>获取是否启用 L1 索引。</summary>
    public bool EnableL1 { get; init; } = true;
    /// <summary>获取是否启用 L2 索引。</summary>
    public bool EnableL2 { get; init; } = true;
    /// <summary>获取最大内存占用（MB）。</summary>
    public int MaxMemoryMB { get; init; } = 600;
    /// <summary>获取文件匹配模式列表。</summary>
    public IEnumerable<string> FilePatterns { get; init; } = new[] { "*.cs" };
    /// <summary>获取排除模式列表。</summary>
    public IEnumerable<string> ExcludePatterns { get; init; } = new[] { "bin/", "obj/", ".git/", ".x/" };
}