namespace JoinCode.Tui.Commands;

/// <summary>
/// TUI 斜杠命令处理器 — 解析 / 开头命令并返回动作。
/// 纯函数，无副作用，可单元测试。
/// 支持命令: /help /exit /clear /history /shell /build /test /save
/// </summary>
public static class TuiCommandProcessor
{
    private static readonly FrozenSet<string> KnownCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "/help", "/exit", "/clear", "/history", "/shell", "/build", "/test", "/save",
        "/grep", "/diff", "/files", "/open", "/patch", "/apply", "/undo");

    /// <summary>
    /// 解析并处理斜杠命令。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <param name="history">聊天历史（/history /save 需要）。</param>
    /// <returns>命令处理结果。</returns>
    public static TuiCommandResult Process(string input, MessageList? history = null)
    {
        if (string.IsNullOrWhiteSpace(input) || input[0] != '/')
            return TuiCommandResult.NotHandled;

        var spaceIndex = input.IndexOf(' ');
        var command = spaceIndex < 0 ? input : input[..spaceIndex];
        var args = spaceIndex < 0 ? string.Empty : input[(spaceIndex + 1)..].TrimStart();

        if (!KnownCommands.Contains(command))
            return new TuiCommandResult(true, $"  ❌ 未知命令: {command}（输入 /help 查看可用命令）", TuiCommandAction.None, null);

        return command.ToLowerInvariant() switch
        {
            "/help" => new TuiCommandResult(true, BuildHelpText(), TuiCommandAction.None, null),
            "/exit" => new TuiCommandResult(true, "  👋 再见", TuiCommandAction.Exit, null),
            "/clear" => new TuiCommandResult(true, string.Empty, TuiCommandAction.ClearOutput, null),
            "/history" => new TuiCommandResult(true, BuildHistoryText(history), TuiCommandAction.None, null),
            "/shell" => HandleShell(args),
            "/build" => new TuiCommandResult(true, "  🔨 执行 build...", TuiCommandAction.ExecuteBuild, null),
            "/test" => new TuiCommandResult(true, "  🧪 执行 test...", TuiCommandAction.ExecuteTest, null),
            "/save" => HandleSave(history),
            "/grep" => HandleGrep(args),
            "/diff" => new TuiCommandResult(true, "  📝 git diff...", TuiCommandAction.ExecuteDiff, null),
            "/files" => HandleFiles(args),
            "/open" => HandleOpen(args),
            "/patch" => HandlePatch(args),
            "/apply" => HandleApply(args),
            "/undo" => new TuiCommandResult(true, "  ↩️ 撤销最后修改...", TuiCommandAction.ExecuteUndo, null),
            _ => new TuiCommandResult(true, $"  ❌ 未知命令: {command}", TuiCommandAction.None, null),
        };
    }

    private static string BuildHelpText()
    {
        return """
              📖 可用命令:
              /help     — 显示此帮助
              /exit     — 退出 TUI
              /clear    — 清空输出
              /history  — 显示聊天历史
              /shell    — 执行 shell 命令（如 /shell dir）
              /build    — 执行 dotnet build
              /test     — 执行 dotnet test
              /save     — 保存当前会话到文件
              /grep     — 搜索代码（如 /grep TODO）
              /diff     — 显示 git diff
              /files    — 列出文件（如 /files *.cs）
              /open     — 显示文件内容（如 /open README.md）
              /patch    — 预览 patch 文件（如 /patch fix.patch）
              /apply    — 应用 patch（如 /apply fix.patch）
              /undo     — 撤销最后修改（git checkout .）
            """;
    }

    private static string BuildHistoryText(MessageList? history)
    {
        if (history is null || history.Count == 0)
            return "  📜 无历史记录";

        var sb = new StringBuilder("  📜 聊天历史:\n");
        for (var i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            var role = msg.Role switch
            {
                MessageRole.User => "👤",
                MessageRole.Assistant => "🤖",
                MessageRole.System => "⚙️",
                _ => "🔧",
            };
            var content = msg.Content ?? string.Empty;
            if (content.Length > 100)
                content = string.Concat(content.AsSpan(0, 97), "...");
            sb.Append($"  {role} [{i + 1}] {content}\n");
        }
        return sb.ToString();
    }

    private static TuiCommandResult HandleShell(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new TuiCommandResult(true, "  用法: /shell <命令>（如 /shell dir）", TuiCommandAction.None, null);
        return new TuiCommandResult(true, $"  🔧 执行: {args}", TuiCommandAction.ExecuteShell, args);
    }

    private static TuiCommandResult HandleSave(MessageList? history)
    {
        if (history is null || history.Count == 0)
            return new TuiCommandResult(true, "  ⚠️ 无历史记录可保存", TuiCommandAction.None, null);
        return new TuiCommandResult(true, "  💾 保存会话...", TuiCommandAction.SaveSession, null);
    }

    private static TuiCommandResult HandleGrep(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new TuiCommandResult(true, "  用法: /grep <模式> [路径]（如 /grep TODO）", TuiCommandAction.None, null);
        return new TuiCommandResult(true, $"  🔍 grep: {args}", TuiCommandAction.ExecuteGrep, args);
    }

    private static TuiCommandResult HandleFiles(string args)
    {
        var pattern = string.IsNullOrWhiteSpace(args) ? "*" : args;
        return new TuiCommandResult(true, $"  📂 files: {pattern}", TuiCommandAction.ExecuteFiles, pattern);
    }

    private static TuiCommandResult HandleOpen(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new TuiCommandResult(true, "  用法: /open <文件路径>（如 /open README.md）", TuiCommandAction.None, null);
        return new TuiCommandResult(true, $"  📄 open: {args}", TuiCommandAction.ExecuteOpen, args);
    }

    private static TuiCommandResult HandlePatch(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new TuiCommandResult(true, "  用法: /patch <patch文件>（如 /patch fix.patch）", TuiCommandAction.None, null);
        return new TuiCommandResult(true, $"  📋 patch 预览: {args}", TuiCommandAction.ExecutePatch, args);
    }

    private static TuiCommandResult HandleApply(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new TuiCommandResult(true, "  用法: /apply <patch文件>（如 /apply fix.patch）", TuiCommandAction.None, null);
        return new TuiCommandResult(true, $"  ✅ apply: {args}", TuiCommandAction.ExecuteApply, args);
    }
}
