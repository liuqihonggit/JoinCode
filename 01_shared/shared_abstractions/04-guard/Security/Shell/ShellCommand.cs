namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Shell 命令封装 - 将解析逻辑从总线移到类型内部
/// </summary>
public sealed record ShellCommand
{
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
        bool hasRedirection)
    {
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
    public static ShellCommand Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
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

    private static bool DetectPowerShell(string command)
    {
        var powerShellIndicators = new[]
        {
            "powershell", "pwsh", "Invoke-", "Get-", "Set-", "New-", "Remove-",
            "Write-", "Read-Host", "Select-Object", "Where-Object", "ForEach-Object"
        };

        return powerShellIndicators.Any(indicator =>
            command.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static bool DetectBash(string command)
    {
        var bashIndicators = new[]
        {
            "bash", "sh ", "#!/bin/bash", "#!/bin/sh", "echo ", "grep ", "awk ", "sed "
        };

        return bashIndicators.Any(indicator =>
            command.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static (string CommandName, IReadOnlyList<string> Arguments) ParseCommandParts(string command)
    {
        var parts = SplitCommand(command);
        if (parts.Count == 0)
        {
            return (string.Empty, Array.Empty<string>());
        }

        return (parts[0], parts.Skip(1).ToList());
    }

    private static List<string> SplitCommand(string command)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if ((c == '"' || c == '\'') && !inQuotes)
            {
                inQuotes = true;
                quoteChar = c;
                continue;
            }

            if (c == quoteChar && inQuotes)
            {
                inQuotes = false;
                quoteChar = '\0';
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static IReadOnlyList<string> ExtractPaths(IReadOnlyList<string> arguments)
    {
        return arguments
            .Where(IsPathLike)
            .Select(a => a.Trim('"', '\''))
            .ToList();
    }

    private static bool IsPathLike(string arg)
    {
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
