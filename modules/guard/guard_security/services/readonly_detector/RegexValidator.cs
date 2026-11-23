namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 正则验证器实现 — 依赖 ICommandCatalog + IShellMetacharacterDetector
/// <para>对齐 TS READONLY_COMMAND_REGEXES</para>
/// </summary>
internal sealed class RegexValidator : IRegexValidator {
    private readonly ICommandCatalog _catalog;
    private readonly IShellMetacharacterDetector _metacharDetector;

    /// <summary>构造正则验证器</summary>
    public RegexValidator(ICommandCatalog catalog, IShellMetacharacterDetector metacharDetector) {
        _catalog = catalog;
        _metacharDetector = metacharDetector;
    }

    /// <inheritdoc />
    public bool MatchesReadOnlyRegex(string command) {
        var spaceIdx = command.IndexOf(' ');
        var cmdName = spaceIdx >= 0 ? command[..spaceIdx] : command;

        if (_catalog.IsSimpleReadOnly(cmdName)) {
            if (!_metacharDetector.ContainsShellMetacharacters(command)) {
                return true;
            }
        }

        return command is "pwd" or "whoami"
            || Regex.IsMatch(command, @"^echo(?:\s|$)")
            || Regex.IsMatch(command, @"^cd\s+")
            || Regex.IsMatch(command, @"^ls(?:\s|$)")
            || Regex.IsMatch(command, @"^find(?:\s|$)")
            || Regex.IsMatch(command, @"^node\s+-v$")
            || Regex.IsMatch(command, @"^node\s+--version$")
            || Regex.IsMatch(command, @"^python3?\s+--version$")
            || Regex.IsMatch(command, @"^history(?:\s+\d+)?\s*$")
            || Regex.IsMatch(command, @"^alias\s*$")
            || Regex.IsMatch(command, @"^arch(?:\s+(?:--help|-h))?\s*$")
            || Regex.IsMatch(command, @"^hostname(?:\s+(?:-[a-zA-Z]|--[a-zA-Z-]+))*\s*$");
    }

    /// <inheritdoc />
    public bool ContainsGitDangerousFlags(string command) {
        if (!command.StartsWith("git", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (command.Contains(" -c ", StringComparison.Ordinal)
            || command.Contains(" --exec-path", StringComparison.Ordinal)
            || command.Contains(" --config-env", StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }
}
