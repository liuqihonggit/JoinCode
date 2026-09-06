namespace JoinCode.CliCommands;

/// <summary>
/// ripgrep 兼容搜索子命令 — jcc rg &lt;pattern&gt; [path...]
/// <para>ADR: 0070 — 内置 rg 实现，复用 ISearchService.GrepSearchAsync（并行 + .gitignore + 二进制检测 + SIMD 正则）。</para>
/// <para>宽容策略：自动修复 PowerShell 双反斜杠转义、缺少路径默认 cwd 但禁止根目录扫盘、超时硬终止。</para>
/// </summary>
internal static class RgSubCommand
{
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 300;

    /// <summary>
    /// 执行 rg 子命令。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var parsed = ParseArgs(args);
        if (parsed is null)
            return 1;

        var pattern = FixPowerShellEscaping(parsed.Pattern);
        if (parsed.FixedStrings)
            pattern = Regex.Escape(pattern);

        if (string.IsNullOrEmpty(pattern))
        {
            TerminalHelper.WriteError("错误: pattern 为空");
            return 1;
        }

        foreach (var p in parsed.Paths)
        {
            if (IsRootOrUnsafePath(p))
            {
                TerminalHelper.WriteError($"拒绝在根目录或无效路径上扫盘: {p}。请指定具体子目录（如 . 或 src/）。");
                return 1;
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(parsed.TimeoutSeconds));
        var token = timeoutCts.Token;

        try
        {
            return await McpCliCommand.WithHostAsync(async services =>
            {
                var searchService = services.GetRequiredService<ISearchService>();
                var fileOp = services.GetRequiredService<IFileOperationService>();

                var searchPaths = new List<string>(parsed.Paths.Count);
                foreach (var p in parsed.Paths)
                {
                    var resolved = ResolveSearchPath(p, fileOp);
                    if (resolved is null)
                    {
                        TerminalHelper.WriteError($"路径不存在: {p}（当前目录: {fileOp.GetCurrentDirectory()}）");
                        return 1;
                    }
                    searchPaths.Add(resolved);
                }

                var mergedFilenames = new List<string>();
                var mergedContent = new StringBuilder();
                var mergedNumMatches = 0;
                var mergedNumFiles = 0;
                var anyFailure = false;
                string? firstError = null;

                foreach (var searchPath in searchPaths)
                {
                    var input = new GrepSearchInput
                    {
                        Pattern = pattern,
                        Path = searchPath,
                        Glob = parsed.Glob,
                        OutputMode = parsed.OutputMode,
                        CaseInsensitive = parsed.CaseInsensitive,
                        FileType = parsed.FileType,
                        Multiline = parsed.Multiline,
                        Before = parsed.Before,
                        After = parsed.After,
                        Context = parsed.Context,
                        LineNumbers = parsed.LineNumbers,
                        HeadLimit = parsed.HeadLimit,
                        Offset = parsed.Offset,
                    };

                    GrepSearchResult result;
                    try
                    {
                        result = await searchService.GrepSearchAsync(input, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        TerminalHelper.WriteError($"搜索超时（{parsed.TimeoutSeconds}s）。请缩小搜索范围、用 --type/-g 过滤，或增加 --timeout。");
                        return 2;
                    }

                    if (!result.Success)
                    {
                        anyFailure = true;
                        firstError ??= result.ErrorMessage ?? "搜索失败";
                        continue;
                    }

                    mergedFilenames.AddRange(result.Filenames);
                    mergedNumFiles += result.NumFiles;
                    if (result.NumMatches.HasValue)
                        mergedNumMatches += result.NumMatches.Value;
                    if (!string.IsNullOrEmpty(result.Content))
                    {
                        if (mergedContent.Length > 0)
                            mergedContent.AppendLine();
                        mergedContent.Append(result.Content);
                    }
                }

                if (anyFailure && mergedNumFiles == 0)
                {
                    TerminalHelper.WriteError(firstError ?? "搜索失败");
                    return 1;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var dedupedFilenames = new List<string>(mergedFilenames.Count);
                foreach (var f in mergedFilenames)
                {
                    if (seen.Add(f))
                        dedupedFilenames.Add(f);
                }

                var mergedResult = GrepSearchResult.SuccessResult(
                    parsed.OutputMode.ToValue(),
                    dedupedFilenames,
                    mergedContent.Length > 0 ? mergedContent.ToString() : null,
                    null,
                    parsed.OutputMode == SearchOutputMode.Count ? mergedNumMatches : null);

                return OutputResult(mergedResult, parsed, fileOp);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TerminalHelper.WriteError($"搜索超时（{parsed.TimeoutSeconds}s）— 已硬终止。");
            return 2;
        }
    }

    /// <summary>
    /// 解析 rg 风格参数。
    /// </summary>
    private static RgOptions? ParseArgs(string[] args)
    {
        string? pattern = null;
        var paths = new List<string>();
        var caseInsensitive = false;
        var lineNumbers = false;
        var multiline = false;
        var fixedStrings = false;
        var json = false;
        string? glob = null;
        string? fileType = null;
        int? before = null;
        int? after = null;
        int? context = null;
        int? headLimit = null;
        int? offset = null;
        var timeoutSeconds = DefaultTimeoutSeconds;
        var outputMode = SearchOutputMode.Files;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Length == 0)
                continue;

            if (arg[0] != '-')
            {
                if (pattern is null)
                    pattern = arg;
                else
                    paths.Add(arg);
                continue;
            }

            if (arg == "--help" || arg == "-h")
            {
                PrintUsage();
                return null;
            }

            if (arg.StartsWith("--"))
            {
                var eqIdx = arg.IndexOf('=');
                var name = eqIdx > 0 ? arg[..eqIdx] : arg;
                var inlineValue = eqIdx > 0 ? arg[(eqIdx + 1)..] : null;

                switch (name)
                {
                    case "--ignore-case": caseInsensitive = true; break;
                    case "--line-number": lineNumbers = true; break;
                    case "--no-line-number": lineNumbers = false; break;
                    case "--multiline":
                    case "--multiline-dotall": multiline = true; break;
                    case "--fixed-strings": fixedStrings = true; break;
                    case "--json": json = true; break;
                    case "--count": outputMode = SearchOutputMode.Count; break;
                    case "--files-with-matches": outputMode = SearchOutputMode.Files; break;
                    case "--content": outputMode = SearchOutputMode.Content; break;
                    case "--no-heading": break;
                    case "--type": fileType = inlineValue ?? ReadNextValue(args, ref i); break;
                    case "--glob": glob = inlineValue ?? ReadNextValue(args, ref i); break;
                    case "--head-limit": headLimit = ParseInt(inlineValue ?? ReadNextValue(args, ref i)); break;
                    case "--offset": offset = ParseInt(inlineValue ?? ReadNextValue(args, ref i)); break;
                    case "--timeout": timeoutSeconds = ClampTimeout(ParseInt(inlineValue ?? ReadNextValue(args, ref i))); break;
                    case "--before-context": before = ParseInt(inlineValue ?? ReadNextValue(args, ref i)); break;
                    case "--after-context": after = ParseInt(inlineValue ?? ReadNextValue(args, ref i)); break;
                    case "--context": context = ParseInt(inlineValue ?? ReadNextValue(args, ref i)); break;
                    default:
                        if (inlineValue is null)
                            ReadNextValue(args, ref i);
                        break;
                }
            }
            else
            {
                if (!ParseShortOptionCluster(arg, args, ref i,
                        ref caseInsensitive, ref lineNumbers, ref multiline, ref fixedStrings,
                        ref json, ref glob, ref fileType,
                        ref before, ref after, ref context, ref headLimit, ref offset,
                        ref timeoutSeconds, ref outputMode))
                {
                    return null;
                }
            }
        }

        if (pattern is null)
        {
            TerminalHelper.WriteError("错误: 缺少 pattern 参数。用法: jcc rg <pattern> [path...]");
            PrintUsage();
            return null;
        }

        var effectiveLineNumbers = lineNumbers || outputMode == SearchOutputMode.Content;

        return new RgOptions(
            Pattern: pattern,
            Paths: paths.Count > 0 ? paths : new List<string> { "." },
            Glob: glob,
            FileType: fileType,
            CaseInsensitive: caseInsensitive,
            LineNumbers: effectiveLineNumbers,
            Multiline: multiline,
            FixedStrings: fixedStrings,
            Json: json,
            Before: before,
            After: after,
            Context: context,
            HeadLimit: headLimit,
            Offset: offset,
            TimeoutSeconds: timeoutSeconds,
            OutputMode: outputMode);
    }

    private static string? ReadNextValue(string[] args, ref int i)
    {
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
        {
            i++;
            return args[i];
        }
        return null;
    }

    private static int ParseInt(string? value)
    {
        if (int.TryParse(value, out var n))
            return n;
        TerminalHelper.WriteError($"警告: 数值参数 '{value}' 不是有效整数，按 0 处理");
        return 0;
    }

    private static int ClampTimeout(int seconds)
    {
        if (seconds <= 0)
            return DefaultTimeoutSeconds;
        return Math.Min(seconds, MaxTimeoutSeconds);
    }

    /// <summary>
    /// 解析短参数簇，如 -in、-A2、-C 3。返回 false 表示遇到 -h 已打印用法应退出。
    /// </summary>
    private static bool ParseShortOptionCluster(
        string arg, string[] args, ref int i,
        ref bool caseInsensitive, ref bool lineNumbers, ref bool multiline, ref bool fixedStrings,
        ref bool json, ref string? glob, ref string? fileType,
        ref int? before, ref int? after, ref int? context, ref int? headLimit, ref int? offset,
        ref int timeoutSeconds, ref SearchOutputMode outputMode)
    {
        var span = arg.AsSpan(1);
        var j = 0;
        while (j < span.Length)
        {
            var c = span[j];
            switch (c)
            {
                case 'i': caseInsensitive = true; j++; break;
                case 'n': lineNumbers = true; j++; break;
                case 'U': multiline = true; j++; break;
                case 'F': fixedStrings = true; j++; break;
                case 'c': outputMode = SearchOutputMode.Count; j++; break;
                case 'l': outputMode = SearchOutputMode.Files; j++; break;
                case 'o': j++; break;
                case 'A': after = ConsumeShortNumber(span, ref j, args, ref i); break;
                case 'B': before = ConsumeShortNumber(span, ref j, args, ref i); break;
                case 'C': context = ConsumeShortNumber(span, ref j, args, ref i); break;
                case 'g': glob = ConsumeShortString(span, ref j, args, ref i); break;
                case 't': fileType = ConsumeShortString(span, ref j, args, ref i); break;
                case 'h': PrintUsage(); return false;
                default:
                    TerminalHelper.WriteError($"未知短参数: -{c}（在 {arg} 中）");
                    return false;
            }
        }
        return true;
    }

    private static int? ConsumeShortNumber(ReadOnlySpan<char> span, ref int j, string[] args, ref int i)
    {
        j++;
        if (j < span.Length)
        {
            var rest = span[j..];
            if (int.TryParse(rest, out var n))
            {
                j = span.Length;
                return n;
            }
        }
        var next = ReadNextValue(args, ref i);
        j = span.Length;
        return ParseInt(next);
    }

    private static string? ConsumeShortString(ReadOnlySpan<char> span, ref int j, string[] args, ref int i)
    {
        j++;
        if (j < span.Length)
        {
            var rest = span[j..].ToString();
            j = span.Length;
            return rest;
        }
        var next = ReadNextValue(args, ref i);
        j = span.Length;
        return next;
    }

    /// <summary>
    /// 宽容修复 PowerShell 双反斜杠转义。
    /// PowerShell 常把 \s 传成 \\s、\{ 传成 \\{ 等，导致 rg 正则解析失败。
    /// 检测 pattern 中的 \\X（X 为正则元字符）并修复为 \X。
    /// </summary>
    internal static string FixPowerShellEscaping(string pattern)
    {
        if (pattern.Length < 2 || !pattern.Contains('\\'))
            return pattern;

        var sb = new StringBuilder(pattern.Length);
        var span = pattern.AsSpan();
        var modified = false;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '\\' && i + 2 < span.Length && span[i + 1] == '\\' && IsRegexMetaChar(span[i + 2]))
            {
                sb.Append('\\');
                sb.Append(span[i + 2]);
                i += 2;
                modified = true;
            }
            else
            {
                sb.Append(span[i]);
            }
        }
        return modified ? sb.ToString() : pattern;
    }

