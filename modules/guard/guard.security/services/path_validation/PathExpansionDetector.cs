namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径展开与命令解析检测器 — 从 PathConstraintValidator 拆分而来。
/// 职责：各命令的路径提取器、安全包装命令剥离、命令 token 解析、tilde 展开、路径解析。
/// 对齐 TS PATH_EXTRACTORS + stripSafeWrappers + parsePatternCommand + expandTilde。
/// </summary>
internal static class PathExpansionDetector {

    /// <summary>
    /// 安全包装命令集合 — 委托 BashSecurityConstants.SafeWrapperCommands 保持单数据源
    /// </summary>
    internal static readonly FrozenSet<string> SafeWrapperCommands = BashSecurityConstants.SafeWrapperCommands;

    /// <summary>
    /// 提取命令中的路径 — 对齐 TS PATH_EXTRACTORS[command]
    /// </summary>
    internal static IReadOnlyList<string> ExtractPaths(
        PathCommand command, IReadOnlyList<string> args, string workingDirectory) {
        return command switch {
            PathCommand.Cd => ExtractCdPaths(args),
            PathCommand.Ls => FilterOutFlags(args, defaultPaths: ["."]),
            PathCommand.Find => ExtractFindPaths(args),
            PathCommand.Grep => ExtractGrepPaths(args),
            PathCommand.Rg => ExtractRgPaths(args),
            PathCommand.Sed => ExtractSedPaths(args),
            PathCommand.Jq => ExtractJqPaths(args),
            PathCommand.Git => ExtractGitPaths(args),
            PathCommand.Tr => ExtractTrPaths(args),
            // 大多数命令直接使用 FilterOutFlags
            _ => FilterOutFlags(args),
        };
    }

    /// <summary>
    /// cd 路径提取 — 对齐 TS: 所有参数拼接为单个路径，无参数则返回 home
    /// </summary>
    internal static IReadOnlyList<string> ExtractCdPaths(IReadOnlyList<string> args) {
        if (args.Count == 0) {
            return [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)];
        }

