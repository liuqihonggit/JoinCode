namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令目录实现 — 管理所有命令配置数据
/// <para>单一数据源: CommandAllowlist + 5个FrozenSet + GitInternalPatterns</para>
/// <para>含所有 Build*SafeFlags 工厂方法和 Check*Dangerous 回调方法</para>
/// </summary>
internal sealed class CommandCatalog : ICommandCatalog {
    /// <summary>
    /// 简单只读命令列表 — 对齐 TS READONLY_COMMANDS
    /// </summary>
    private static readonly FrozenSet<string> SimpleReadOnlyCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        // 时间日期
        "cal", "uptime",
        // 文件内容查看
        "cat", "head", "tail", "wc", "stat", "strings", "hexdump", "od", "nl",
        // 系统信息
        "id", "uname", "free", "df", "du", "locale", "groups", "nproc",
        // 路径信息
        "basename", "dirname", "realpath",
        // 文本处理
        "cut", "paste", "tr", "column", "tac", "rev", "fold", "expand", "unexpand",
        "fmt", "comm", "cmp", "numfmt",
        // 路径信息（附加）
        "readlink",
        // 文件比较
        "diff",
        // 布尔值
        "true", "false",
        // 杂项安全命令
        "sleep", "which", "type", "expr", "test", "getconf", "seq", "tsort", "pr",
        // 目录列表
        "ls", "dir", "ll", "la",
        // 搜索（find/grep/rg 走白名单标志验证，不在此列表）
        // 进程
        "ps", "top", "htop",
        // 网络
        "ping", "netstat", "ifconfig", "nslookup", "traceroute",
        // 版本
        "whoami", "pwd", "echo", "printenv", "env");

    /// <summary>
    /// 安全的 Git 子命令 — 委托 GitCommandCatalog.SafeSubcommands 唯一数据源
    /// </summary>
    private static readonly FrozenSet<string> SafeGitSubcommands = GitCommandCatalog.SafeSubcommands;

    /// <summary>
    /// 危险的 Git 子命令 — 委托 GitCommandCatalog.DangerousSubcommands 唯一数据源
    /// </summary>
    private static readonly FrozenSet<string> DangerousGitSubcommands = GitCommandCatalog.DangerousSubcommands;

    /// <summary>
    /// xargs 自动批准的安全目标命令 — 对齐 TS SAFE_TARGET_COMMANDS_FOR_XARGS
    /// </summary>
    private static readonly FrozenSet<string> SafeXargsTargets = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "echo", "printf", "wc", "grep", "head", "tail");

    /// <summary>
    /// 命令白名单配置 — 对齐 TS COMMAND_ALLOWLIST
    /// </summary>
    private static readonly FrozenDictionary<string, CommandConfig> CommandAllowlist = BuildCommandAllowlist();

    /// <summary>
    /// Git 内部路径模式 — 对齐 TS GIT_INTERNAL_PATTERNS
    /// </summary>
    private static readonly Regex[] GitInternalPatternsField =
    [
        new(@"^HEAD$", RegexOptions.Compiled),
        new(@"^objects(?:\/|$)", RegexOptions.Compiled),
        new(@"^refs(?:\/|$)", RegexOptions.Compiled),
        new(@"^hooks(?:\/|$)", RegexOptions.Compiled),
    ];

    /// <summary>
    /// 非创建型写入命令 — 对齐 TS NON_CREATING_WRITE_COMMANDS
    /// </summary>
    private static readonly FrozenSet<string> NonCreatingWriteCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "rm", "rmdir", "sed");

    /// <inheritdoc />
    public bool TryGetConfig(List<string> tokens, [MaybeNullWhen(false)] out CommandConfig config) {
        var baseCommand = tokens[0];
        if (CommandAllowlist.TryGetValue(baseCommand, out config)) return true;
        if (CommandAllowlist.TryGetValue(TwoTokenKey(tokens), out config)) return true;
        if (CommandAllowlist.TryGetValue(ThreeTokenKey(tokens), out config)) return true;
        return false;
    }

    /// <inheritdoc />
    public bool IsSimpleReadOnly(string commandName) => SimpleReadOnlyCommands.Contains(commandName);

    /// <inheritdoc />
    public bool IsSafeXargsTarget(string commandName) => SafeXargsTargets.Contains(commandName);

    /// <inheritdoc />
    public bool IsNonCreatingWrite(string commandName) => NonCreatingWriteCommands.Contains(commandName);

    /// <inheritdoc />
    public bool IsSafeGitSubcommand(string subcommand) => SafeGitSubcommands.Contains(subcommand);

    /// <inheritdoc />
    public bool IsDangerousGitSubcommand(string subcommand) => DangerousGitSubcommands.Contains(subcommand);

    /// <inheritdoc />
    public IReadOnlyList<Regex> GitInternalPatterns => GitInternalPatternsField;

    /// <summary>
    /// 构造 2-token 查找键 — 用 string.Concat 直拼避免 Take().ToArray() 数组分配
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string TwoTokenKey(List<string> tokens)
        => tokens.Count >= 2 ? string.Concat(tokens[0], " ", tokens[1]) : tokens[0];

    /// <summary>
    /// 构造 3-token 查找键 — 用 string.Concat 直拼避免 Take().ToArray() 数组分配
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ThreeTokenKey(List<string> tokens)
        => tokens.Count >= 3 ? string.Concat(tokens[0], " ", tokens[1], " ", tokens[2])
        : TwoTokenKey(tokens);

    // === 以下方法从 ReadOnlyCommandDetector.GitFlags.cs 和 NonGitFlags.cs 迁移 ===
    // === BuildCommandAllowlist + 所有 Build*SafeFlags 工厂方法 + Check*Dangerous 回调方法 ===

