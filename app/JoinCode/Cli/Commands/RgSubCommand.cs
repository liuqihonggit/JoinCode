namespace JoinCode.CliCommands;

/// <summary>
/// ripgrep 兼容搜索子命令 — jcc rg &lt;pattern&gt; &lt;path2path&gt; [path...]
/// <para>ADR: 0070 — 内置 rg 实现，用 RgEngine（mmap + PLINQ 并行 + 零 GC Span 行遍历）。</para>
/// <para>宽容策略：自动修复 PowerShell 双反斜杠转义、强制路径参数禁止扫盘、超时硬终止。</para>
/// </summary>
internal static class RgSubCommand
{
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 300;

    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var parsed = ParseArgs(args);
        if (parsed is null)
            return 1;

        var pattern = FixPowerShellEscaping(parsed.Pattern);

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
            var query = new RgQuery(
                Pattern: pattern,
                Paths: parsed.Paths,
                Glob: parsed.Glob,
                FileType: parsed.FileType,
                CaseInsensitive: parsed.CaseInsensitive,
                SmartCase: parsed.SmartCase,
                WordRegexp: parsed.WordRegexp,
                OnlyMatching: parsed.OnlyMatching,
                Replace: parsed.Replace,
                Multiline: parsed.Multiline,
                FixedStrings: parsed.FixedStrings,
                Hidden: parsed.Hidden,
                NoIgnore: parsed.NoIgnore,
                Before: parsed.Before,
                After: parsed.After,
                Context: parsed.Context,
                LineNumbers: parsed.LineNumbers,
                HeadLimit: parsed.HeadLimit,
                Offset: parsed.Offset,
                OutputMode: parsed.OutputMode,
                Sort: parsed.Sort);

            RgOutcome outcome;
            try
            {
                outcome = await Task.Run(() => RgEngine.Search(query, token), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TerminalHelper.WriteError($"搜索超时（{parsed.TimeoutSeconds}s）。请缩小搜索范围、用 --type/-g 过滤，或增加 --timeout。");
                return 2;
            }

            return OutputOutcome(outcome, parsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TerminalHelper.WriteError($"搜索超时（{parsed.TimeoutSeconds}s）— 已硬终止。");
            return 2;
        }
    }