    internal static bool IsRegexMetaChar(char c)
        => c is 's' or 'S' or 'd' or 'D' or 'w' or 'W' or 'b' or 'B'
            or '{' or '}' or '[' or ']' or '(' or ')' or '.' or '+' or '*' or '?'
            or '|' or '^' or '$' or 'n' or 'r' or 't' or 'f' or 'v' or '0' or 'x' or 'u' or 'c' or 'p' or 'P' or 'k' or 'A' or 'Z' or 'z' or 'G';

    /// <summary>
    /// 检查路径是否为根目录或不安全路径（禁止扫盘）。
    /// </summary>
    private static bool IsRootOrUnsafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "." || path == "./")
            return IsRootPath(Environment.CurrentDirectory);

        try
        {
            var fullPath = Path.GetFullPath(path);
            return IsRootPath(fullPath);
        }
        catch (Exception)
        {
            return true;
        }
    }

    internal static bool IsRootPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return false;
        if (fullPath.Length == 1 && fullPath[0] == '/')
            return true;
        if (fullPath.Length == 3 && char.IsLetter(fullPath[0]) && fullPath[1] == ':' && (fullPath[2] == '\\' || fullPath[2] == '/'))
            return true;
        if (fullPath.Length == 2 && char.IsLetter(fullPath[0]) && fullPath[1] == ':')
            return true;
        return false;
    }

    /// <summary>
    /// 解析搜索路径，返回 null 表示路径不存在。
    /// </summary>
    private static string? ResolveSearchPath(string? path, IFileOperationService fileOp)
    {
        if (string.IsNullOrWhiteSpace(path))
            return fileOp.GetCurrentDirectory();

        var fullPath = fileOp.GetFullPath(path);
        if (fileOp.DirectoryExists(fullPath) || fileOp.FileExists(fullPath))
            return fullPath;

        return null;
    }

    /// <summary>
    /// 格式化输出结果（rg 兼容风格）。
    /// </summary>
    private static int OutputResult(GrepSearchResult result, RgOptions opts, IFileOperationService fileOp)
    {
        if (result.NumFiles == 0)
        {
            if (opts.Json)
                System.Console.WriteLine("{\"matches\":[]}");
            return 1;
        }

        var cwd = fileOp.GetCurrentDirectory();

        if (opts.Json)
        {
            OutputJson(result, cwd);
            return 0;
        }

        var sb = new StringBuilder(256);
        switch (opts.OutputMode)
        {
            case SearchOutputMode.Content:
                if (!string.IsNullOrEmpty(result.Content))
                    sb.Append(result.Content);
                break;
            case SearchOutputMode.Count:
                foreach (var f in result.Filenames)
                    sb.AppendLine($"{ToRelative(f, cwd)}:1");
                if (result.NumMatches.HasValue)
                    sb.Append($"Found {result.NumMatches.Value} total matches across {result.NumFiles} file(s).");
                break;
            default:
                foreach (var f in result.Filenames)
                    sb.AppendLine(ToRelative(f, cwd));
                break;
        }

        if (result.AppliedLimit.HasValue || (result.AppliedOffset.HasValue && result.AppliedOffset.Value > 0))
        {
            var parts = new List<string>(2);
            if (result.AppliedLimit.HasValue) parts.Add($"limit: {result.AppliedLimit.Value}");
            if (result.AppliedOffset.HasValue && result.AppliedOffset.Value > 0) parts.Add($"offset: {result.AppliedOffset.Value}");
            sb.AppendLine();
            sb.Append($"[pagination: {string.Join(", ", parts)}]");
        }

        TerminalHelper.WriteRaw(sb);
        return 0;
    }

    private static void OutputJson(GrepSearchResult result, string cwd)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"matches\":[");
        var first = true;
        foreach (var f in result.Filenames)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"file\":\"");
            AppendEscaped(sb, ToRelative(f, cwd));
            sb.Append("\"}");
        }
        sb.Append("],\"count\":");
        sb.Append(result.NumFiles);
        if (result.NumMatches.HasValue)
        {
            sb.Append(",\"totalMatches\":");
            sb.Append(result.NumMatches.Value);
        }
        if (!string.IsNullOrEmpty(result.Content))
        {
            sb.Append(",\"content\":\"");
            AppendEscaped(sb, result.Content);
            sb.Append("\"");
        }
        sb.Append("}");
        System.Console.WriteLine(sb.ToString());
    }

    private static string ToRelative(string path, string cwd)
    {
        var rel = DirectoryHelper.GetRelativePath(cwd, path);
        return rel.StartsWith("..", StringComparison.Ordinal) ? path : rel;
    }

    private static void AppendEscaped(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                    else sb.Append(c);
                    break;
            }
        }
    }

    private static void PrintUsage()
    {
        TerminalHelper.WriteLine("""
            jcc rg <pattern> [path...] — ripgrep 兼容搜索（内置实现，复用 Grep 引擎）

            用法:
              jcc rg "finally\s*\{" --type cs -g "!**/tests/**"
              jcc rg "TODO|FIXME" src/ -i -n
              jcc rg "class\s+\w+Service" -A 2 -B 1 --content

            位置参数:
              <pattern>     正则表达式（PowerShell 双反斜杠会自动修复: \\s → \s）
              [path]        搜索路径（默认当前目录，禁止根目录扫盘）

            过滤选项:
              -t, --type <type>       文件类型（cs, js, ts, py, go, rust, java, ...）
              -g, --glob <pattern>    glob 过滤（! 前缀排除，如 !**/tests/**）

            输出选项:
              -i, --ignore-case       忽略大小写
              -n, --line-number       显示行号（content 模式默认开启）
              -A <n>                  匹配行后 n 行
              -B <n>                  匹配行前 n 行
              -C <n>                  匹配行前后 n 行
              -U, --multiline         多行模式（. 匹配换行）
              -F, --fixed-strings     字面量搜索（非正则）
              --content               输出匹配行
              --count                 输出匹配计数
              --files-with-matches    只输出文件名（默认）
              --head-limit <n>        限制结果数（默认 250，0=无限）
              --offset <n>            跳过前 n 条结果
              --json                  JSON 输出

            控制:
              --timeout <seconds>     超时秒数（默认 30，最大 300，超时硬终止返回 2）
              -h, --help              显示帮助

            宽容策略:
              1. PowerShell 把 \s 传成 \\s → 自动修复为 \s
              2. 缺少 path → 默认 cwd，但 cwd 为根目录则拒绝扫盘
              3. 超时 → 硬终止返回退出码 2
              4. 无匹配 → 退出码 1（对齐 rg）
              5. 二进制文件自动跳过，遵守 .gitignore

            退出码:
              0 = 有匹配
              1 = 无匹配或参数错误
              2 = 超时
            """);
    }

    private sealed record RgOptions(
        string Pattern,
        IReadOnlyList<string> Paths,
        string? Glob,
        string? FileType,
        bool CaseInsensitive,
        bool LineNumbers,
        bool Multiline,
        bool FixedStrings,
        bool Json,
        int? Before,
        int? After,
        int? Context,
        int? HeadLimit,
        int? Offset,
        int TimeoutSeconds,
        SearchOutputMode OutputMode);
}
