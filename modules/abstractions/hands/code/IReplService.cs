namespace JoinCode.Abstractions.Interfaces;

public sealed class ReplResult {
    /// <summary>获取一个值，指示执行是否成功。</summary>
    public required bool Success { get; init; }
    /// <summary>获取输出内容。</summary>
    public required string Output { get; init; }
    /// <summary>获取语言名称。</summary>
    public required string Language { get; init; }
    /// <summary>获取执行时长。</summary>
    public required TimeSpan ExecutionTime { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
}

public sealed class ReplLanguageInfo {
    /// <summary>获取语言名称。</summary>
    public required string Language { get; init; }
    /// <summary>获取显示名称。</summary>
    public required string DisplayName { get; init; }
    /// <summary>获取可执行文件路径。</summary>
    public required string Executable { get; init; }
    /// <summary>获取一个值，指示该语言是否可用。</summary>
    public required bool IsAvailable { get; init; }
    /// <summary>获取安装提示。</summary>
    public string? InstallHint { get; init; }
}

public interface IReplService {
    /// <summary>获取一个值，指示 REPL 模式是否启用。</summary>
    bool IsReplModeEnabled { get; }
    /// <summary>启用 REPL 模式。</summary>
    void EnableReplMode();
    /// <summary>禁用 REPL 模式。</summary>
    void DisableReplMode();
    /// <summary>异步执行代码并返回结果。</summary>
    Task<ReplResult> ExecuteAsync(string code, string language = ReplLanguageEnumConstants.CSharp, int timeoutSeconds = 30, CancellationToken ct = default);
    /// <summary>获取 REPL 模式下隐藏的工具列表。</summary>
    IReadOnlyList<string> GetHiddenTools();
    /// <summary>获取可用的 REPL 语言列表。</summary>
    IReadOnlyList<ReplLanguageInfo> GetAvailableLanguages();
}