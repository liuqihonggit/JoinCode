namespace McpToolDispatch;

/// <summary>
/// GitHub Actions 日志行解析器 — 状态机 + Span,零 GC 逐行处理
/// <para>跟踪两类步骤: ##[start-action display=...] (action 步骤) 和 ##[group]Run cmd (run 步骤)</para>
/// <para>action 步骤优先级高于 run 步骤,action 内部的 ##[group]Run 归 action 步骤</para>
/// <para>Span 处理时间戳剥离和标记检测,只在提取步骤名时 ToString 分配</para>
/// </summary>
internal sealed class GitHubLogParser {
    private string? _currentStepName;
    private bool _inAction;
    private bool _inRunGroup;
    private static readonly SearchValues<char> s_nameTerminators = SearchValues.Create(";]");

    /// <summary>
    /// 解析一行日志并累积 — 状态机跟踪步骤名,Span 检测标记
    /// </summary>
    public void ParseLine(string line, RunLogSummary summary, Dictionary<string, Dictionary<string, List<string>>> sectionContents) {
        var content = StripTimestamp(line.AsSpan());

        // 优先级1: ##[start-action display=StepName;id=...]
        if (content.StartsWith("##[start-action display=".AsSpan())) {
            var rest = content.Slice("##[start-action display=".Length);
            var endIdx = rest.IndexOfAny(s_nameTerminators);
            _currentStepName = endIdx > 0 ? rest[..endIdx].ToString() : rest.ToString();
            _inAction = true;
            _inRunGroup = false;
            return;
        }

        // 优先级2: ##[end-action
        if (content.StartsWith("##[end-action".AsSpan())) {
            _currentStepName = null;
            _inAction = false;
            _inRunGroup = false;
            return;
        }

        // 优先级3: ##[group]Run cmd (仅当不在 action 中,action 内部的 group 归 action)
        if (!_inAction && content.StartsWith("##[group]Run ".AsSpan())) {
            var cmd = content.Slice("##[group]Run ".Length);
            _currentStepName = ExtractRunStepName(cmd);
            _inRunGroup = true;
            return;
        }

        // 优先级4: ##[endgroup] (仅当在 run group 中)
        // 不清除 _currentStepName — ##[endgroup] 只结束命令回显,实际输出在 endgroup 之后
        // 步骤持续到下一个 ##[start-action] 或 ##[group]Run 切换
        if (_inRunGroup && content.StartsWith("##[endgroup]".AsSpan())) {
            _inRunGroup = false;
            return;
        }

        if (_currentStepName is not null) {
            GitHubRunStepExtractor.Accumulate(line, _currentStepName, summary, sectionContents);
            return;
        }

        // 回退: [entry.Name] 前缀 或 TSV 格式
        var stepName = TryExtractStepName(line.AsSpan());
        if (stepName is not null)
            GitHubRunStepExtractor.Accumulate(line, stepName, summary, sectionContents);
    }

