namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 非Git命令的安全标志构建器 — 所有非Git的 Build*SafeFlags 和 Check*Dangerous 方法
/// <para>单一数据源: 非Git命令的 FrozenDictionary 标志配置</para>
/// </summary>
internal static class NonGitFlagBuilders {

    internal static FrozenDictionary<string, FlagArgType> BuildFileSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-b"] = FlagArgType.None,
            ["--brief"] = FlagArgType.None,
            ["-C"] = FlagArgType.None,
            ["--compile"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["--debug"] = FlagArgType.None,
            ["-i"] = FlagArgType.Required,
            ["--mime-type"] = FlagArgType.None,
            ["--mime-encoding"] = FlagArgType.None,
            ["-k"] = FlagArgType.None,
            ["--keep-going"] = FlagArgType.None,
            ["-L"] = FlagArgType.Required,
            ["--dereference"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            ["-z"] = FlagArgType.None,
            ["--uncompress"] = FlagArgType.None,
            ["-0"] = FlagArgType.None,
            ["--print0"] = FlagArgType.None,
            ["-F"] = FlagArgType.Required,
            ["--separator"] = FlagArgType.Required,
            ["-m"] = FlagArgType.Required,
            ["--magic-file"] = FlagArgType.Required,
            ["-N"] = FlagArgType.None,
            ["--no-buffer"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--no-pad"] = FlagArgType.None,
            ["-p"] = FlagArgType.None,
            ["--preserve-date"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--raw"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--special-files"] = FlagArgType.None,
            ["-S"] = FlagArgType.None,
            ["--no-sandbox"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildSortSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-b"] = FlagArgType.None,
            ["--ignore-leading-blanks"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["--dictionary-order"] = FlagArgType.None,
            ["-f"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
            ["-g"] = FlagArgType.None,
            ["--general-numeric-sort"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--human-numeric-sort"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--ignore-nonprinting"] = FlagArgType.None,
            ["-M"] = FlagArgType.None,
            ["--month-sort"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--numeric-sort"] = FlagArgType.None,
            ["-R"] = FlagArgType.None,
            ["--random-sort"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--reverse"] = FlagArgType.None,
            ["-V"] = FlagArgType.None,
            ["--version-sort"] = FlagArgType.None,
            ["-c"] = FlagArgType.None,
            ["--check"] = FlagArgType.Optional,
            ["-C"] = FlagArgType.None,
            ["--check=silent"] = FlagArgType.None,
            ["-k"] = FlagArgType.Required,
            ["--key"] = FlagArgType.Required,
            ["-m"] = FlagArgType.None,
            ["--merge"] = FlagArgType.None,
            ["-o"] = FlagArgType.Required,
            ["--output"] = FlagArgType.Required,
            ["-s"] = FlagArgType.None,
            ["--stable"] = FlagArgType.None,
            ["-S"] = FlagArgType.Required,
            ["--buffer-size"] = FlagArgType.Required,
            ["-t"] = FlagArgType.Required,
            ["--field-separator"] = FlagArgType.Required,
            ["-T"] = FlagArgType.Required,
            ["--temporary-directory"] = FlagArgType.Required,
            ["-u"] = FlagArgType.None,
            ["--unique"] = FlagArgType.None,
            ["-z"] = FlagArgType.None,
            ["--zero-terminated"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildManSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-a"] = FlagArgType.None,
            ["--all"] = FlagArgType.None,
            ["-c"] = FlagArgType.None,
            ["--catman"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["--debug"] = FlagArgType.None,
            ["-D"] = FlagArgType.None,
            ["--default"] = FlagArgType.None,
            ["-f"] = FlagArgType.None,
            ["--whatis"] = FlagArgType.None,
            ["-k"] = FlagArgType.None,
            ["--apropos"] = FlagArgType.None,
            ["-K"] = FlagArgType.None,
            ["--global-apropos"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--local-file"] = FlagArgType.None,
            ["-L"] = FlagArgType.Required,
            ["--locale"] = FlagArgType.Required,
            ["-M"] = FlagArgType.Required,
            ["--manpath"] = FlagArgType.Required,
            ["-P"] = FlagArgType.Required,
            ["--pager"] = FlagArgType.Required,
            ["-S"] = FlagArgType.Required,
            ["--sections"] = FlagArgType.Required,
            ["-w"] = FlagArgType.None,
            ["--where"] = FlagArgType.None,
            ["-W"] = FlagArgType.None,
            ["--where-cat"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildNetstatSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-a"] = FlagArgType.None,
            ["--all"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--listening"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--numeric"] = FlagArgType.None,
            ["-p"] = FlagArgType.None,
            ["--programs"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--route"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--statistics"] = FlagArgType.None,
            ["-t"] = FlagArgType.None,
            ["--tcp"] = FlagArgType.None,
            ["-u"] = FlagArgType.None,
            ["--udp"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["--raw"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["--unix"] = FlagArgType.None,
            ["-4"] = FlagArgType.None,
            ["-6"] = FlagArgType.None,
            ["-e"] = FlagArgType.None,
            ["--extend"] = FlagArgType.None,
            ["-c"] = FlagArgType.Required,
            ["--continuous"] = FlagArgType.Required,
            ["-g"] = FlagArgType.None,
            ["--groups"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--interfaces"] = FlagArgType.None,
            ["-M"] = FlagArgType.None,
            ["--masquerade"] = FlagArgType.None,
            ["-N"] = FlagArgType.None,
            ["--symbolic"] = FlagArgType.None,
            ["-V"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildPsSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-a"] = FlagArgType.None,
            ["-A"] = FlagArgType.None,
            ["-e"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["-f"] = FlagArgType.None,
            ["-F"] = FlagArgType.None,
            ["-j"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["-L"] = FlagArgType.None,
            ["-o"] = FlagArgType.Required,
            ["-O"] = FlagArgType.Required,
            ["-p"] = FlagArgType.Required,
            ["-q"] = FlagArgType.Required,
            ["-t"] = FlagArgType.Required,
            ["-u"] = FlagArgType.Required,
            ["-U"] = FlagArgType.Required,
            ["-G"] = FlagArgType.Required,
            ["-g"] = FlagArgType.Required,
            ["-s"] = FlagArgType.Required,
            ["-H"] = FlagArgType.None,
            ["-m"] = FlagArgType.None,
            ["-y"] = FlagArgType.None,
            ["-Z"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            ["--no-headers"] = FlagArgType.None,
            ["--headers"] = FlagArgType.None,
            ["--forest"] = FlagArgType.None,
            ["--full"] = FlagArgType.None,
            ["--cols"] = FlagArgType.Required,
            ["--columns"] = FlagArgType.Required,
            ["--lines"] = FlagArgType.Required,
            ["--rows"] = FlagArgType.Required,
            ["--sort"] = FlagArgType.Required,
            ["--info"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static bool CheckPsDangerous(string _, IReadOnlyList<string> args) {
        // BSD 风格 e 修饰符显示环境变量
        return args.Any(a => a.Length > 0 && !a.StartsWith('-') && a.Contains('e'));
    }

    internal static FrozenDictionary<string, FlagArgType> BuildBase64SafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-d"] = FlagArgType.None,
            ["--decode"] = FlagArgType.None,
            ["-D"] = FlagArgType.None,
            ["--decode"] = FlagArgType.None,
            ["-e"] = FlagArgType.None,
            ["--encode"] = FlagArgType.None,
            ["-i"] = FlagArgType.Required,
            ["--input"] = FlagArgType.Required,
            ["-o"] = FlagArgType.Required,
            ["--output"] = FlagArgType.Required,
            ["-n"] = FlagArgType.None,
            ["--no-newline"] = FlagArgType.None,
            ["-u"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            ["--break"] = FlagArgType.Required,
            ["--ignore-garbage"] = FlagArgType.None,
            ["--wrap"] = FlagArgType.Required,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildGrepSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-E"] = FlagArgType.None,
            ["--extended-regexp"] = FlagArgType.None,
            ["-F"] = FlagArgType.None,
            ["--fixed-strings"] = FlagArgType.None,
            ["-G"] = FlagArgType.None,
            ["--basic-regexp"] = FlagArgType.None,
            ["-P"] = FlagArgType.None,
            ["--perl-regexp"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["--invert-match"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["--word-regexp"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["--line-regexp"] = FlagArgType.None,
            ["-c"] = FlagArgType.None,
            ["--count"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--files-with-matches"] = FlagArgType.None,
            ["-L"] = FlagArgType.None,
            ["--files-without-match"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--line-number"] = FlagArgType.None,
            ["-H"] = FlagArgType.None,
            ["--with-filename"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--no-filename"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["--only-matching"] = FlagArgType.None,
            ["-q"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--no-messages"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--recursive"] = FlagArgType.None,
            ["-R"] = FlagArgType.None,
            ["--dereference-recursive"] = FlagArgType.None,
            ["-e"] = FlagArgType.Required,
            ["--regexp"] = FlagArgType.Required,
            ["-f"] = FlagArgType.Required,
            ["--file"] = FlagArgType.Required,
            ["-m"] = FlagArgType.Required,
            ["--max-count"] = FlagArgType.Required,
            ["-A"] = FlagArgType.Required,
            ["--after-context"] = FlagArgType.Required,
            ["-B"] = FlagArgType.Required,
            ["--before-context"] = FlagArgType.Required,
            ["-C"] = FlagArgType.Required,
            ["--context"] = FlagArgType.Required,
            ["--include"] = FlagArgType.Required,
            ["--exclude"] = FlagArgType.Required,
            ["--exclude-from"] = FlagArgType.Required,
            ["--exclude-dir"] = FlagArgType.Required,
            ["--color"] = FlagArgType.Optional,
            ["--binary-files"] = FlagArgType.Required,
            ["-a"] = FlagArgType.None,
            ["--text"] = FlagArgType.None,
            ["-I"] = FlagArgType.None,
            ["-b"] = FlagArgType.None,
            ["--byte-offset"] = FlagArgType.None,
            ["-T"] = FlagArgType.None,
            ["--initial-tab"] = FlagArgType.None,
            ["-Z"] = FlagArgType.None,
            ["--null"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// rg (ripgrep) 安全标志 — 对齐 TS COMMAND_ALLOWLIST.rg
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildRgSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            // 搜索模式
            ["-e"] = FlagArgType.Required,
            ["--regexp"] = FlagArgType.Required,
            ["-f"] = FlagArgType.Required,
            ["--file"] = FlagArgType.Required,
            ["-i"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
            ["-S"] = FlagArgType.None,
            ["--smart-case"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--case-sensitive"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["--invert-match"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["--word-regexp"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["--line-regexp"] = FlagArgType.None,
            ["-F"] = FlagArgType.None,
            ["--fixed-strings"] = FlagArgType.None,
            ["-U"] = FlagArgType.None,
            ["--multiline"] = FlagArgType.None,
            ["-P"] = FlagArgType.None,
            ["--pcre2"] = FlagArgType.None,
            // 输出控制
            ["-c"] = FlagArgType.None,
            ["--count"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--files-with-matches"] = FlagArgType.None,
            ["-L"] = FlagArgType.None,
            ["--files-without-match"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--line-number"] = FlagArgType.None,
            ["-N"] = FlagArgType.None,
            ["--no-line-number"] = FlagArgType.None,
            ["-H"] = FlagArgType.None,
            ["--with-filename"] = FlagArgType.None,
            ["-I"] = FlagArgType.None,
            ["--no-filename"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["--only-matching"] = FlagArgType.None,
            ["-q"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
            ["-0"] = FlagArgType.None,
            ["--null"] = FlagArgType.None,
            ["--column"] = FlagArgType.None,
            ["-A"] = FlagArgType.Required,
            ["--after-context"] = FlagArgType.Required,
            ["-B"] = FlagArgType.Required,
            ["--before-context"] = FlagArgType.Required,
            ["-C"] = FlagArgType.Required,
            ["--context"] = FlagArgType.Required,
            ["-m"] = FlagArgType.Required,
            ["--max-count"] = FlagArgType.Required,
            // 文件过滤
            ["-g"] = FlagArgType.Required,
            ["--glob"] = FlagArgType.Required,
            ["-t"] = FlagArgType.Required,
            ["--type"] = FlagArgType.Required,
            ["-T"] = FlagArgType.Required,
            ["--type-not"] = FlagArgType.Required,
            ["--type-add"] = FlagArgType.Required,
            ["--type-clear"] = FlagArgType.Required,
            ["--sort"] = FlagArgType.Required,
            ["--sortr"] = FlagArgType.Required,
            // 搜索行为
            ["-r"] = FlagArgType.Required,
            ["--replace"] = FlagArgType.Required,
            ["-j"] = FlagArgType.Required,
            ["--threads"] = FlagArgType.Required,
            ["--maxdepth"] = FlagArgType.Required,
            ["--max-filesize"] = FlagArgType.Required,
            ["--deterministic-order"] = FlagArgType.None,
            ["-M"] = FlagArgType.Required,
            ["--max-columns"] = FlagArgType.Required,
            // 显示选项
            ["--color"] = FlagArgType.Optional,
            ["--colors"] = FlagArgType.Required,
            ["--heading"] = FlagArgType.None,
            ["--no-heading"] = FlagArgType.None,
            ["--line-buffered"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// jq 安全标志 — 对齐 TS READONLY_COMMAND_REGEXES.jq
    /// 故意排除: -f/--from-file/--rawfile/--slurpfile/--run-tests/-L/--library-path
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildJqSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-e"] = FlagArgType.None,
            ["--exit-status"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--null-input"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--raw-output"] = FlagArgType.None,
            ["-j"] = FlagArgType.None,
            ["--join-output"] = FlagArgType.None,
            ["-c"] = FlagArgType.None,
            ["--compact-output"] = FlagArgType.None,
            ["-C"] = FlagArgType.None,
            ["--color-output"] = FlagArgType.None,
            ["-M"] = FlagArgType.None,
            ["--monochrome-output"] = FlagArgType.None,
            ["-S"] = FlagArgType.None,
            ["--sort-keys"] = FlagArgType.None,
            ["-R"] = FlagArgType.None,
            ["--raw-input"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--slurp"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--in-place"] = FlagArgType.None,
            ["--tab"] = FlagArgType.None,
            ["--indent"] = FlagArgType.Required,
            ["--arg"] = FlagArgType.Required,
            ["--argjson"] = FlagArgType.Required,
            ["--args"] = FlagArgType.None,
            ["--jsonargs"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// jq 危险回调 — 阻止 $env/$ENV 访问（可能泄露环境变量）
    /// </summary>
    internal static bool CheckJqDangerous(string command, IReadOnlyList<string> _) {
        if (command.Contains("$env", StringComparison.Ordinal)
            || command.Contains("$ENV", StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }

    /// <summary>
    /// find 安全标志 — 对齐 TS READONLY_COMMAND_REGEXES.find
    /// 故意排除: -delete, -exec, -execdir, -ok, -okdir, -fprint0?, -fls, -fprintf
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildFindSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            // 搜索条件
            ["-name"] = FlagArgType.Required,
            ["-iname"] = FlagArgType.Required,
            ["-path"] = FlagArgType.Required,
            ["-ipath"] = FlagArgType.Required,
            ["-regex"] = FlagArgType.Required,
            ["-iregex"] = FlagArgType.Required,
            ["-type"] = FlagArgType.Required,
            ["-size"] = FlagArgType.Required,
            ["-empty"] = FlagArgType.None,
            ["-perm"] = FlagArgType.Required,
            ["-readable"] = FlagArgType.None,
            ["-writable"] = FlagArgType.None,
            ["-executable"] = FlagArgType.None,
            ["-user"] = FlagArgType.Required,
            ["-group"] = FlagArgType.Required,
            ["-uid"] = FlagArgType.Required,
            ["-gid"] = FlagArgType.Required,
            ["-mtime"] = FlagArgType.Required,
            ["-atime"] = FlagArgType.Required,
            ["-ctime"] = FlagArgType.Required,
            ["-mmin"] = FlagArgType.Required,
            ["-amin"] = FlagArgType.Required,
            ["-cmin"] = FlagArgType.Required,
            ["-newer"] = FlagArgType.Required,
            ["-anewer"] = FlagArgType.Required,
            ["-cnewer"] = FlagArgType.Required,
            // 逻辑操作
            ["-not"] = FlagArgType.None,
            ["-a"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["-or"] = FlagArgType.None,
            ["-and"] = FlagArgType.None,
            ["!"] = FlagArgType.None,
            ["("] = FlagArgType.None,
            [")"] = FlagArgType.None,
            // 输出格式
            ["-print"] = FlagArgType.None,
            ["-print0"] = FlagArgType.None,
            ["-printf"] = FlagArgType.Required,
            ["-ls"] = FlagArgType.None,
            ["-maxdepth"] = FlagArgType.Required,
            ["-mindepth"] = FlagArgType.Required,
            ["-depth"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["-follow"] = FlagArgType.None,
            ["-mount"] = FlagArgType.None,
            ["-xdev"] = FlagArgType.None,
            ["-prune"] = FlagArgType.None,
            ["-quit"] = FlagArgType.None,
            ["-true"] = FlagArgType.None,
            ["-false"] = FlagArgType.None,
            ["-daystart"] = FlagArgType.None,
            ["-regextype"] = FlagArgType.Required,
            ["-help"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["-version"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// find 危险回调 — 阻止 -delete/-exec/-execdir/-ok/-okdir/-fprint/-fls/-fprintf
    /// </summary>
    internal static bool CheckFindDangerous(string _, IReadOnlyList<string> args) {
        var dangerousOps = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "-delete", "-exec", "-execdir", "-ok", "-okdir",
            "-fprint", "-fprint0", "-fls", "-fprintf");

        foreach (var arg in args) {
            if (dangerousOps.Contains(arg)) {
                return true;
            }
        }

        return false;
    }

    internal static FrozenDictionary<string, FlagArgType> BuildChecksumSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-b"] = FlagArgType.None,
            ["--binary"] = FlagArgType.None,
            ["-t"] = FlagArgType.None,
            ["--text"] = FlagArgType.None,
            ["-c"] = FlagArgType.None,
            ["--check"] = FlagArgType.None,
            ["--tag"] = FlagArgType.None,
            ["-q"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
            ["--status"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["--warn"] = FlagArgType.None,
            ["--strict"] = FlagArgType.None,
            ["-z"] = FlagArgType.None,
            ["--zero"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildTreeSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-a"] = FlagArgType.None,
            ["--all"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["--dirsfirst"] = FlagArgType.None,
            ["-f"] = FlagArgType.None,
            ["--fullpath"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--noreport"] = FlagArgType.None,
            ["-l"] = FlagArgType.Required,
            ["--level"] = FlagArgType.Required,
            ["-p"] = FlagArgType.None,
            ["--prune"] = FlagArgType.None,
            ["-q"] = FlagArgType.None,
            ["--quote"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--size"] = FlagArgType.None,
            ["-u"] = FlagArgType.None,
            ["--user"] = FlagArgType.None,
            ["-g"] = FlagArgType.None,
            ["--group"] = FlagArgType.None,
            ["-D"] = FlagArgType.None,
            ["--date"] = FlagArgType.None,
            ["-F"] = FlagArgType.None,
            ["--type"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            ["--charset"] = FlagArgType.Required,
            ["--filelimit"] = FlagArgType.Required,
            ["--si"] = FlagArgType.None,
            ["--inodes"] = FlagArgType.None,
            ["--device"] = FlagArgType.None,
            ["--noreport"] = FlagArgType.None,
            ["--dirsfirst"] = FlagArgType.None,
            ["--sort"] = FlagArgType.Required,
            // 故意排除 -R（递归时写 00Tree.html）和 -o/--output
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildDateSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-d"] = FlagArgType.Required,
            ["--date"] = FlagArgType.Required,
            ["-f"] = FlagArgType.Required,
            ["--file"] = FlagArgType.Required,
            ["-I"] = FlagArgType.Optional,
            ["--iso-8601"] = FlagArgType.Optional,
            ["-R"] = FlagArgType.None,
            ["--rfc-email"] = FlagArgType.None,
            ["--rfc-3339"] = FlagArgType.Optional,
            ["-r"] = FlagArgType.Required,
            ["--reference"] = FlagArgType.Required,
            ["-s"] = FlagArgType.Required,
            ["--set"] = FlagArgType.Required,
            ["-u"] = FlagArgType.None,
            ["--utc"] = FlagArgType.None,
            ["--universal"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static bool CheckDateDangerous(string _, IReadOnlyList<string> args) {
        // 位置参数必须以 + 开头（格式字符串），否则可能是设置系统时间的 MMDDhhmm 格式
        return args.Any(a => !a.StartsWith('-') && !a.StartsWith('+'));
    }

    internal static FrozenDictionary<string, FlagArgType> BuildHostnameSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-a"] = FlagArgType.None,
            ["--alias"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["--domain"] = FlagArgType.None,
            ["-f"] = FlagArgType.None,
            ["--fqdn"] = FlagArgType.None,
            ["-A"] = FlagArgType.None,
            ["--all-fqdns"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--ip-address"] = FlagArgType.None,
            ["-I"] = FlagArgType.None,
            ["--all-ip-addresses"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--short"] = FlagArgType.None,
            ["-y"] = FlagArgType.None,
            ["--yp"] = FlagArgType.None,
            ["-F"] = FlagArgType.Required,
            ["--file"] = FlagArgType.Required,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildLsofSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-a"] = FlagArgType.None,
            ["-b"] = FlagArgType.None,
            ["-c"] = FlagArgType.Required,
            ["-d"] = FlagArgType.Required,
            ["-g"] = FlagArgType.Optional,
            ["-i"] = FlagArgType.Optional,
            ["-l"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["-P"] = FlagArgType.None,
            ["-r"] = FlagArgType.Optional,
            ["-R"] = FlagArgType.None,
            ["-s"] = FlagArgType.Optional,
            ["-S"] = FlagArgType.Required,
            ["-t"] = FlagArgType.None,
            ["-T"] = FlagArgType.Optional,
            ["-u"] = FlagArgType.Required,
            ["-U"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["-V"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["-X"] = FlagArgType.None,
            ["-Z"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            ["+c"] = FlagArgType.Required,
            ["+d"] = FlagArgType.Required,
            ["+D"] = FlagArgType.Required,
            ["+f"] = FlagArgType.None,
            ["+L"] = FlagArgType.Optional,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static bool CheckLsofDangerous(string _, IReadOnlyList<string> args) =>
        args.Any(a => a.StartsWith("+m", StringComparison.Ordinal));

    internal static FrozenDictionary<string, FlagArgType> BuildPgrepSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-d"] = FlagArgType.Required,
            ["--delimiter"] = FlagArgType.Required,
            ["-f"] = FlagArgType.None,
            ["--full"] = FlagArgType.None,
            ["-g"] = FlagArgType.Required,
            ["--pgroup"] = FlagArgType.Required,
            ["-i"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--list-name"] = FlagArgType.None,
            ["-a"] = FlagArgType.None,
            ["--list-full"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--newest"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["--oldest"] = FlagArgType.None,
            ["-P"] = FlagArgType.Required,
            ["--parent"] = FlagArgType.Required,
            ["-q"] = FlagArgType.None,
            ["--queue"] = FlagArgType.Required,
            ["-r"] = FlagArgType.None,
            ["--runstates"] = FlagArgType.Required,
            ["-s"] = FlagArgType.Required,
            ["--session"] = FlagArgType.Required,
            ["-t"] = FlagArgType.Required,
            ["--terminal"] = FlagArgType.Required,
            ["-u"] = FlagArgType.Required,
            ["--euid"] = FlagArgType.Required,
            ["-U"] = FlagArgType.Required,
            ["--uid"] = FlagArgType.Required,
            ["-v"] = FlagArgType.None,
            ["--inverse"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["--lightweight"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["--exact"] = FlagArgType.None,
            ["-F"] = FlagArgType.Required,
            ["--pidfile"] = FlagArgType.Required,
            ["-L"] = FlagArgType.Required,
            ["--logpidfile"] = FlagArgType.Required,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static bool CheckTputDangerous(string command, IReadOnlyList<string> _) {
        // 阻止 -S 标志（从 stdin 读取能力名）
        if (command.Contains("-S", StringComparison.Ordinal)) {
            return true;
        }

        // 阻止危险能力名
        var dangerousCaps = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "init", "reset", "rs1", "rs2", "rs3", "is1", "is2", "is3",
            "iprog", "if", "rf", "clear", "flash", "mc0", "mc4", "mc5",
            "mc5i", "mc5p", "pfkey", "pfloc", "pfx", "pfxl",
            "smcup", "rmcup");

        var spaceIdx = command.IndexOf(' ');
        if (spaceIdx < 0) return false;

        var capName = command[(spaceIdx + 1)..].Trim();
        return dangerousCaps.Contains(capName);
    }

    /// <summary>
    /// sed 危险回调 — 对齐 TS sedValidation.ts 双层防御
    /// 调用 SedValidation 检查 w/W(写文件) 和 e/E(执行命令) 危险操作
    /// </summary>
    internal static bool CheckSedDangerous(string command, IReadOnlyList<string> _) {
        return !SedValidation.IsSedCommandSafe(command);
    }

    internal static FrozenDictionary<string, FlagArgType> BuildSsSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-a"] = FlagArgType.None,
            ["--all"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--listening"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--numeric"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--resolve"] = FlagArgType.None,
            ["-e"] = FlagArgType.None,
            ["--extended"] = FlagArgType.None,
            ["-m"] = FlagArgType.None,
            ["--memory"] = FlagArgType.None,
            ["-p"] = FlagArgType.None,
            ["--processes"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--info"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--summary"] = FlagArgType.None,
            ["-t"] = FlagArgType.None,
            ["--tcp"] = FlagArgType.None,
            ["-u"] = FlagArgType.None,
            ["--udp"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["--raw"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["--unix"] = FlagArgType.None,
            ["-4"] = FlagArgType.None,
            ["--ipv4"] = FlagArgType.None,
            ["-6"] = FlagArgType.None,
            ["--ipv6"] = FlagArgType.None,
            ["-0"] = FlagArgType.None,
            ["--packet"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["--options"] = FlagArgType.None,
            ["-b"] = FlagArgType.None,
            ["--bpf"] = FlagArgType.None,
            ["-E"] = FlagArgType.None,
            ["--events"] = FlagArgType.None,
            ["-Z"] = FlagArgType.None,
            ["--context"] = FlagArgType.None,
            ["-z"] = FlagArgType.None,
            ["--contexts"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            // 故意排除 -K/--kill, -D/--diag, -F/--filter, -N/--net
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildFdSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-H"] = FlagArgType.None,
            ["--hidden"] = FlagArgType.None,
            ["-I"] = FlagArgType.None,
            ["--no-ignore"] = FlagArgType.None,
            ["-u"] = FlagArgType.None,
            ["--unrestricted"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--case-sensitive"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
            ["-g"] = FlagArgType.Required,
            ["--glob"] = FlagArgType.Required,
            ["-e"] = FlagArgType.Required,
            ["--extension"] = FlagArgType.Required,
            ["-t"] = FlagArgType.Required,
            ["--type"] = FlagArgType.Required,
            ["-d"] = FlagArgType.Required,
            ["--max-depth"] = FlagArgType.Required,
            ["--min-depth"] = FlagArgType.Required,
            ["--exact-depth"] = FlagArgType.Required,
            ["-S"] = FlagArgType.Required,
            ["--size"] = FlagArgType.Required,
            ["--changed-within"] = FlagArgType.Required,
            ["--changed-before"] = FlagArgType.Required,
            ["-o"] = FlagArgType.Required,
            ["--owner"] = FlagArgType.Required,
            ["--exclude"] = FlagArgType.Required,
            ["--ignore-file"] = FlagArgType.Required,
            ["-j"] = FlagArgType.Required,
            ["--threads"] = FlagArgType.Required,
            ["-n"] = FlagArgType.Required,
            ["--max-results"] = FlagArgType.Required,
            ["-F"] = FlagArgType.Required,
            ["--fixed-strings"] = FlagArgType.Required,
            ["-p"] = FlagArgType.Required,
            ["--full-path"] = FlagArgType.Required,
            ["--format"] = FlagArgType.Required,
            ["-l"] = FlagArgType.Required,
            ["--absolute-path"] = FlagArgType.None,
            ["--follow"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            // 故意排除 -x/--exec, -X/--exec-batch, -l/--list-details
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildXargsSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-I"] = FlagArgType.Required,
            ["-n"] = FlagArgType.Required,
            ["-P"] = FlagArgType.Required,
            ["-L"] = FlagArgType.Required,
            ["-s"] = FlagArgType.Required,
            ["-E"] = FlagArgType.Required,
            ["-0"] = FlagArgType.None,
            ["-t"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["-x"] = FlagArgType.None,
            ["-d"] = FlagArgType.Required,
            // 故意排除 -i/-e（GNU getopt 可选参数语义问题）
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FrozenDictionary<string, FlagArgType> BuildSedSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["-n"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
            ["--silent"] = FlagArgType.None,
            ["-e"] = FlagArgType.Required,
            ["--expression"] = FlagArgType.Required,
            ["-f"] = FlagArgType.Required,
            ["--file"] = FlagArgType.Required,
            ["-i"] = FlagArgType.Optional,
            ["--in-place"] = FlagArgType.Optional,
            ["-E"] = FlagArgType.None,
            ["--regexp-extended"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--posix"] = FlagArgType.None,
            ["-l"] = FlagArgType.Required,
            ["--line-length"] = FlagArgType.Required,
            ["--sandbox"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--separate"] = FlagArgType.None,
            ["-u"] = FlagArgType.None,
            ["--unbuffered"] = FlagArgType.None,
            ["-z"] = FlagArgType.None,
            ["--null-data"] = FlagArgType.None,
            ["--help"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// docker logs 安全标志 — 对齐 TS DOCKER_READ_ONLY_COMMANDS["docker logs"]
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildDockerLogsSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["--follow"] = FlagArgType.None,
            ["-f"] = FlagArgType.None,
            ["--tail"] = FlagArgType.Required,
            ["-n"] = FlagArgType.Required,
            ["--timestamps"] = FlagArgType.None,
            ["-t"] = FlagArgType.None,
            ["--since"] = FlagArgType.Required,
            ["--until"] = FlagArgType.Required,
            ["--details"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// docker inspect 安全标志 — 对齐 TS DOCKER_READ_ONLY_COMMANDS["docker inspect"]
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildDockerInspectSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["--format"] = FlagArgType.Required,
            ["-f"] = FlagArgType.Required,
            ["--type"] = FlagArgType.Required,
            ["--size"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// docker ps 安全标志 — 对齐 TS EXTERNAL_READONLY_COMMANDS["docker ps"]
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildDockerPsSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["--all"] = FlagArgType.None,
            ["-a"] = FlagArgType.None,
            ["--filter"] = FlagArgType.Required,
            ["-f"] = FlagArgType.Required,
            ["--format"] = FlagArgType.Required,
            ["--last"] = FlagArgType.Required,
            ["-n"] = FlagArgType.Required,
            ["--latest"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--no-trunc"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
            ["-q"] = FlagArgType.None,
            ["--size"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// docker images 安全标志 — 对齐 TS EXTERNAL_READONLY_COMMANDS["docker images"]
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildDockerImagesSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["--all"] = FlagArgType.None,
            ["-a"] = FlagArgType.None,
            ["--filter"] = FlagArgType.Required,
            ["-f"] = FlagArgType.Required,
            ["--format"] = FlagArgType.Required,
            ["--no-trunc"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
            ["-q"] = FlagArgType.None,
            ["--digests"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// pyright 安全标志 — 对齐 TS PYRIGHT_READ_ONLY_COMMANDS
    /// 注意: pyright 不尊重 -- 约定（RespectsDoubleDash=false）
    /// </summary>
    internal static FrozenDictionary<string, FlagArgType> BuildPyrightSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase) {
            ["--outputjson"] = FlagArgType.None,
            ["--project"] = FlagArgType.Required,
            ["-p"] = FlagArgType.Required,
            ["--pythonversion"] = FlagArgType.Required,
            ["--typeshedpath"] = FlagArgType.Required,
            ["--venvpath"] = FlagArgType.Required,
            ["--level"] = FlagArgType.Required,
            ["--stats"] = FlagArgType.None,
            ["--verbose"] = FlagArgType.None,
            ["--version"] = FlagArgType.None,
            ["--dependencies"] = FlagArgType.None,
            ["--warnings"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// pyright 危险标志检查 — 阻止 --watch/-w（持续监听模式）
    /// 对齐 TS PYRIGHT_READ_ONLY_COMMANDS additionalCommandIsDangerousCallback
    /// </summary>
    internal static bool CheckPyrightDangerous(string command, IReadOnlyList<string> args) {
        for (var i = 0; i < args.Count; i++) {
            var arg = args[i];
            if (string.Equals(arg, "--watch", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-w", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
