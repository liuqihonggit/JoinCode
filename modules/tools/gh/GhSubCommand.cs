namespace JoinCode.Gh;

internal static class GhSubCommand {
    /// <summary>
    /// 按工具名直调的选项 —— 给 CLI 节点 exe（jcc-tool-github）用。
    /// <para>存在理由：节点侧的工具名是 <c>gh_issue_list</c>，而本 exe 走 gh 风格的
    /// <c>issue list</c> 解析。若让节点侧自己把名字拆成 group/action，就得复制一份组名表，
    /// 迟早和这里的 KnownGroups 漂移；直调入口让两边共用同一个 GhDispatcher。</para>
    /// </summary>
    internal const string CallToolOption = "--call-tool";

    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var hasHelp = Array.IndexOf(args, CliArgCliOptionConstants.HelpLongName) >= 0
                    || Array.IndexOf(args, CliArgCliOptionConstants.HelpShortName) >= 0;
        if (hasHelp) {
            var group = FlatSubCommandRouter.GetPositional(args, 0);
            var action = group is not null ? FlatSubCommandRouter.GetPositional(args, 1) : null;
            if (group is not null && action is not null)
                return RenderToolHelp(group, action);
            if (string.Equals(group, "api", StringComparison.OrdinalIgnoreCase))
                return RenderToolHelp("api", "");
            if (group is not null && GhCommandResolver.SingleLevelGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
                return RenderToolHelp(group, "");
            if (group is not null)
                return RenderGroupHelp(group);
            TerminalHelper.WriteLine(GhCommandResolver.Usage);
            return 0;
        }

        var callToolIndex = Array.IndexOf(args, CallToolOption);
        if (callToolIndex > 0) return await CallToolAsync(args, callToolIndex, ct).ConfigureAwait(false);

        var noCache = Array.IndexOf(args, "--no-cache") >= 0;
        var cleanArgs = noCache ? args.Where(a => !string.Equals(a, "--no-cache", StringComparison.Ordinal)).ToArray() : args;
        var resolved = GhCommandResolver.Resolve(cleanArgs, out var resolveError);
        if (resolved is null) {
            TerminalHelper.WriteError(resolveError!);
            return 1;
        }

        if (!GhDispatcher.ContainsTool(resolved.ToolName)) {
            TerminalHelper.WriteError(ToolNotFoundMessage(resolved));
            return 1;
        }

        var parameters = GhDispatcher.GetParams(resolved.ToolName);
        var bound = GhArgsBinder.Bind(resolved.Tail, parameters, resolved.ToolName, out var bindError);
        if (bound is null) {
            TerminalHelper.WriteError(bindError!);
            return 1;
        }

        if (resolved.Json && !bound.ContainsKey("verbosity")) bound["verbosity"] = "2";
        return await InvokeAndOutputAsync(resolved.ToolName, bound, resolved.Json, noCache, ct).ConfigureAwait(false);
    }

    /// <summary>按工具名直接分发 —— 跳过 gh 风格命令解析，参数按同一份 schema 绑定。</summary>
    private static async Task<int> CallToolAsync(string[] args, int index, CancellationToken ct) {
        if (index + 1 >= args.Length) {
            TerminalHelper.WriteError($"{CallToolOption} 需要一个工具名，例如: {CallToolOption} gh_issue_list --repo owner/name");
            return 1;
        }
        var toolName = args[index + 1];
        if (!GhDispatcher.ContainsTool(toolName)) {
            TerminalHelper.WriteError(UnknownToolMessage(toolName));
            return 1;
        }
        var tail = args[(index + 2)..];
        var json = FlatSubCommandRouter.ShouldOutputJson(args);
        var parameters = GhDispatcher.GetParams(toolName);
        var bound = GhArgsBinder.Bind(tail, parameters, toolName, out var bindError);
        if (bound is null) {
            TerminalHelper.WriteError(bindError!);
            return 1;
        }
        if (json && !bound.ContainsKey("verbosity")) bound["verbosity"] = "2";
        return await InvokeAndOutputAsync(toolName, bound, json, noCache: false, ct).ConfigureAwait(false);
    }

