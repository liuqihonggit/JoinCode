
namespace JoinCode.Hands.Shell;

/// <summary>
/// PowerShell 命令退出码语义解释。
/// PS 原生 cmdlet 不需要退出码语义（通过 $? 信号失败，不是退出码），
/// 但外部可执行文件（如 grep.exe、robocopy.exe）使用非零退出码传达信息而非失败。
/// 对齐 TS: src/tools/PowerShellTool/commandSemantics.ts
/// </summary>
public static class PsCommandSemantics
{
    /// <summary>
    /// 命令语义解释结果
    /// </summary>
    public sealed record CommandSemanticResult
    {
        /// <summary>
        /// 是否为错误
        /// </summary>
        public required bool IsError { get; init; }

        /// <summary>
        /// 解释消息（可选）
        /// </summary>
        public string? Message { get; init; }
    }

    /// <summary>
    /// PS 原生 cmdlet 名称（小写，不含 .exe 后缀）。
    /// 这些 cmdlet 通过终止错误 ($?) 信号失败，退出码始终为 0，不需要退出码语义解释。
    /// </summary>
    private static readonly FrozenSet<string> NativeCmdlets = FrozenSet.ToFrozenSet(
    [
        "select-string", "compare-object", "test-path",
        "get-content", "set-content", "add-content",
        "get-childitem", "get-item", "remove-item",
        "copy-item", "move-item", "new-item",
        "write-host", "write-output", "write-error",
        "get-process", "stop-process", "start-process",
        "invoke-command", "invoke-expression",
        "foreach-object", "where-object", "select-object",
        "sort-object", "group-object", "measure-object",
        "convertto-json", "convertfrom-json", "convertto-csv",
        "import-csv", "export-csv", "out-file",
        "get-member", "get-command", "get-help"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 解释命令执行结果。区分 PS 原生 cmdlet 和外部可执行文件。
    /// </summary>
    /// <param name="command">完整命令行</param>
    /// <param name="exitCode">退出码</param>
    /// <param name="stdout">标准输出</param>
    /// <param name="stderr">标准错误</param>
    /// <returns>语义解释结果</returns>
    public static CommandSemanticResult InterpretCommandResult(string command, int exitCode, string stdout, string stderr)
    {
        if (string.IsNullOrEmpty(command))
        {
            return new CommandSemanticResult { IsError = exitCode != 0, Message = exitCode != 0 ? $"Command failed with exit code {exitCode}" : null };
        }

        var baseCommand = HeuristicallyExtractBaseCommand(command);

        // PS 原生 cmdlet 不需要退出码语义
        if (NativeCmdlets.Contains(baseCommand))
        {
            return new CommandSemanticResult { IsError = exitCode != 0, Message = exitCode != 0 ? $"Command failed with exit code {exitCode}" : null };
        }

        return baseCommand switch
        {
            // grep / ripgrep / findstr: 0=匹配, 1=无匹配, 2+=错误
            "grep" or "rg" or "findstr" when exitCode >= 2 =>
                new CommandSemanticResult { IsError = true, Message = null },
            "grep" or "rg" or "findstr" when exitCode == 1 =>
                new CommandSemanticResult { IsError = false, Message = "No matches found" },
            "grep" or "rg" or "findstr" =>
                new CommandSemanticResult { IsError = false, Message = null },

            // robocopy: 0-7=成功, 8+=错误（Windows 最常见的"CI失败但实际没问题"陷阱）
            "robocopy" when exitCode >= 8 =>
                new CommandSemanticResult { IsError = true, Message = null },
            "robocopy" when exitCode == 0 =>
                new CommandSemanticResult { IsError = false, Message = "No files copied (already in sync)" },
            "robocopy" when exitCode is >= 1 and < 8 =>
                new CommandSemanticResult { IsError = false, Message = (exitCode & 1) != 0 ? "Files copied successfully" : "Robocopy completed (no errors)" },

            // 默认: 仅 0 为成功
            _ => new CommandSemanticResult { IsError = exitCode != 0, Message = exitCode != 0 ? $"Command failed with exit code {exitCode}" : null }
        };
    }

    /// <summary>
    /// 从 PowerShell 命令行启发式提取基础命令名。
    /// 取管道的最后一个段（决定退出码的段）。
    /// 启发式分割 ; 和 | — 对引号字符串或复杂构造可能不准确。
    /// 不用于安全判断，仅用于退出码解释。
    /// </summary>
    private static string HeuristicallyExtractBaseCommand(string command)
    {
        // 按 ; 和 | 分割，取最后一段
        var segments = command.Split(';', '|');
        var lastSegment = segments[^1].Trim();

        return ExtractBaseCommand(lastSegment);
    }

    /// <summary>
    /// 从单个管道段提取命令名。
    /// 去除前导 &amp; / . 调用运算符和 .exe 后缀，转小写。
    /// </summary>
    private static string ExtractBaseCommand(string segment)
    {
        var trimmed = segment.TrimStart();

        // 去除 PS 调用运算符: & "cmd", . "cmd"
        if (trimmed.Length > 1 && (trimmed[0] == '&' || trimmed[0] == '.') && char.IsWhiteSpace(trimmed[1]))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        // 取第一个 token
        var spaceIndex = trimmed.IndexOf(' ');
        var firstToken = spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;

        // 去除引号
        if (firstToken.Length >= 2 && ((firstToken[0] == '"' && firstToken[^1] == '"') || (firstToken[0] == '\'' && firstToken[^1] == '\'')))
        {
            firstToken = firstToken[1..^1];
        }

        // 去除路径: C:\bin\grep.exe → grep.exe, .\rg.exe → rg.exe
        var lastSlash = firstToken.LastIndexOfAny(['\\', '/']);
        if (lastSlash >= 0)
        {
            firstToken = firstToken[(lastSlash + 1)..];
        }

        // 去除 .exe 后缀
        if (firstToken.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            firstToken = firstToken[..^4];
        }

        return firstToken.ToLowerInvariant();
    }
}
