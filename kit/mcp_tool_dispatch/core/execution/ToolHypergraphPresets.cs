namespace McpToolDispatch;

/// <summary>
/// 预设工具链超边 — 基于领域知识的工具关联定义
/// 工具名称全部来自 XxxToolName 枚举的 ToValue()，零硬编码字符串
/// </summary>
public static class ToolHypergraphPresets
{
    public static ToolHyperedge[] GetPresets() =>
    [
        new()
        {
            Id = "file_ops",
            ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
                FileToolName.FileRead.ToValue(),
                FileToolName.FileWrite.ToValue(),
                FileToolName.FileEdit.ToValue(),
                FileToolName.FileDelete.ToValue()),
            Weight = 0.6,
            ChainOrder = [FileToolName.FileRead.ToValue(), FileToolName.FileEdit.ToValue(), FileToolName.FileWrite.ToValue()]
        },
        new()
        {
            Id = "shell_exec",
            ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
                ShellToolName.Bash.ToValue(),
                ShellToolName.Powershell.ToValue()),
            Weight = 0.4,
            ChainOrder = null
        },
        new()
        {
            Id = "search_chain",
            ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
                SearchToolName.SearchCode.ToValue(),
                SearchToolName.SearchFiles.ToValue(),
                SearchToolName.Search.ToValue()),
            Weight = 0.5,
            ChainOrder = [SearchToolName.SearchCode.ToValue(), SearchToolName.SearchFiles.ToValue(), SearchToolName.Search.ToValue()]
        },
        new()
        {
            Id = "git_chain",
            ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
                GitToolName.GitStatus.ToValue(),
                GitToolName.GitAdd.ToValue(),
                GitToolName.GitCommit.ToValue(),
                GitToolName.GitPush.ToValue(),
                GitToolName.GitDiff.ToValue()),
            Weight = 0.6,
            ChainOrder = [GitToolName.GitStatus.ToValue(), GitToolName.GitDiff.ToValue(), GitToolName.GitAdd.ToValue(), GitToolName.GitCommit.ToValue(), GitToolName.GitPush.ToValue()]
        },
        new()
        {
            Id = "code_chain",
            ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
                CodeToolName.AnalyzeCsharpCode.ToValue(),
                CodeToolName.GenerateCsharpCode.ToValue(),
                CodeToolName.ExecuteCsharpCode.ToValue()),
            Weight = 0.5,
            ChainOrder = [CodeToolName.AnalyzeCsharpCode.ToValue(), CodeToolName.GenerateCsharpCode.ToValue(), CodeToolName.ExecuteCsharpCode.ToValue()]
        },
    ];
}