    internal static RgOptions? ParseArgs(string[] args)
    {
        string? pattern = null;
        var paths = new List<string>();
        var caseInsensitive = false;
        var smartCase = false;
        var wordRegexp = false;
        var onlyMatching = false;
        var lineNumbers = false;
        var multiline = false;
        var fixedStrings = false;
        var hidden = false;
        var noIgnore = false;
        var json = false;
        string? glob = null;
        string? fileType = null;
        string? replace = null;
        string? sort = null;
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
                    case "--smart-case": smartCase = true; break;
                    case "--word-regexp": wordRegexp = true; break;
                    case "--only-matching": onlyMatching = true; break;
                    case "--line-number": lineNumbers = true; break;
                    case "--no-line-number": lineNumbers = false; break;
                    case "--multiline":
                    case "--multiline-dotall": multiline = true; break;
                    case "--fixed-strings": fixedStrings = true; break;
                    case "--hidden": hidden = true; break;
                    case "--no-ignore": noIgnore = true; break;
                    case "--json": json = true; break;
                    case "--count": outputMode = SearchOutputMode.Count; break;
                    case "--files-with-matches": outputMode = SearchOutputMode.Files; break;
                    case "--content": outputMode = SearchOutputMode.Content; break;
                    case "--replace": replace = inlineValue ?? ReadNextValue(args, ref i); break;
                    case "--sort": sort = inlineValue ?? ReadNextValue(args, ref i); break;
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
                        ref caseInsensitive, ref smartCase, ref wordRegexp, ref onlyMatching,
                        ref lineNumbers, ref multiline, ref fixedStrings,
                        ref json, ref glob, ref fileType, ref replace,
                        ref before, ref after, ref context, ref headLimit, ref offset,
                        ref timeoutSeconds, ref outputMode))
                {
                    return null;
                }
            }
        }

        if (pattern is null)
        {
            TerminalHelper.WriteError("错误: 缺少 pattern 参数。用法: jcc rg <pattern> <path> [path...]");
            PrintUsage();
            return null;
        }

        if (paths.Count == 0)
        {
            TerminalHelper.WriteError("错误: 必须指定搜索路径。禁止无路径搜索（会扫盘卡死）。");
            TerminalHelper.WriteError("用法: jcc rg <pattern> <path> [path...]");
            TerminalHelper.WriteError("示例: jcc rg \"WorktreeToolNameConstants\" core/ --type cs -l");
            TerminalHelper.WriteError("      jcc rg \"class SearchService\" app/JoinCode -n --content");
            return null;
        }

        var effectiveLineNumbers = lineNumbers || outputMode == SearchOutputMode.Content;

        return new RgOptions(
            Pattern: pattern,
            Paths: paths,
            Glob: glob,
            FileType: fileType,
            CaseInsensitive: caseInsensitive,
            SmartCase: smartCase,
            WordRegexp: wordRegexp,
            OnlyMatching: onlyMatching,
            Replace: replace,
            Multiline: multiline,
            FixedStrings: fixedStrings,
            Hidden: hidden,
            NoIgnore: noIgnore,
            Json: json,
            LineNumbers: effectiveLineNumbers,
            Before: before,
            After: after,
            Context: context,
            HeadLimit: headLimit,
            Offset: offset,
            TimeoutSeconds: timeoutSeconds,
            OutputMode: outputMode,
            Sort: sort);
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

    private static bool ParseShortOptionCluster(
        string arg, string[] args, ref int i,
        ref bool caseInsensitive, ref bool smartCase, ref bool wordRegexp, ref bool onlyMatching,
        ref bool lineNumbers, ref bool multiline, ref bool fixedStrings,
        ref bool json, ref string? glob, ref string? fileType, ref string? replace,
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
                case 'S': smartCase = true; j++; break;
                case 'w': wordRegexp = true; j++; break;
                case 'o': onlyMatching = true; j++; break;
                case 'n': lineNumbers = true; j++; break;
                case 'U': multiline = true; j++; break;
                case 'F': fixedStrings = true; j++; break;
                case 'c': outputMode = SearchOutputMode.Count; j++; break;
                case 'l': outputMode = SearchOutputMode.Files; j++; break;
                case 'A': after = ConsumeShortNumber(span, ref j, args, ref i); break;
                case 'B': before = ConsumeShortNumber(span, ref j, args, ref i); break;
                case 'C': context = ConsumeShortNumber(span, ref j, args, ref i); break;
                case 'g': glob = ConsumeShortString(span, ref j, args, ref i); break;
                case 't': fileType = ConsumeShortString(span, ref j, args, ref i); break;
                case 'r': replace = ConsumeShortString(span, ref j, args, ref i); break;
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

    private static int OutputOutcome(RgOutcome outcome, RgOptions opts)
    {
        if (!outcome.Success)
        {
            TerminalHelper.WriteError(outcome.Error ?? "搜索失败");
            return 1;
        }

        if (outcome.Results.Count == 0)
        {
            if (opts.Json)
                System.Console.WriteLine("{\"matches\":[]}");
            return 1;
        }

        if (opts.Json)
        {
            OutputJson(outcome);
            return 0;
        }

        var sb = new StringBuilder(256);
        switch (opts.OutputMode)
        {
            case SearchOutputMode.Content:
                foreach (var r in outcome.Results)
                {
                    if (r.ContentLines is not null)
                        foreach (var line in r.ContentLines)
                            sb.AppendLine(line);
                }
                break;
            case SearchOutputMode.Count:
                foreach (var r in outcome.Results)
                    sb.AppendLine($"{r.FilePath}:{r.MatchCount}");
                sb.Append($"Found {outcome.TotalMatches} total matches across {outcome.Results.Count} file(s).");
                break;
            default:
                foreach (var r in outcome.Results)
                    sb.AppendLine(r.FilePath);
                break;
        }

        if (outcome.AppliedLimit.HasValue || (outcome.AppliedOffset.HasValue && outcome.AppliedOffset.Value > 0))
        {
            var parts = new List<string>(2);
            if (outcome.AppliedLimit.HasValue) parts.Add($"limit: {outcome.AppliedLimit.Value}");
            if (outcome.AppliedOffset.HasValue && outcome.AppliedOffset.Value > 0) parts.Add($"offset: {outcome.AppliedOffset.Value}");
            sb.AppendLine();
            sb.Append($"[pagination: {string.Join(", ", parts)}]");
        }

        TerminalHelper.WriteRaw(sb);
        return 0;
    }

    private static void OutputJson(RgOutcome outcome)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"matches\":[");
        var first = true;
        foreach (var r in outcome.Results)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"file\":\"");
            AppendEscaped(sb, r.FilePath);
            sb.Append("\",\"count\":");
            sb.Append(r.MatchCount);
            if (r.ContentLines is not null && r.ContentLines.Count > 0)
            {
                sb.Append(",\"lines\":[");
                var firstLine = true;
                foreach (var line in r.ContentLines)
                {
                    if (!firstLine) sb.Append(',');
                    firstLine = false;
                    sb.Append("\"");
                    AppendEscaped(sb, line);
                    sb.Append("\"");
                }
                sb.Append("]");
            }
            sb.Append("}");
        }
        sb.Append("],\"totalMatches\":");
        sb.Append(outcome.TotalMatches);
        sb.Append(",\"fileCount\":");
        sb.Append(outcome.Results.Count);
        sb.Append("}");
        System.Console.WriteLine(sb.ToString());
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
            jcc rg <pattern> <path> [path...] — ripgrep 兼容搜索（mmap + PLINQ 并行 + 零 GC）

            用法:
              jcc rg "finally\s*\{" core/ --type cs -g "!**/tests/**"
              jcc rg "TODO|FIXME" src/ -i -n -C 2
              jcc rg "class\s+\w+Service" app/JoinCode -A 2 -B 1 --content

            位置参数:
              <pattern>     正则表达式（PowerShell 双反斜杠会自动修复: \\s → \s）
              <path>        搜索路径（必填！禁止无路径搜索，避免扫盘卡死）
              [path...]     额外搜索路径（多路径合并去重）

            过滤选项:
              -t, --type <type>       文件类型（cs, js, ts, py, go, rust, java, ...）
              -g, --glob <pattern>    glob 过滤（! 前缀排除，如 !**/tests/**）
              --hidden                搜索隐藏文件
              --no-ignore             禁用 .gitignore

            输出选项:
              -i, --ignore-case       忽略大小写
              -S, --smart-case        智能大小写（模式含大写则区分，否则忽略）
              -w, --word-regexp       词边界匹配
              -o, --only-matching     只输出匹配部分
              -r, --replace <text>    替换匹配文本
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
              --sort <key>            排序（path/modified/accessed/created/none）
              --json                  JSON 输出

            控制:
              --timeout <seconds>     超时秒数（默认 30，最大 300，超时硬终止返回 2）
              -h, --help              显示帮助

            宽容策略:
              1. PowerShell 把 \s 传成 \\s → 自动修复为 \s
              2. 缺少 path → 立即报错退出（禁止无路径搜索，避免扫盘卡死）
              3. 根目录（C:\ / /）→ 拒绝扫盘
              4. 超时 → 硬终止返回退出码 2
              5. 无匹配 → 退出码 1（对齐 rg）
              6. 二进制文件自动跳过，遵守 .gitignore
              7. mmap 零拷贝读取大文件（>64KB），PLINQ 并行，Span 零 GC 行遍历

            退出码:
              0 = 有匹配
              1 = 无匹配或参数错误
              2 = 超时
            """);
    }

    internal sealed record RgOptions(
        string Pattern,
        IReadOnlyList<string> Paths,
        string? Glob,
        string? FileType,
        bool CaseInsensitive,
        bool SmartCase,
        bool WordRegexp,
        bool OnlyMatching,
        string? Replace,
        bool Multiline,
        bool FixedStrings,
        bool Hidden,
        bool NoIgnore,
        bool Json,
        bool LineNumbers,
        int? Before,
        int? After,
        int? Context,
        int? HeadLimit,
        int? Offset,
        int TimeoutSeconds,
        SearchOutputMode OutputMode,
        string? Sort);
}
