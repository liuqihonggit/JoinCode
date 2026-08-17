namespace JoinCode.Tui.Commands;

/// <summary>
/// TUI 斜杠命令处理结果。
/// </summary>
/// <param name="IsHandled">是否被识别为斜杠命令。</param>
/// <param name="Output">要显示的文本（如有）。</param>
/// <param name="Action">要执行的动作。</param>
/// <param name="ShellCommand">Shell 命令内容（仅 ExecuteShell 时有值）。</param>
public sealed record TuiCommandResult(
    bool IsHandled,
    string Output,
    TuiCommandAction Action,
    string? ShellCommand)
{
    /// <summary>未处理的命令（非斜杠命令）</summary>
    public static TuiCommandResult NotHandled => new(false, string.Empty, TuiCommandAction.None, null);
}

/// <summary>
/// TUI 斜杠命令动作类型。
/// </summary>
public enum TuiCommandAction
{
    /// <summary>无动作</summary>
    None,
    /// <summary>退出 TUI</summary>
    Exit,
    /// <summary>清空输出</summary>
    ClearOutput,
    /// <summary>执行 shell 命令</summary>
    ExecuteShell,
    /// <summary>执行 build</summary>
    ExecuteBuild,
    /// <summary>执行 test</summary>
    ExecuteTest,
    /// <summary>保存会话</summary>
    SaveSession,
    /// <summary>执行 grep 搜索</summary>
    ExecuteGrep,
    /// <summary>执行 git diff</summary>
    ExecuteDiff,
    /// <summary>列出文件</summary>
    ExecuteFiles,
    /// <summary>打开/显示文件内容</summary>
    ExecuteOpen,
    /// <summary>预览 patch 文件</summary>
    ExecutePatch,
    /// <summary>应用 patch</summary>
    ExecuteApply,
    /// <summary>撤销最后修改</summary>
    ExecuteUndo,
    /// <summary>加载会话</summary>
    ExecuteLoad,
    /// <summary>显示配置</summary>
    ShowConfig,
    /// <summary>显示当前模型</summary>
    ShowModel,
    /// <summary>设置模型</summary>
    SetModel,
    /// <summary>列出已保存会话</summary>
    ListSessions,
    /// <summary>显示 token 用量</summary>
    ShowTokens,
    /// <summary>清空聊天历史</summary>
    ClearHistory,
}
