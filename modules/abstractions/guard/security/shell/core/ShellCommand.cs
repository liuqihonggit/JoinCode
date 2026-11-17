namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Shell 命令封装 - 将解析逻辑从总线移到类型内部
/// </summary>
public sealed record ShellCommand {
    /// <summary>
    /// 原始命令字符串
    /// </summary>
    public string RawCommand { get; }

    /// <summary>
    /// 命令名称（第一个词）
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// 命令参数
    /// </summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// 命令中引用的路径
    /// </summary>
    public IReadOnlyList<string> ReferencedPaths { get; }

    /// <summary>
    /// 是否为 PowerShell 命令
    /// </summary>
    public bool IsPowerShell { get; }

    /// <summary>
    /// 是否为 Bash 命令
    /// </summary>
    public bool IsBash { get; }

    /// <summary>
    /// 是否包含管道
    /// </summary>
    public bool HasPipe { get; }

    /// <summary>
    /// 是否包含重定向
    /// </summary>
    public bool HasRedirection { get; }

    private ShellCommand(
        string rawCommand,
        string commandName,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> referencedPaths,
        bool isPowerShell,
        bool isBash,
        bool hasPipe,
        bool hasRedirection) {
        RawCommand = rawCommand;
        CommandName = commandName;
        Arguments = arguments;
        ReferencedPaths = referencedPaths;
        IsPowerShell = isPowerShell;
        IsBash = isBash;
        HasPipe = hasPipe;
        HasRedirection = hasRedirection;
    }

    /// <summary>
    /// 解析命令字符串
    /// </summary>
    public static ShellCommand Parse(string command) {
        if (string.IsNullOrWhiteSpace(command)) {
            throw new ArgumentException("Command cannot be null or empty", nameof(command));
        }

        var raw = command.Trim();
        var isPowerShell = DetectPowerShell(raw);
        var isBash = !isPowerShell && DetectBash(raw);
        var hasPipe = raw.Contains('|');
        var hasRedirection = raw.Contains('>') || raw.Contains('<');

        var (commandName, arguments) = ParseCommandParts(raw);
        var referencedPaths = ExtractPaths(arguments);

        return new ShellCommand(
            raw,
            commandName,
            arguments,
            referencedPaths,
            isPowerShell,
            isBash,
            hasPipe,
            hasRedirection);
    }

    /// <summary>
    /// 按 shell 子命令分隔符拆分命令：分号、<c>&amp;&amp;</c>、<c>||</c>、换行。
    /// <para>
    /// 引号内、反引号内、<c>$(...)</c> 内的分隔符不拆 —— 它们属于同一个词。
    /// <b>单管道不拆</b>：管道两侧是同一条命令的数据流，拆成两条独立命令会丢掉「右侧消费左侧输出」
    /// 这个语义。管道右侧要单独判定时用 <see cref="SplitPipelineStages"/>。
    /// 含 heredoc（<c>&lt;&lt;</c>）时也不把换行当分隔符：heredoc 正文里出现的 <c>rm -rf</c>
    /// 只是被写入文件的内容，不是被执行的命令，按行硬拆会把「写脚本」误判成危险操作。
    /// </para>
    /// <para>
    /// 返回的每段已 Trim，空段被丢弃；无分隔符时返回单元素（即原命令）。
    /// 供需要「逐子命令判定再取最严」的调用方使用（如危险分级）。
    /// </para>
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    public static IReadOnlyList<string> SplitSubcommands(string command)
        => Scan(command, splitPipes: false);

    /// <summary>
    /// 按 shell 分隔符拆分到**最小执行单元**：在 <see cref="SplitSubcommands"/> 的基础上，
    /// 把单管道两侧也拆开（<c>a | b | c</c> → <c>a</c> / <c>b</c> / <c>c</c>）。
    /// <para>
    /// 存在理由：管道左侧只负责产出数据，真正动手的是右侧。而按首词查表的分类逻辑只看得到左侧，
    /// <c>echo hi | xargs rm -rf</c> 的首词是 <c>echo</c>，于是「给危险命令加一个良性前缀」
    /// 就能让拦截失效 —— 与 <c>echo hi; rm -rf ./build</c> 同一类降级，只是分隔符不同。
    /// </para>
    /// <para>
    /// 引号 / 反引号 / <c>$(...)</c> / heredoc 的取舍与 <see cref="SplitSubcommands"/> 完全一致。
    /// 返回的第一段是首条命令，其后每段都是某个管道的右侧。
    /// </para>
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    public static IReadOnlyList<string> SplitPipelineStages(string command)
        => Scan(command, splitPipes: true);