    /// <summary>
    /// 从 ##[group]Run 命令提取简短步骤名 — Span 处理,零 GC
    /// <para>"dotnet test xxx.csproj ..." → "dotnet test xxx"</para>
    /// <para>"dotnet build xxx.csproj ..." → "dotnet build xxx"</para>
    /// <para>"actions/checkout@v5" → "actions/checkout@v5"</para>
    /// <para>"./.github/actions/setup-test-env" → "setup-test-env"</para>
    /// <para>其他 → 截断到 60 字符</para>
    /// </summary>
    private static string ExtractRunStepName(ReadOnlySpan<char> cmd) {
        // dotnet test xxx.csproj ... → dotnet test xxx
        if (cmd.StartsWith("dotnet test ".AsSpan())) {
            var after = cmd.Slice("dotnet test ".Length);
            var csprojIdx = after.IndexOf(".csproj".AsSpan());
            if (csprojIdx > 0) {
                var path = after[..csprojIdx];
                var lastSlash = path.LastIndexOf('/');
                var shortName = lastSlash >= 0 ? path.Slice(lastSlash + 1) : path;
                return string.Concat("dotnet test ", shortName.ToString());
            }
            return "dotnet test";
        }

        // dotnet build xxx.csproj ... → dotnet build xxx
        if (cmd.StartsWith("dotnet build ".AsSpan())) {
            var after = cmd.Slice("dotnet build ".Length);
            var csprojIdx = after.IndexOf(".csproj".AsSpan());
            if (csprojIdx > 0) {
                var path = after[..csprojIdx];
                var lastSlash = path.LastIndexOf('/');
                var shortName = lastSlash >= 0 ? path.Slice(lastSlash + 1) : path;
                return string.Concat("dotnet build ", shortName.ToString());
            }
            return "dotnet build";
        }

        // ./.github/actions/xxx → xxx
        if (cmd.StartsWith("./.github/actions/".AsSpan())) {
            var after = cmd.Slice("./.github/actions/".Length);
            var spaceIdx = after.IndexOf(' ');
            var name = spaceIdx > 0 ? after[..spaceIdx] : after;
            return name.ToString();
        }

        // 其他: 截断到 60 字符
        return cmd.Length <= 60 ? cmd.ToString() : cmd[..60].ToString();
    }

    /// <summary>
    /// 剥离时间戳前缀 — "2026-09-07T17:08:27.5016453Z content" → "content",返回 Span 不分配
    /// </summary>
    private static ReadOnlySpan<char> StripTimestamp(ReadOnlySpan<char> span) {
        // 时间戳格式: "2026-09-07T17:08:27.5016453Z content"
        var zIdx = span.IndexOf('Z');
        if (zIdx > 0 && zIdx + 2 < span.Length && span[zIdx + 1] == ' ')
            return span.Slice(zIdx + 2);
        // [entry.Name] content
        if (span.Length > 0 && span[0] == '[') {
            var closeIdx = span.IndexOf(']');
            if (closeIdx > 0 && closeIdx + 2 < span.Length)
                return span.Slice(closeIdx + 2);
        }
        return span;
    }

    /// <summary>
    /// 从日志行 Span 提取步骤名 — [entry.Name] 前缀优先,回退 TSV,只在找到时 ToString
    /// </summary>
    private static string? TryExtractStepName(ReadOnlySpan<char> span) {
        // [entry.Name] line → GitHubRunStepExtractor.ExtractStepNameFromEntryName(entry.Name)
        if (span.Length > 0 && span[0] == '[') {
            var closeIdx = span.IndexOf(']');
            if (closeIdx > 1)
                return ExtractStepNameFromEntryName(span[1..closeIdx]);
        }
        // TSV: col1\tstepName\t...
        var tabIdx = span.IndexOf('\t');
        if (tabIdx < 0) return null;
        var remaining = span.Slice(tabIdx + 1);
        var secondTabIdx = remaining.IndexOf('\t');
        return secondTabIdx >= 0 ? remaining[..secondTabIdx].ToString() : remaining.ToString();
    }

    /// <summary>
    /// 从 zip entry 名 Span 提取步骤名 — "0_Checkout.txt" → "Checkout"
    /// </summary>
    private static string ExtractStepNameFromEntryName(ReadOnlySpan<char> entryName) {
        var name = entryName;
        var slashIdx = name.LastIndexOf('/');
        if (slashIdx >= 0) name = name.Slice(slashIdx + 1);
        var dotIdx = name.LastIndexOf('.');
        if (dotIdx > 0) name = name[..dotIdx];
        var underscoreIdx = name.IndexOf('_');
        if (underscoreIdx > 0 && int.TryParse(name[..underscoreIdx], CultureInfo.InvariantCulture, out _))
            name = name.Slice(underscoreIdx + 1);
        return name.Length == 0 ? entryName.ToString() : name.ToString();
    }
}
