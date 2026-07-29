using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

[Register]
public sealed partial class BashPermissionChecker : IBashPermissionChecker
{
    [Inject] private readonly IBashSecurityValidator _securityValidator;
    [Inject] private readonly IPathConstraintValidator _pathConstraintValidator;
    [Inject] private readonly IReadOnlyCommandDetector _readOnlyDetector;

    private const int MaxSubcommandsForSecurityCheck = 50;

    private static readonly FrozenSet<string> SafeEnvVars = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "NODE_ENV", "PYTHONUNBUFFERED", "PYTHONDONTWRITEBYTECODE",
        "RUST_BACKTRACE", "RUST_LOG", "GOOS", "GOARCH", "CGO_ENABLED",
        "GO111MODULE", "GOEXPERIMENT", "LANG", "LC_ALL", "LC_CTYPE",
        "TERM", "TZ", "NO_COLOR", "FORCE_COLOR", "CLICOLOR",
        "LS_COLORS", "GREP_COLORS", ProviderEnvVar.AnthropicApiKey.ToValue(),
        "CUDA_VISIBLE_DEVICES", "DOCKER_HOST", "KUBECONFIG");

    private static readonly FrozenSet<string> BareShellPrefixes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "sh", "bash", "zsh", "fish", "csh", "tcsh", "ksh", "dash",
        "cmd", "powershell", "pwsh", "env", "xargs",
        "nice", "stdbuf", "nohup", "timeout", "time",
        "sudo", "doas", "pkexec");

    private static readonly Regex BinaryHijackVarsPattern = new(
        @"^(LD_|DYLD_|PATH$)", RegexOptions.Compiled);

    public BashPermissionResult CheckPermission(
        string command,
        string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new BashPermissionResult(PermissionBehavior.Passthrough);
        }

        var trimmed = command.Trim();

        var securityResult = _securityValidator.Validate(trimmed);
        if (!securityResult.IsSafe)
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: securityResult.Message ?? $"Security check failed: {securityResult.CheckId}",
                SuggestedRule: null);
        }

        var subcommands = SplitSubcommands(trimmed);
        if (subcommands.Count > MaxSubcommandsForSecurityCheck)
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: $"Command has too many subcommands ({subcommands.Count} > {MaxSubcommandsForSecurityCheck})");
        }

        var cdCount = subcommands.Count(sc => IsCdCommand(sc));
        var compoundCommandHasCd = cdCount > 0;

        if (cdCount > 1)
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: "Multiple cd commands in compound command require manual approval");
        }

        if (compoundCommandHasCd && ContainsGitCommand(subcommands))
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: "cd + git compound command requires manual approval");
        }

        foreach (var subcommand in subcommands)
        {
            var pathResult = _pathConstraintValidator.CheckPathConstraints(
                subcommand, workingDirectory, compoundCommandHasCd);
            if (pathResult.Behavior == PermissionBehavior.Deny)
            {
                return new BashPermissionResult(
                    PermissionBehavior.Deny,
                    Message: pathResult.Message,
                    SuggestedRule: null);
            }

            if (pathResult.Behavior == PermissionBehavior.Ask)
            {
                return new BashPermissionResult(
                    PermissionBehavior.Ask,
                    Message: pathResult.Message,
                    SuggestedRule: GenerateSuggestedRule(subcommand, pathResult));
            }

            var readOnlyResult = _readOnlyDetector.CheckReadOnlyConstraints(
                subcommand, compoundCommandHasCd);
            if (readOnlyResult.Behavior == PermissionBehavior.Ask)
            {
                return new BashPermissionResult(
                    PermissionBehavior.Ask,
                    Message: readOnlyResult.Message,
                    SuggestedRule: GenerateSuggestedRule(subcommand));
            }
        }

        var overallReadOnly = _readOnlyDetector.CheckReadOnlyConstraints(
            trimmed, compoundCommandHasCd);
        if (overallReadOnly.Behavior == PermissionBehavior.Allow)
        {
            return new BashPermissionResult(PermissionBehavior.Allow);
        }

        return new BashPermissionResult(
            PermissionBehavior.Passthrough,
            Message: "No explicit permission rule matched",
            SuggestedRule: GenerateSuggestedRule(trimmed));
    }

    #region 辅助方法

    private static List<string> SplitSubcommands(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                current.Append(c);
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                current.Append(c);
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                current.Append(c);
                continue;
            }

            if (c == ';')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                continue;
            }

            if (c == '&' && i + 1 < command.Length && command[i + 1] == '&')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                i++;
                continue;
            }

            if (c == '|' && i + 1 < command.Length && command[i + 1] == '|')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                i++;
                continue;
            }

            if (c == '|')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            var last = current.ToString().Trim();
            if (last.Length > 0)
            {
                result.Add(last);
            }
        }

        return result;
    }

    private static bool IsCdCommand(string command)
    {
        var stripped = BashSafeWrapperStripper.StripSafeWrappersString(command, SafeEnvVars);
        var spaceIdx = stripped.IndexOf(' ');
        var cmdName = spaceIdx >= 0 ? stripped[..spaceIdx] : stripped;
        return cmdName is "cd" or "pushd" or "popd";
    }

    private static bool ContainsGitCommand(List<string> subcommands) =>
        subcommands.Any(sc =>
        {
            var stripped = BashSafeWrapperStripper.StripSafeWrappersString(sc, SafeEnvVars);
            return stripped.StartsWith("git ", StringComparison.OrdinalIgnoreCase)
                || stripped.Equals("git", StringComparison.OrdinalIgnoreCase);
        });

    private static string GenerateSuggestedRule(
        string command,
        PathConstraintResult? pathResult = null)
    {
        var stripped = BashSafeWrapperStripper.StripSafeWrappersString(command, SafeEnvVars);
        var spaceIdx = stripped.IndexOf(' ');
        var cmdName = spaceIdx >= 0 ? stripped[..spaceIdx] : stripped;

        if (BareShellPrefixes.Contains(cmdName))
        {
            return command;
        }

        if (pathResult is not null && pathResult.OperationType is not null)
        {
            var prefix = GetSimpleCommandPrefix(stripped);
            if (prefix is not null)
            {
                return $"{prefix}:*";
            }
        }

        return command;
    }

    private static string? GetSimpleCommandPrefix(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        foreach (var c in command)
        {
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
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        if (tokens.Count == 0) return null;

        var cmdName = tokens[0];

        if (BareShellPrefixes.Contains(cmdName)) return null;

        if (tokens.Count > 1 && LooksLikeSubcommand(tokens[1]))
        {
            return $"{cmdName} {tokens[1]}";
        }

        return cmdName;
    }

    private static bool LooksLikeSubcommand(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (token.StartsWith('-')) return false;

        return token.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            && token.Any(char.IsLetter);
    }

    #endregion
}
