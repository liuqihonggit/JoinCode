namespace JoinCode.Gh;

internal static class CliArgCliOptionConstants {
    public const string HelpLongName = "--help";
    public const string HelpShortName = "-h";
    public const string JsonLongName = "--json";
    public const string FormatLongName = "--format";

    public static readonly FrozenSet<string> BooleanFlags = new[] {
        "--help", "-h", "--version", "-v", "--non-interactive", "--no-confirm",
        "--trust", "--force-interactive", "--debuglog", "-d", "--continue", "-c",
        "--dry-run", "--yes", "-y", "--force", "--quiet", "-q", "--json",
        "--dangerously-skip-permissions", "--no-cache"
    }.ToFrozenSet();

    public static readonly FrozenSet<string> AllOptionNames = new[] {
        "--help", "-h", "--version", "-v", "--pipe", "--prompt", "-p", "--model", "-m",
        "--vendor", "--non-interactive", "--no-confirm", "--trust", "--force-interactive",
        "--await", "--debuglog", "-d", "--continue", "-c", "--resume", "-r",
        "--permission-mode", "--dangerously-skip-permissions", "--allowed-tools",
        "--disallowed-tools", "--system-prompt", "--append-system-prompt", "--json",
        "--format", "--dry-run", "--yes", "-y", "--force", "--quiet", "-q", "--no-cache"
    }.ToFrozenSet();
}

internal static class TerminalHelper {
    public static void WriteLine(string? msg = null) => Console.WriteLine(msg);
    public static void WriteError(string msg) => Console.Error.WriteLine(msg);
    public static void NewLine() => Console.WriteLine();
}

internal static class FlatSubCommandRouter {
    internal static string? GetPositional(string[] args, int index) {
        var positionalIndex = 0;
        for (var i = 1; i < args.Length; i++) {
            if (args[i].StartsWith("--")) {
                if (ShouldConsumeNext(args, i)) i++;
                continue;
            }
            if (positionalIndex == index) return args[i];
            positionalIndex++;
        }
        return null;
    }

    private static bool ShouldConsumeNext(string[] args, int i) {
        if (CliArgCliOptionConstants.BooleanFlags.Contains(args[i])) return false;
        if (i + 1 >= args.Length) return false;
        if (args[i + 1].StartsWith("--")) return false;
        var eqIdx = args[i + 1].IndexOf('=');
        return eqIdx <= 0 || eqIdx >= args[i + 1].Length - 1;
    }

    internal static bool ShouldOutputJson(string[] args) {
        var formatValue = GetOptionValue(args, CliArgCliOptionConstants.FormatLongName);
        if (!string.IsNullOrEmpty(formatValue))
            return !string.Equals(formatValue, "text", StringComparison.OrdinalIgnoreCase);
        var envFormat = Environment.GetEnvironmentVariable("JCC_OUTPUT_FORMAT");
        if (string.Equals(envFormat, "text", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string? GetOptionValue(string[] args, string optionName) {
        for (var i = 1; i < args.Length - 1; i++)
            if (args[i] == optionName) return args[i + 1];
        return null;
    }
}

public sealed class CliStructuredError {
    public string Code { get; init; }
    public string Message { get; init; }
    public string? Hint { get; init; }
    public bool Retryable { get; init; }

    public CliStructuredError(string code, string message, string? hint = null, bool retryable = false) {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Hint = hint;
        Retryable = retryable;
    }

    public string ToRustStyleString(string token) {
        var sb = new StringBuilder();
        sb.AppendLine($"error: {Message}");
        sb.AppendLine("  |");
        sb.AppendLine($"  | {token}");
        var arrow = new string('^', token.Length);
        sb.AppendLine($"  | {arrow} {Message}");
        sb.AppendLine("  |");
        if (!string.IsNullOrEmpty(Hint)) sb.AppendLine($"hint: {Hint}");
        return sb.ToString();
    }
}

internal static class CliErrorCatalog {
    internal static CliStructuredError ArgParseError(string detail) =>
        new("ARG_PARSE_ERROR", detail, "使用 --help 查看可用选项");
    internal static CliStructuredError ArgMissingRequired(string paramName) =>
        new("ARG_MISSING_REQUIRED", $"缺少必需参数: {paramName}", "使用 --help 查看参数说明");
    internal static CliStructuredError ResourceNotFound(string resourceType, string identifier) =>
        new("RESOURCE_NOT_FOUND", $"{resourceType} '{identifier}' 未找到", "请检查标识符是否正确");
    internal static CliStructuredError ArgUnknownOption(string option) =>
        new("ARG_UNKNOWN_OPTION", $"未知选项 '{option}'", "可用选项见 jcc --help");
}
