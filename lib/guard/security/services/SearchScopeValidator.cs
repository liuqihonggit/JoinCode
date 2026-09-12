namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 搜索范围验证器实现 — 检测搜索命令的危险标志和过大路径
/// 使用 FrozenSet/FrozenDictionary 实现高效忽略大小写匹配
/// 支持热重载：通过 ISearchScopeReloadable 接口动态更新额外配置
/// </summary>
[Register(typeof(ISearchScopeValidator), ServiceLifetime.Singleton)]
[Register(typeof(ISearchScopeReloadable), ServiceLifetime.Singleton)]
public sealed partial class SearchScopeValidator : ServiceEntity, ISearchScopeValidator, ISearchScopeReloadable
{
    private static readonly FrozenSet<string> SearchCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "rg", "grep", "egrep", "fgrep", "ag", "ack",
        "find", "fd", "fdfind", "locate", "mlocate");

    private static readonly FrozenDictionary<string, FrozenSet<string>> BuiltInDangerousFlags =
        new Dictionary<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["rg"] = FrozenSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "--no-ignore", "--no-ignore-parent", "--no-ignore-vcs",
                "--no-ignore-dot", "--no-ignore-global", "--no-ignore-exclude",
                "-u", "--unrestricted"),
            ["grep"] = FrozenSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "-r", "-R", "--recursive", "--dereference-recursive"),
            ["find"] = FrozenSet.Create<string>(
                StringComparer.OrdinalIgnoreCase),
            ["ag"] = FrozenSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "-u", "--unrestricted", "--ignore-case"),
            ["ack"] = FrozenSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "--ignore-case", "-i"),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> BuiltInExcessivePathPrefixes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "/", "/home", "/etc", "/var", "/usr", "/opt", "/srv", "/tmp",
        @"C:\", @"D:\", @"E:\", @"F:\",
        @"C:\Users", @"C:\Windows", @"C:\Program Files", @"C:\ProgramData");

    private static readonly Regex DriveRootPattern = new(
        @"^[A-Za-z]:\\?$", RegexOptions.Compiled);

    private volatile bool _enabled = true;
    private volatile FrozenDictionary<string, FrozenSet<string>> _mergedDangerousFlags = BuiltInDangerousFlags;
    private volatile FrozenSet<string> _mergedExcessivePathPrefixes = BuiltInExcessivePathPrefixes;

    public SearchScopeValidationResult? Validate(ShellCommand command, string workingDirectory)
    {
        if (!_enabled)
        {
            return null;
        }

        if (!SearchCommands.Contains(command.CommandName))
        {
            return null;
        }

        var risks = new List<string>();

        if (HasDangerousFlags(command, out var dangerousFlagDetails) && dangerousFlagDetails is not null)
        {
            risks.Add(dangerousFlagDetails);
        }

        if (HasExcessivePath(command, out var pathDetails) && pathDetails is not null)
        {
            risks.Add(pathDetails);
        }

        if (risks.Count == 0)
        {
            return null;
        }

        var details = string.Join("; ", risks);
        var suggestion = BuildSuggestion(command.CommandName, risks.Count > 0 && dangerousFlagDetails != null);

        return new SearchScopeValidationResult(
            CommandRisk.ExcessiveSearchScope,
            details,
            suggestion);
    }

    /// <summary>
    /// 热重载搜索范围配置 — 双变量切换模式：构建新快照 → 原子替换引用
    /// </summary>
    public void ReloadSearchScope(SearchScopeConfig config)
    {
        _enabled = config.Enabled;

        var mergedFlags = MergeDangerousFlags(config.ExtraDangerousFlags);
        _mergedDangerousFlags = mergedFlags;

        var mergedPaths = MergeExcessivePathPrefixes(config.ExtraExcessivePathPrefixes);
        _mergedExcessivePathPrefixes = mergedPaths;
    }

    private static FrozenDictionary<string, FrozenSet<string>> MergeDangerousFlags(
        Dictionary<string, FrozenSet<string>> extra)
    {
        if (extra.Count == 0)
        {
            return BuiltInDangerousFlags;
        }

        var merged = new Dictionary<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (cmd, flags) in BuiltInDangerousFlags)
        {
            if (extra.TryGetValue(cmd, out var extraFlags))
            {
                merged[cmd] = FrozenSet.Create(
                    StringComparer.OrdinalIgnoreCase,
                    [.. flags, .. extraFlags]);
            }
            else
            {
                merged[cmd] = flags;
            }
        }

        foreach (var (cmd, flags) in extra)
        {
            if (!merged.ContainsKey(cmd))
            {
                merged[cmd] = flags;
            }
        }

        return merged.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenSet<string> MergeExcessivePathPrefixes(FrozenSet<string> extra)
    {
        if (extra.Count == 0)
        {
            return BuiltInExcessivePathPrefixes;
        }

        return FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            [.. BuiltInExcessivePathPrefixes, .. extra]);
    }

    private bool HasDangerousFlags(ShellCommand command, out string? details)
    {
        details = null;

        var flags = _mergedDangerousFlags;
        if (!flags.TryGetValue(command.CommandName, out var cmdFlags))
        {
            return false;
        }

        var found = new List<string>();
        foreach (var arg in command.Arguments)
        {
            var normalizedArg = arg.StartsWith("--", StringComparison.Ordinal)
                ? arg.Split('=', 2)[0]
                : arg;

            if (cmdFlags.Contains(normalizedArg))
            {
                found.Add(arg);
            }
        }

        if (found.Count == 0)
        {
            return false;
        }

        details = $"危险标志: {string.Join(", ", found)} — 可能导致搜索忽略忽略规则，遍历大量文件";
        return true;
    }

    private bool HasExcessivePath(ShellCommand command, out string? details)
    {
        details = null;

        var prefixes = _mergedExcessivePathPrefixes;

        foreach (var arg in command.Arguments)
        {
            if (!IsPathLike(arg))
            {
                continue;
            }

            var normalized = arg.Replace('/', '\\').TrimEnd('\\');

            if (prefixes.Contains(normalized) || prefixes.Contains(arg))
            {
                details = $"搜索路径过大: '{arg}' — 搜索系统根目录可能导致长时间卡顿";
                return true;
            }

            if (DriveRootPattern.IsMatch(arg))
            {
                details = $"搜索路径过大: '{arg}' — 搜索盘符根目录可能导致长时间卡顿";
                return true;
            }
        }

        return false;
    }

    private static string BuildSuggestion(string commandName, bool hasDangerousFlags)
    {
        var sb = new StringBuilder();

        if (hasDangerousFlags)
        {
            sb.Append(commandName.Equals("rg", StringComparison.OrdinalIgnoreCase)
                ? "移除 --no-ignore/-u 等标志，让 rg 遵守 .gitignore 规则"
                : "避免使用会忽略忽略规则的标志");
        }

        sb.Append("。将搜索范围限制在项目目录内，如指定具体子目录路径");

        return sb.ToString();
    }

    private static bool IsPathLike(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return false;
        }

        if (arg.Length >= 2 && char.IsLetter(arg[0]) && arg[1] == ':')
        {
            return true;
        }

        if (arg.StartsWith('/') || arg.StartsWith("./") || arg.StartsWith("../"))
        {
            return true;
        }

        if (arg.Contains('/') || arg.Contains('\\'))
        {
            return true;
        }

        return false;
    }
}