    private static async Task<int> InvokeAndOutputAsync(
        string toolName, Dictionary<string, string> bound, bool json, bool noCache, CancellationToken ct) {
        var tracker = new GhTimingTracker { CacheDisabled = noCache };
        var prevTimer = GhTimingTracker.CurrentTimer.Value;
        GhTimingTracker.CurrentTimer.Value = tracker;
        ToolResult? result;
        try {
            result = await GhDispatcher.InvokeAsync(toolName, bound, ct).ConfigureAwait(false);
        } catch (JqSyntaxException ex) {
            // jq 写错属参数错误（不是执行失败）：报原因 + 退出 2，避免静默回退成整份 JSON
            TerminalHelper.WriteError(ex.Message);
            return 2;
        } finally {
            GhTimingTracker.CurrentTimer.Value = prevTimer;
        }
        if (result is null) {
            TerminalHelper.WriteError($"工具 '{toolName}' 调用返回 null");
            return 1;
        }
        result.TimingInfo = tracker.Format();
        return OutputResult(result, json);
    }

    /// <summary>直调入口的未知工具提示 —— 没有 group/action 可拆，直接按名字做包含/近似匹配。</summary>
    private static string UnknownToolMessage(string toolName) {
        var lowered = toolName.ToLowerInvariant();
        var candidates = GhDispatcher.GetToolNames()
            .Where(n => n.Contains(lowered, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
        var head = CliErrorCatalog.ResourceNotFound("gh 工具", toolName).ToRustStyleString(toolName);
        return candidates.Count == 0 ? $"{head}\n提示: {CallToolOption} 后接 [McpTool] 特性里的完整工具名（如 gh_issue_list）"
                                     : $"{head}\n可用: {string.Join(", ", candidates)}";
    }

    private static string ToolNotFoundMessage(GhResolvedCommand resolved) {
        var prefix = resolved.Action is null ? $"gh_{resolved.Group}" : $"gh_{resolved.Group}_";
        var candidates = GhDispatcher.GetToolNames()
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => resolved.Action is null ? t : t[prefix.Length..])
            .Select(t => t.Replace('_', '-'))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var head = CliErrorCatalog.ResourceNotFound("gh 子命令", resolved.ToolName).ToRustStyleString(resolved.ToolName);
        if (candidates.Count == 0)
            return $"{head}\n提示: 没有以 {prefix} 开头的工具";
        return $"{head}\n可用: {string.Join(", ", candidates)}";
    }

    private static int RenderToolHelp(string group, string action) {
        var normalizedGroup = group.Replace('-', '_');
        var normalizedAction = action.Replace('-', '_');
        var toolName = string.IsNullOrEmpty(normalizedAction)
            ? $"gh_{normalizedGroup}" : $"gh_{normalizedGroup}_{normalizedAction}";
        if (!GhDispatcher.ContainsTool(toolName)) {
            TerminalHelper.WriteLine(GhCommandResolver.Usage);
            return 0;
        }
        var parameters = GhDispatcher.GetParams(toolName);
        var sb = new StringBuilder(512);
        sb.AppendLine($"工具: {toolName}");
        sb.AppendLine();
        var required = parameters.Where(p => p.IsRequired).DistinctBy(p => p.Name).ToList();
        var optional = parameters.Where(p => !p.IsRequired).DistinctBy(p => p.Name).ToList();
        var usageParts = new List<string> { "jcc", "gh", group, action };
        foreach (var r in required) usageParts.Add($"<{r.Name}>");
        foreach (var o in optional) usageParts.Add($"[--{o.Name}]");
        usageParts.Add("[--json]");
        sb.AppendLine($"用法: {string.Join(' ', usageParts)}");
        if (required.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("必填参数:");
            foreach (var r in required) sb.AppendLine($"  <{r.Name}>    ({r.TypeHint})");
        }
        if (optional.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("可选参数:");
            foreach (var o in optional) {
                var hint = o.IsBoolean ? "" : " <值>";
                sb.AppendLine($"  --{o.Name}{hint}    ({o.TypeHint})");
            }
        }
        TerminalHelper.WriteLine(sb.ToString());
        return 0;
    }

    /// <summary>显示分组下所有可用子命令 — gh <group> --help</summary>
    private static int RenderGroupHelp(string group) {
        var normalizedGroup = group.Replace('-', '_');
        var prefix = $"gh_{normalizedGroup}_";
        var actions = GhDispatcher.GetToolNames()
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n[prefix.Length..].Replace('_', '-'))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var sb = new StringBuilder(256);
        sb.AppendLine($"分组: {group}");
        if (actions.Count == 0) {
            sb.AppendLine("  该分组下无可用子命令。提示: 用 jcc gh --help 查看所有分组");
        } else {
            sb.AppendLine($"可用子命令 ({actions.Count}):");
            foreach (var a in actions) sb.AppendLine($"  gh {group} {a}");
            sb.AppendLine($"用法: jcc gh {group} <子命令> [参数] — 用 jcc gh {group} <子命令> --help 查看具体参数");
        }
        TerminalHelper.WriteLine(sb.ToString());
        return 0;
    }