private static FrozenDictionary<string, CommandConfig> BuildCommandAllowlist() {
        var builder = new Dictionary<string, CommandConfig>(StringComparer.OrdinalIgnoreCase);

        // file 命令
        builder["file"] = new CommandConfig(NonGitFlagBuilders.BuildFileSafeFlags());

        // sort 命令
        builder["sort"] = new CommandConfig(NonGitFlagBuilders.BuildSortSafeFlags());

        // man 命令
        builder["man"] = new CommandConfig(NonGitFlagBuilders.BuildManSafeFlags());

        // help 命令（bash 内建）
        builder["help"] = new CommandConfig(
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
                ["-d"] = FlagArgType.None,
                ["-m"] = FlagArgType.None,
                ["-s"] = FlagArgType.None,
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));

        // netstat 命令
        builder["netstat"] = new CommandConfig(NonGitFlagBuilders.BuildNetstatSafeFlags());

        // ps 命令
        builder["ps"] = new CommandConfig(NonGitFlagBuilders.BuildPsSafeFlags(),
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckPsDangerous);

        // base64 命令（macOS 不尊重 --）
        builder["base64"] = new CommandConfig(NonGitFlagBuilders.BuildBase64SafeFlags(),
            RespectsDoubleDash: false);

        // grep 命令
        builder["grep"] = new CommandConfig(NonGitFlagBuilders.BuildGrepSafeFlags());

        // rg (ripgrep) 命令 — 对齐 TS COMMAND_ALLOWLIST.rg
        builder["rg"] = new CommandConfig(NonGitFlagBuilders.BuildRgSafeFlags());

        // jq 命令 — 对齐 TS READONLY_COMMAND_REGEXES.jq（排除危险标志）
        builder["jq"] = new CommandConfig(NonGitFlagBuilders.BuildJqSafeFlags(),
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckJqDangerous);

        // find 命令 — 对齐 TS READONLY_COMMAND_REGEXES.find（排除危险操作）
        builder["find"] = new CommandConfig(NonGitFlagBuilders.BuildFindSafeFlags(),
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckFindDangerous);

        // sha256sum / sha1sum / md5sum
        builder["sha256sum"] = new CommandConfig(NonGitFlagBuilders.BuildChecksumSafeFlags());
        builder["sha1sum"] = new CommandConfig(NonGitFlagBuilders.BuildChecksumSafeFlags());
        builder["md5sum"] = new CommandConfig(NonGitFlagBuilders.BuildChecksumSafeFlags());

        // tree 命令（排除 -R 和 -o/--output）
        builder["tree"] = new CommandConfig(NonGitFlagBuilders.BuildTreeSafeFlags());

        // date 命令（位置参数必须以 + 开头）
        builder["date"] = new CommandConfig(NonGitFlagBuilders.BuildDateSafeFlags(),
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckDateDangerous);

        // hostname 命令（阻止位置参数）
        builder["hostname"] = new CommandConfig(NonGitFlagBuilders.BuildHostnameSafeFlags(),
            Regex: new Regex(@"^hostname(?:\s+(?:-[a-zA-Z]|--[a-zA-Z-]+))*\s*$", RegexOptions.Compiled));

        // lsof 命令（阻止 +m）
        builder["lsof"] = new CommandConfig(NonGitFlagBuilders.BuildLsofSafeFlags(),
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckLsofDangerous);

        // pgrep 命令
        builder["pgrep"] = new CommandConfig(NonGitFlagBuilders.BuildPgrepSafeFlags());

        // tput 命令（阻止危险能力名和 -S）
        builder["tput"] = new CommandConfig(
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
                ["-T"] = FlagArgType.Required,
                ["--terminal"] = FlagArgType.Required,
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckTputDangerous);

        // ss 命令（排除 -K/--kill, -D/--diag, -F/--filter, -N/--net）
        builder["ss"] = new CommandConfig(NonGitFlagBuilders.BuildSsSafeFlags());

        // fd / fdfind 命令
        builder["fd"] = new CommandConfig(NonGitFlagBuilders.BuildFdSafeFlags());
        builder["fdfind"] = new CommandConfig(NonGitFlagBuilders.BuildFdSafeFlags());

        // xargs 命令
        builder["xargs"] = new CommandConfig(NonGitFlagBuilders.BuildXargsSafeFlags());

        // sed 命令 — 对齐 TS sedValidation.ts 双层防御
        builder["sed"] = new CommandConfig(NonGitFlagBuilders.BuildSedSafeFlags(),
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckSedDangerous);

        // docker 只读子命令 — 对齐 TS DOCKER_READ_ONLY_COMMANDS
        builder["docker logs"] = new CommandConfig(NonGitFlagBuilders.BuildDockerLogsSafeFlags());
        builder["docker inspect"] = new CommandConfig(NonGitFlagBuilders.BuildDockerInspectSafeFlags());

        // docker ps / docker images — 对齐 TS EXTERNAL_READONLY_COMMANDS
        builder["docker ps"] = new CommandConfig(NonGitFlagBuilders.BuildDockerPsSafeFlags());
        builder["docker images"] = new CommandConfig(NonGitFlagBuilders.BuildDockerImagesSafeFlags());

        // pyright 命令 — 对齐 TS PYRIGHT_READ_ONLY_COMMANDS
        builder["pyright"] = new CommandConfig(NonGitFlagBuilders.BuildPyrightSafeFlags(),
            RespectsDoubleDash: false,
            AdditionalDangerousCallback: NonGitFlagBuilders.CheckPyrightDangerous);

        // git 只读子命令 — 对齐 TS GIT_READ_ONLY_COMMANDS（24个独立注册，每个子命令有专属安全标志）
        builder["git diff"] = new CommandConfig(GitFlagBuilders.BuildGitDiffSafeFlags());
        builder["git log"] = new CommandConfig(GitFlagBuilders.BuildGitLogSafeFlags());
        builder["git show"] = new CommandConfig(GitFlagBuilders.BuildGitShowSafeFlags());
        builder["git shortlog"] = new CommandConfig(GitFlagBuilders.BuildGitShortlogSafeFlags());
        builder["git reflog"] = new CommandConfig(GitFlagBuilders.BuildGitReflogSafeFlags(),
            AdditionalDangerousCallback: GitFlagBuilders.CheckGitReflogDangerous);
        builder["git stash list"] = new CommandConfig(GitFlagBuilders.BuildGitStashListSafeFlags());
        builder["git ls-remote"] = new CommandConfig(GitFlagBuilders.BuildGitLsRemoteSafeFlags());
        builder["git status"] = new CommandConfig(GitFlagBuilders.BuildGitStatusSafeFlags());
        builder["git blame"] = new CommandConfig(GitFlagBuilders.BuildGitBlameSafeFlags());
        builder["git ls-files"] = new CommandConfig(GitFlagBuilders.BuildGitLsFilesSafeFlags());
        builder["git config --get"] = new CommandConfig(GitFlagBuilders.BuildGitConfigGetSafeFlags());
        builder["git remote show"] = new CommandConfig(GitFlagBuilders.BuildGitRemoteShowSafeFlags(),
            AdditionalDangerousCallback: GitFlagBuilders.CheckGitRemoteShowDangerous);
        builder["git remote"] = new CommandConfig(GitFlagBuilders.BuildGitRemoteSafeFlags(),
            AdditionalDangerousCallback: GitFlagBuilders.CheckGitRemoteDangerous);
        builder["git merge-base"] = new CommandConfig(GitFlagBuilders.BuildGitMergeBaseSafeFlags());
        builder["git rev-parse"] = new CommandConfig(GitFlagBuilders.BuildGitRevParseSafeFlags());
        builder["git rev-list"] = new CommandConfig(GitFlagBuilders.BuildGitRevListSafeFlags());
        builder["git describe"] = new CommandConfig(GitFlagBuilders.BuildGitDescribeSafeFlags());
        builder["git cat-file"] = new CommandConfig(GitFlagBuilders.BuildGitCatFileSafeFlags());
        builder["git for-each-ref"] = new CommandConfig(GitFlagBuilders.BuildGitForEachRefSafeFlags());
        builder["git grep"] = new CommandConfig(GitFlagBuilders.BuildGitGrepSafeFlags());
        builder["git stash show"] = new CommandConfig(GitFlagBuilders.BuildGitStashShowSafeFlags());
        builder["git worktree list"] = new CommandConfig(GitFlagBuilders.BuildGitWorktreeListSafeFlags());
        builder["git tag"] = new CommandConfig(GitFlagBuilders.BuildGitTagSafeFlags(),
            AdditionalDangerousCallback: GitFlagBuilders.CheckGitTagDangerous);
        builder["git branch"] = new CommandConfig(GitFlagBuilders.BuildGitBranchSafeFlags(),
            AdditionalDangerousCallback: GitFlagBuilders.CheckGitBranchDangerous);
        builder["git cherry-pick"] = new CommandConfig(GitFlagBuilders.BuildGitCherryPickSafeFlags());
        builder["git whatchanged"] = new CommandConfig(GitFlagBuilders.BuildGitWhatchangedSafeFlags());
        builder["git show-branch"] = new CommandConfig(GitFlagBuilders.BuildGitShowBranchSafeFlags());
        builder["git verify-pack"] = new CommandConfig(GitFlagBuilders.BuildGitVerifyPackSafeFlags());
        builder["git annotate"] = new CommandConfig(GitFlagBuilders.BuildGitAnnotateSafeFlags());
        builder["git name-rev"] = new CommandConfig(GitFlagBuilders.BuildGitNameRevSafeFlags());

        return builder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