    private static IReadOnlyList<string> Scan(string command, bool splitPipes) {
        if (string.IsNullOrWhiteSpace(command)) {
            return Array.Empty<string>();
        }

        var allowsNewlineSplit = command.IndexOf("<<", StringComparison.Ordinal) < 0;
        // heredoc 正文总在换行之后开始：正文里的 `rm -rf` / `| sh` 都是被写入文件的内容，
        // 不是被执行的命令。所以含 heredoc 时，起始行之后不再按管道拆。
        var heredocBodyStart = allowsNewlineSplit ? -1 : command.IndexOf('\n');
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoteChar = '\0';
        var parenDepth = 0;
        var inBacktick = false;

        for (var i = 0; i < command.Length; i++) {
            var c = command[i];

            if (quoteChar != '\0') {
                current.Append(c);
                // 双引号内的反斜杠转义不结束字符串
                if (c == '\\' && quoteChar == '"' && i + 1 < command.Length) {
                    current.Append(command[++i]);
                    continue;
                }
                if (c == quoteChar) {
                    quoteChar = '\0';
                }
                continue;
            }

            if (inBacktick) {
                current.Append(c);
                if (c == '`') {
                    inBacktick = false;
                }
                continue;
            }

            if (c is '\'' or '"') {
                quoteChar = c;
                current.Append(c);
                continue;
            }

            if (c == '`') {
                inBacktick = true;
                current.Append(c);
                continue;
            }

            if (c == '$' && i + 1 < command.Length && command[i + 1] == '(') {
                parenDepth++;
                current.Append(c).Append(command[++i]);
                continue;
            }

            if (parenDepth > 0) {
                if (c == '(') {
                    parenDepth++;
                } else if (c == ')') {
                    parenDepth--;
                }
                current.Append(c);
                continue;
            }

            if (c == ';') {
                FlushSegment(segments, current);
                continue;
            }

            if (c == '&' && i + 1 < command.Length && command[i + 1] == '&') {
                FlushSegment(segments, current);
                i++;
                continue;
            }

            if (c == '|') {
                if (i + 1 < command.Length && command[i + 1] == '|') {
                    FlushSegment(segments, current);
                    i++;
                    continue;
                }
                if (splitPipes && (heredocBodyStart < 0 || i < heredocBodyStart)) {
                    FlushSegment(segments, current);
                    continue;
                }
                // 单管道不拆：两侧是同一条命令的数据流
                current.Append(c);
                continue;
            }

            if (c == '\n' && allowsNewlineSplit) {
                FlushSegment(segments, current);
                continue;
            }

            current.Append(c);
        }

        FlushSegment(segments, current);
        return segments;
    }

    private static void FlushSegment(List<string> segments, System.Text.StringBuilder current) {
        var text = current.ToString().Trim();
        if (text.Length > 0) {
            segments.Add(text);
        }
        current.Clear();
    }

    private static bool DetectPowerShell(string command) {
        var powerShellIndicators = new[]
        {
            ShellToolName.Powershell.ToValue(), "pwsh", "Invoke-", "Get-", "Set-", "New-", "Remove-",
            "Write-", "Read-Host", "Select-Object", "Where-Object", "ForEach-Object"
        };

        return powerShellIndicators.Any(indicator =>
            command.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static bool DetectBash(string command) {
        var bashIndicators = new[]
        {
            ShellToolName.Bash.ToValue(), "sh ", "#!/bin/bash", "#!/bin/sh", "echo ", "grep ", "awk ", "sed "
        };

        return bashIndicators.Any(indicator =>
            command.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static (string CommandName, IReadOnlyList<string> Arguments) ParseCommandParts(string command) {
        var parts = SplitCommand(command);
        if (parts.Count == 0) {
            return (string.Empty, Array.Empty<string>());
        }

        return (parts[0], parts.Skip(1).ToList());
    }

    private static List<string> SplitCommand(string command) {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < command.Length; i++) {
            var c = command[i];

            if ((c == '"' || c == '\'') && !inQuotes) {
                inQuotes = true;
                quoteChar = c;
                continue;
            }

            if (c == quoteChar && inQuotes) {
                inQuotes = false;
                quoteChar = '\0';
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes) {
                if (current.Length > 0) {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static IReadOnlyList<string> ExtractPaths(IReadOnlyList<string> arguments) {
        return arguments
            .Where(IsPathLike)
            .Select(a => a.Trim('"', '\''))
            .ToList();
    }

    private static bool IsPathLike(string arg) {
        if (string.IsNullOrEmpty(arg))
            return false;

        // Windows 路径特征
        if (arg.Length >= 2 && char.IsLetter(arg[0]) && arg[1] == ':')
            return true;

        // Unix 路径特征
        if (arg.StartsWith('/') || arg.StartsWith("./") || arg.StartsWith("../"))
            return true;

        // 包含路径分隔符
        if (arg.Contains('/') || arg.Contains('\\'))
            return true;

        // 文件扩展名特征
        var fileExtensions = new[] { ".txt", ".cs", ".json", ".xml", ".md", ".yml", ".yaml" };
        if (fileExtensions.Any(ext => arg.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}