    private static int OutputResult(ToolResult result, bool json)
        => json ? OutputJsonResult(result) : OutputTextResult(result);

    /// <summary>JSON 模式输出 — 3 路优先级：error/empty → 序列化整个结果；已是 JSON → 直接输出；否则 → 序列化字符串</summary>
    /// <para>timing 信息用 XML 标签包裹输出到 stdout 末尾,AI 可区分数据与元信息</para>
    private static int OutputJsonResult(ToolResult result) {
        var text = (result.Content.Count == 1 ? result.Content[0].Text : null) ?? string.Empty;
        if (result.IsError || string.IsNullOrEmpty(text)) {
            Console.WriteLine(GhJsonContext.UnescapeHtmlChars(JsonSerializer.Serialize(result, GhJsonContext.Safe.ToolResult)));
            if (result.TimingInfo is not null) WriteToChannel(result.TimingInfo, result.IsError);
            return result.IsError ? 1 : 0;
        }
        if (text.Length > 0 && (text[0] == '[' || text[0] == '{' || text[0] == '"' || IsJsonPrimitive(text))) {
            Console.WriteLine(text);
            if (result.TimingInfo is not null) WriteToChannel(result.TimingInfo, result.IsError);
            return result.IsError ? 1 : 0;
        }
        Console.WriteLine(GhJsonContext.UnescapeHtmlChars(JsonSerializer.Serialize(text, GhJsonContext.Safe.String)));
        if (result.TimingInfo is not null) WriteToChannel(result.TimingInfo, result.IsError);
        return result.IsError ? 1 : 0;
    }

    /// <summary>文本模式输出 — LINQ 过滤非空内容消除 hasOutput 标志位，无内容时输出占位提示</summary>
    /// <para>timing 信息与内容走同一 channel(错误→stderr,成功→stdout),保证顺序确定</para>
    private static int OutputTextResult(ToolResult result) {
        var nonEmptyContents = result.Content.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        if (nonEmptyContents.Count == 0) WriteToChannel("(无文本输出)", result.IsError);
        else nonEmptyContents.ForEach(c => WriteToChannel(c.Text, result.IsError));
        if (result.TimingInfo is not null) WriteToChannel(result.TimingInfo, result.IsError);
        return result.IsError ? 1 : 0;
    }

    /// <summary>按错误标志分流到 stderr/stdout</summary>
    private static void WriteToChannel(string? text, bool isError) {
        if (string.IsNullOrEmpty(text)) return;
        if (isError) Console.Error.WriteLine(text); else Console.WriteLine(text);
    }

    /// <summary>判断 text 是否是 JSON 原始值（数字/true/false/null），用于 --jq 结果直接输出而非再次序列化</summary>
    private static bool IsJsonPrimitive(string text)
        => text is "true" or "false" or "null"
           || (text.Length > 0 && (char.IsDigit(text[0]) || text[0] == '-') && double.TryParse(text, out _));
}