        // cd 的所有参数拼接为单个路径
        return [string.Join(" ", args)];
    }

    /// <summary>
    /// find 路径提取 — 对齐 TS: 收集路径直到遇到非全局标志
    /// </summary>
    internal static IReadOnlyList<string> ExtractFindPaths(IReadOnlyList<string> args) {
        var paths = new List<string>();
        var i = 0;

        while (i < args.Count) {
            var arg = args[i];

            // -- 定界符后全是路径
            if (arg == "--") {
                i++;
                while (i < args.Count) {
                    paths.Add(args[i]);
                    i++;
                }

                break;
            }

            // 以 - 开头的是标志（find 的标志如 -name, -type 等）
            if (arg.StartsWith('-')) {
                break;
            }

            paths.Add(arg);
            i++;
        }

        return paths.Count > 0 ? paths : ["."];
    }

    /// <summary>
    /// grep 路径提取 — 对齐 TS parsePatternCommand
    /// </summary>
    internal static IReadOnlyList<string> ExtractGrepPaths(IReadOnlyList<string> args) {
        var grepFlagsWithArgs = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "-e", "--regexp", "-f", "--file", "--include", "--exclude",
            "--exclude-from", "--exclude-dir", "--color");

        var defaults = args.Any(a => a is "-r" or "-R" or "--recursive") ? (IReadOnlyList<string>)["."] : [];

        return ParsePatternCommand(args, grepFlagsWithArgs, defaults);
    }

    /// <summary>
    /// rg 路径提取 — 对齐 TS parsePatternCommand
    /// </summary>
    internal static IReadOnlyList<string> ExtractRgPaths(IReadOnlyList<string> args) {
        var rgFlagsWithArgs = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "-e", "--regexp", "-f", "--file", "-g", "--glob",
            "--iglob", "--type-add", "--type-not", "--color",
            "--max-columns", "--max-count", "--max-depth",
            "--max-filesize", "--mmap", "--sort", "--sort-path");

        return ParsePatternCommand(args, rgFlagsWithArgs, ["."]);
    }

    /// <summary>
    /// sed 路径提取 — 对齐 TS: 处理 -e/-f 标志，支持 -- 定界符
    /// </summary>
    internal static IReadOnlyList<string> ExtractSedPaths(IReadOnlyList<string> args) {
        var paths = new List<string>();
        var pastFlags = false;
        var i = 0;

        while (i < args.Count) {
            var arg = args[i];

            if (arg == "--") {
                pastFlags = true;
                i++;
                continue;
            }

            if (!pastFlags) {
                if (arg is "-e" or "--expression") {
                    i += 2; // 跳过标志和值
                    continue;
                }

                if (arg is "-f" or "--file") {
                    i += 2; // 跳过标志和值
                    continue;
                }

                if (arg.StartsWith('-')) {
                    i++;
                    continue;
                }

                // 第一个非标志参数是脚本，跳过
                pastFlags = true;
                i++;
                continue;
            }

            paths.Add(arg);
            i++;
        }

        return paths;
    }

    /// <summary>
    /// jq 路径提取 — 对齐 TS: filter 后跟文件路径
    /// </summary>
    internal static IReadOnlyList<string> ExtractJqPaths(IReadOnlyList<string> args) {
        var jqFlagsWithArgs = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "-f", "--from-file", "-L", "--arg", "--argjson",
            "--slurpfile", "--rawfile", "--args", "--jsonargs");

        return ParsePatternCommand(args, jqFlagsWithArgs, []);
    }

    /// <summary>
    /// git 路径提取 — 对齐 TS: 仅处理 git diff --no-index
    /// </summary>
    internal static IReadOnlyList<string> ExtractGitPaths(IReadOnlyList<string> args) {
        if (args.Count > 0
            && args[0].Equals("diff", StringComparison.OrdinalIgnoreCase)
            && args.Any(a => a.Equals("--no-index", StringComparison.OrdinalIgnoreCase))) {
            // git diff --no-index: 提取前2个非标志路径
            var paths = FilterOutFlags(SliceFrom(args, 1));
            return paths.Take(2).ToList();
        }

        // 其他 git 命令不做路径约束
        return [];
    }

    /// <summary>
    /// tr 路径提取 — 对齐 TS: 跳过字符集
    /// </summary>
    internal static IReadOnlyList<string> ExtractTrPaths(IReadOnlyList<string> args) {
        // tr 命令: tr [选项] 字符集1 [字符集2] — 通常从 stdin 读取，无文件路径
        // 仅当有 -d 标志时跳1个字符集，否则跳2个
        var hasDelete = args.Any(a => a is "-d" or "--delete");
        var skipCount = hasDelete ? 1 : 2;

        var nonFlagArgs = args.Where(a => !a.StartsWith('-')).Skip(skipCount).ToList();
        return nonFlagArgs;
    }

    /// <summary>
    /// 过滤标志参数，保留位置参数 — 对齐 TS filterOutFlags
    /// 正确处理 POSIX -- 端标志定界符
    /// </summary>
    internal static IReadOnlyList<string> FilterOutFlags(
        IReadOnlyList<string> args, IReadOnlyList<string>? defaultPaths = null) {
        var positional = new List<string>();
        var pastDelimiter = false;

        foreach (var arg in args) {
            if (pastDelimiter) {
                positional.Add(arg);
                continue;
            }

            if (arg == "--") {
                pastDelimiter = true;
                continue;
            }

            if (!arg.StartsWith('-')) {
                positional.Add(arg);
            }
        }

        return positional.Count > 0 ? positional : defaultPaths ?? [];
    }

    /// <summary>
    /// 解析 grep/rg 风格命令 — 对齐 TS parsePatternCommand
    /// </summary>
    internal static IReadOnlyList<string> ParsePatternCommand(
        IReadOnlyList<string> args,
        FrozenSet<string> flagsWithArgs,
        IReadOnlyList<string> defaults) {
        var paths = new List<string>();
        var pastDelimiter = false;
        var pastPattern = false;
        var i = 0;

        while (i < args.Count) {
            var arg = args[i];

            if (pastDelimiter) {
                paths.Add(arg);
                i++;
                continue;
            }

            if (arg == "--") {
                pastDelimiter = true;
                i++;
                continue;
            }

            // 跳过带参数的标志
            if (i + 1 < args.Count && flagsWithArgs.Contains(arg)) {
                i += 2;
                continue;
            }

            // 跳过标志
            if (arg.StartsWith('-')) {
                i++;
                continue;
            }

            // 第一个非标志参数是 pattern，跳过
            if (!pastPattern) {
                pastPattern = true;
                i++;
                continue;
            }

            paths.Add(arg);
            i++;
        }

        return paths.Count > 0 ? paths : defaults;
    }

    /// <summary>
    /// 剥离安全包装命令 — 对齐 TS stripSafeWrappers / stripWrappersFromArgv
    /// </summary>
    internal static (string Command, IReadOnlyList<string> Args) StripSafeWrappers(
        string command, IReadOnlyList<string> args) {
        var currentCmd = command;
        var currentArgs = args;
        var offset = 0;

        // 循环剥离包装命令 — 用 offset 索引替代 Skip+ToList 消除循环内 O(n) 拷贝
        while (offset < currentArgs.Count && SafeWrapperCommands.Contains(currentCmd)) {
            switch (currentCmd.ToLowerInvariant()) {
                case "time":
                case "nohup":
                // 直接剥离，支持 -- 定界符
                if (offset < currentArgs.Count && currentArgs[offset] == "--") {
                    offset++;
                }

                if (offset < currentArgs.Count) {
                    currentCmd = currentArgs[offset];
                    offset++;
                }

                break;

                case "timeout":
                // 跳过 timeout 的 GNU 标志，找到 duration 参数后的命令
                var timeoutIdx = SkipTimeoutFlags(currentArgs, offset);
                if (timeoutIdx >= 0 && timeoutIdx + 1 < currentArgs.Count) {
                    currentCmd = currentArgs[timeoutIdx + 1];
                    offset = timeoutIdx + 2;
                } else {
                    // 无法解析，返回原始
                    return (currentCmd, SliceFrom(currentArgs, offset));
                }

                break;

                case "nice":
                // nice cmd / nice -N cmd / nice -n N cmd
                var niceIdx = 0;
                var remaining = currentArgs.Count - offset;
                if (remaining > 0 && currentArgs[offset].StartsWith("-")
                    && !currentArgs[offset].Equals("--", StringComparison.Ordinal)) {
                    if (currentArgs[offset] == "-n" && remaining > 1) {
                        niceIdx = 2;
                    } else {
                        niceIdx = 1;
                    }
                }

                if (niceIdx + 1 <= remaining && niceIdx < remaining) {
                    currentCmd = currentArgs[offset + niceIdx];
                    offset += niceIdx + 1;
                } else {
                    return (currentCmd, SliceFrom(currentArgs, offset));
                }

                break;

                case "stdbuf":
                // 跳过 -i/-o/-e 标志
                var stdbufIdx = SkipStdbufFlags(currentArgs, offset);
                if (stdbufIdx < currentArgs.Count) {
                    currentCmd = currentArgs[stdbufIdx];
                    offset = stdbufIdx + 1;
                } else {
                    return (currentCmd, SliceFrom(currentArgs, offset));
                }

                break;

                case "env":
                // 跳过 VAR=val 和安全标志
                var envIdx = SkipEnvFlags(currentArgs, offset);
                if (envIdx < currentArgs.Count) {
                    currentCmd = currentArgs[envIdx];
                    offset = envIdx + 1;
                } else {
                    return (currentCmd, SliceFrom(currentArgs, offset));
                }

                break;

                default:
                return (currentCmd, SliceFrom(currentArgs, offset));
            }
        }

        return (currentCmd, SliceFrom(currentArgs, offset));
    }

    /// <summary>
    /// 从指定位置切片返回不可变列表 — 消除 Skip+ToList 拷贝
    /// </summary>
    internal static IReadOnlyList<string> SliceFrom(IReadOnlyList<string> list, int start) {
        if (start == 0) return list;
        if (start >= list.Count) return Array.Empty<string>();
        var result = new string[list.Count - start];
        for (var i = 0; i < result.Length; i++) {
            result[i] = list[start + i];
        }
        return result;
    }

    /// <summary>
    /// 跳过 timeout 的 GNU 标志 — 对齐 TS skipTimeoutFlags
    /// </summary>
    internal static int SkipTimeoutFlags(IReadOnlyList<string> args, int start) {
        var i = start;
        while (i < args.Count) {
            var arg = args[i];

            if (arg == "--foreground") {
                i++;
                continue;
            }

            if (arg is "--kill-after" or "-k" or "--signal" or "-s" or "-v") {
                i += 2; // 标志 + 值
                continue;
            }

            // duration 参数: 数字+[smhd]?
            if (Regex.IsMatch(arg, @"^\d+(?:\.\d+)?[smhd]?$")) {
                return i;
            }

            // 未知标志，无法解析
            return -1;
        }

        return -1;
    }

    /// <summary>
    /// 跳过 stdbuf 的 -i/-o/-e 标志 — 对齐 TS skipStdbufFlags
    /// </summary>
    internal static int SkipStdbufFlags(IReadOnlyList<string> args, int start) {
        var i = start;
        while (i < args.Count) {
            var arg = args[i];

            // -iVAL, -oVAL, -eVAL (融合选项)
            if (arg.Length >= 3 && arg[0] == '-'
                && (arg[1] is 'i' or 'o' or 'e')) {
                i++;
                continue;
            }

            // --input=VAL, --output=VAL, --error=VAL (长选项)
            if (arg.StartsWith("--input=", StringComparison.Ordinal)
                || arg.StartsWith("--output=", StringComparison.Ordinal)
                || arg.StartsWith("--error=", StringComparison.Ordinal)) {
                i++;
                continue;
            }

            // -i VAL, -o VAL, -e VAL (短选项+空格)
            if (arg is "-i" or "-o" or "-e" && i + 1 < args.Count) {
                i += 2;
                continue;
            }

            // 非标志，这是命令开始
            break;
        }

        return i;
    }

    /// <summary>
    /// 跳过 env 的 VAR=val 和安全标志 — 对齐 TS skipEnvFlags
    /// </summary>
    internal static int SkipEnvFlags(IReadOnlyList<string> args, int start) {
        var i = start;
        while (i < args.Count) {
            var arg = args[i];

            // VAR=val 形式
            if (!arg.StartsWith('-') && arg.Contains('=')) {
                i++;
                continue;
            }

            // 安全标志
            if (arg is "-i" or "-0" or "-v" or "-u") {
                i += arg is "-u" ? 2 : 1;
                continue;
            }

            // 拒绝危险标志
            if (arg is "-S" or "-C" or "-P") {
                return args.Count; // fail-closed
            }

            // 非标志，命令开始
            break;
        }

        return i;
    }

    /// <summary>
    /// 展开 tilde — 对齐 TS expandTilde
    /// </summary>
    internal static string ExpandTilde(string path) {
        if (string.IsNullOrEmpty(path)) {
            return path;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal)) {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home + path[1..];
        }

        if (path == "~") {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return path;
    }

    /// <summary>
    /// 解析为绝对路径（不解析符号链接）— 对齐 TS resolve(path, cwd)
    /// </summary>
    internal static string ResolvePath(string path, string workingDirectory) {
        if (string.IsNullOrEmpty(path)) {
            return workingDirectory;
        }

        // 去除引号
        path = path.Trim('"', '\'');

        if (Path.IsPathRooted(path)) {
            return Path.GetFullPath(path);
        }

        try {
            return Path.GetFullPath(Path.Combine(workingDirectory, path));
        } catch {
            return path;
        }
    }

    /// <summary>
    /// 解析命令部分 — 提取命令名和参数
    /// </summary>
    internal static (string CommandName, IReadOnlyList<string> Arguments) ParseCommandParts(
        string command) {
        var parts = SplitCommandTokens(command);
        if (parts.Count == 0) {
            return (string.Empty, Array.Empty<string>());
        }

        return (parts[0], SliceFrom(parts, 1));
    }

    /// <summary>
    /// 分割命令为 token — 对齐 TS tryParseShellCommand
    /// </summary>
    internal static List<string> SplitCommandTokens(string command) {
        var parts = new List<string>();
        var current = new StringBuilder();
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

            // 遇到管道/分号/&& 结束当前命令
            if (!inQuotes && (c == '|' || c == ';' || c == '&')) {
                if (current.Length > 0) {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                break;
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
}
