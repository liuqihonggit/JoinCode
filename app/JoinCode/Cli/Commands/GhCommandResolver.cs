namespace JoinCode.CliCommands;

/// <summary>
/// gh 子命令的参数元信息 — 由 <see cref="GhParamSchemaParser"/> 从 <c>ToolSchema</c> 抽取，
/// 供 <see cref="GhArgsBinder"/> 做位置参数绑定与布尔 flag 判定。
/// </summary>
/// <param name="Name">参数名（工具 schema 的 property 名）。</param>
/// <param name="IsRequired">是否必填（来自 schema 的 required 数组）。</param>
/// <param name="IsBoolean">schema 中 type 是否为 boolean，决定是否支持无值 flag 形式。</param>
internal sealed record GhParam(string Name, bool IsRequired, bool IsBoolean);

/// <summary>
/// gh 子命令的工具名解析结果。
/// </summary>
/// <param name="ToolName">拼接出的 MCP 工具名（如 <c>gh_pr_view</c>）。</param>
/// <param name="Group">gh 分组（pr/issue/repo/release/run/branch/api）。</param>
/// <param name="Action">分组下的动作（view/list/...），api 分组为 null。</param>
/// <param name="Tail">待绑定的剩余参数（位置参数 + 选项）。</param>
/// <param name="Json">是否要求 JSON 输出。</param>
internal sealed record GhResolvedCommand(string ToolName, string Group, string? Action, string[] Tail, bool Json);

/// <summary>
/// <c>jcc gh</c> 子命令解析器 — 纯函数，不依赖 DI/Host，便于单元测试。
/// <para>ADR: 0090 — 扁平元动词 <c>jcc gh &lt;group&gt; &lt;action&gt; [positional...] [--opt value] [--json]</c>，
/// 工具名按约定拼接 <c>gh_{group}_{action}</c>，位置参数按 schema required 顺序绑定。</para>
/// </summary>
internal static class GhCommandResolver
{
    /// <summary>gh 分组清单 — 与 <c>gh_*</c> 工具前缀一一对应，用于用法提示。</summary>
    private static readonly string[] KnownGroups = ["pr", "issue", "repo", "release", "run", "branch", "api"];

    /// <summary>
    /// 解析 <c>jcc gh ...</c> 命令行（<paramref name="args"/>[0] 为子命令名 <c>gh</c>）。
    /// </summary>
    /// <param name="args">完整命令行参数，首个元素必须是 <c>gh</c>。</param>
    /// <returns>解析成功返回结果；失败返回 null 并填充 <paramref name="error"/>。</returns>
    internal static GhResolvedCommand? Resolve(string[] args, out string? error)
    {
        error = null;

        // args[0] == "gh"，从 args[1] 开始取分组
        var group = NextPositional(args, 1);
        if (group is null)
        {
            error = MissingGroupError();
            return null;
        }

        // --json 可能出现在任意位置，先全局扫描，不参与位置参数计数
        var json = Array.IndexOf(args, CliArgConstants.JsonLongName) >= 0;

        // api 组是单级命令: jcc gh api <path> → gh_api
        if (string.Equals(group, "api", StringComparison.OrdinalIgnoreCase))
            return new GhResolvedCommand("gh_api", "api", null, CollectTail(args, 2, json), json);

        var action = NextPositional(args, 2);
        if (action is null)
        {
            error = MissingActionError(group);
            return null;
        }

        var toolName = $"gh_{group}_{action}";
        return new GhResolvedCommand(toolName, group, action, CollectTail(args, 3, json), json);
    }

    /// <summary>
    /// 从指定下标开始取下一个位置参数（跳过 <c>--option</c> 及其值）。
    /// </summary>
    private static string? NextPositional(string[] args, int startIndex)
    {
        for (var i = startIndex; i < args.Length; i++)
        {
            var token = args[i];
            if (token.StartsWith("--"))
            {
                // --key=value 自带值，不吞下一个 token
                if (!token.Contains('=') && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    i++;
                continue;
            }
            return token;
        }
        return null;
    }

    /// <summary>
    /// 收集待绑定的剩余参数（跳过分组、动作与 <c>--json</c>）。
    /// </summary>
    private static string[] CollectTail(string[] args, int startIndex, bool jsonStripped)
    {
        var tail = new List<string>(args.Length - startIndex);
        for (var i = startIndex; i < args.Length; i++)
        {
            if (jsonStripped && args[i] == CliArgConstants.JsonLongName)
                continue;
            tail.Add(args[i]);
        }
        return tail.ToArray();
    }

    /// <summary>缺少分组时的 Rust 风格报错 + 用法。</summary>
    private static string MissingGroupError()
        => $"{CliErrorCatalog.ArgParseError("缺少 gh 分组").ToRustStyleString("gh")}\n{Usage}";

    /// <summary>缺少动作时的 Rust 风格报错 + 该分组用法。</summary>
    private static string MissingActionError(string group)
    {
        var known = string.Join(" | ", KnownGroups);
        var hint = Array.IndexOf(KnownGroups, group.ToLowerInvariant()) >= 0
            ? $"用法: jcc gh {group} <action> [参数]（用 jcc mcp_list --category github 查看全部 gh_* 工具）"
            : $"未知分组: {group}。可用分组: {known}";
        return $"{CliErrorCatalog.ArgParseError("缺少 action").ToRustStyleString($"gh {group}")}\n{hint}";
    }

    /// <summary><c>jcc gh</c> 用法文本。</summary>
    internal static string Usage => $"""
        用法:
          jcc gh <group> <action> [位置参数...] [--选项 值] [--json]

        分组: {string.Join(" | ", KnownGroups)}

        示例:
          jcc gh pr view 123                     查看 PR 详情
          jcc gh pr checks 123                   查看 PR 的 CI 检查状态
          jcc gh pr list --limit 3               列出 PR
          jcc gh run view 123 --log --filter error   查看 CI 日志（只留 error）
          jcc gh run rerun 123                   重跑失败的 job
          jcc gh issue comment 12 "正文"          评论 Issue
          jcc gh release download v1.0 ./out     下载 Release asset
          jcc gh api repos/o/r/issues            通用 GitHub REST 调用

        说明:
          位置参数按工具 schema 的 required 顺序绑定（如 pr_number / run_id / tag）
          --json  结构化 JSON 输出
          用 jcc mcp_schema <tool> 查看该工具的完整参数
        """;
}

/// <summary>
/// 从 <c>ToolSchema</c> 抽取 gh 参数元信息 — 纯函数，便于单元测试。
/// <para>位置参数槽位 = schema required 数组（按声明顺序），这是"人类直觉上的位置参数"。</para>
/// </summary>
internal static class GhParamSchemaParser
{
    /// <summary>
    /// 抽取参数列表：必填参数保持 required 声明顺序在前，其余按 properties 顺序在后。
    /// </summary>
    internal static List<GhParam> Parse(ToolSchema schema)
    {
        var result = new List<GhParam>(schema.Properties.Count);
        var requiredSet = new HashSet<string>(schema.Required, StringComparer.Ordinal);
        foreach (var required in schema.Required)
        {
            var isBoolean = schema.Properties.TryGetValue(required, out var prop)
                && string.Equals(prop.Type, "boolean", StringComparison.OrdinalIgnoreCase);
            result.Add(new GhParam(required, IsRequired: true, IsBoolean: isBoolean));
        }
        foreach (var (name, prop) in schema.Properties)
        {
            if (requiredSet.Contains(name))
                continue;
            result.Add(new GhParam(name, IsRequired: false,
                IsBoolean: string.Equals(prop.Type, "boolean", StringComparison.OrdinalIgnoreCase)));
        }
        return result;
    }
}

/// <summary>
/// gh 参数绑定器 — 把剩余参数按 <see cref="GhParam"/> 绑定为 <c>参数名 → 字符串值</c>。
/// <para>支持三种形式：位置参数、<c>--key=value</c>、<c>--key value</c>；布尔参数支持无值 flag。</para>
/// </summary>
internal static class GhArgsBinder
{
    /// <summary>
    /// 执行绑定。
    /// </summary>
    /// <param name="tail">待绑定的剩余参数。</param>
    /// <param name="parameters">工具参数元信息。</param>
    /// <param name="toolName">工具名，仅用于报错文案。</param>
    /// <param name="error">失败时的 Rust 风格报错。</param>
    /// <returns>成功返回参数字典；失败返回 null。</returns>
    internal static Dictionary<string, string>? Bind(
        string[] tail, IReadOnlyList<GhParam> parameters, string toolName, out string? error)
    {
        error = null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var byName = new Dictionary<string, GhParam>(StringComparer.Ordinal);
        foreach (var p in parameters)
            byName[p.Name] = p;

        var slots = parameters.Where(p => p.IsRequired).ToList();
        var slotIndex = 0;

        for (var i = 0; i < tail.Length; i++)
        {
            var token = tail[i];
            if (!token.StartsWith("--"))
            {
                if (slotIndex >= slots.Count)
                {
                    error = TooManyPositionalError(toolName, token, parameters);
                    return null;
                }
                var slot = slots[slotIndex++];
                if (result.ContainsKey(slot.Name))
                {
                    error = DuplicateParamError(slot.Name);
                    return null;
                }
                result[slot.Name] = token;
                continue;
            }

            var key = token[2..];
            string? inlineValue = null;
            var eqIdx = key.IndexOf('=');
            if (eqIdx > 0)
            {
                inlineValue = key[(eqIdx + 1)..];
                key = key[..eqIdx];
            }

            // 宽容策略: 真实 gh CLI 用连字符（--max-lines），工具 schema 用下划线（max_lines）
            if (!byName.TryGetValue(key, out var param) && !byName.TryGetValue(key.Replace('-', '_'), out param))
            {
                error = UnknownOptionError(key, parameters);
                return null;
            }
            key = param.Name;

            if (inlineValue is not null)
            {
                result[key] = inlineValue;
                continue;
            }

            if (param.IsBoolean)
            {
                result[key] = "true";
                continue;
            }

            if (i + 1 >= tail.Length || tail[i + 1].StartsWith("--"))
            {
                error = $"{CliErrorCatalog.ArgMissingRequired($"--{key} 的值").ToRustStyleString(token)}\n提示: 用法 --{key} <值> 或 --{key}=<值>";
                return null;
            }

            result[key] = tail[i + 1];
            i++;
        }

        var missing = slots.FirstOrDefault(s => !result.ContainsKey(s.Name));
        if (missing is not null)
        {
            error = MissingPositionalError(toolName, missing.Name, slots);
            return null;
        }

        return result;
    }

    private static string TooManyPositionalError(string toolName, string token, IReadOnlyList<GhParam> parameters)
    {
        var names = string.Join(", ", parameters.Select(p => p.Name));
        return $"{CliErrorCatalog.ArgParseError($"多余的位置参数: {token}").ToRustStyleString(token)}\n"
             + $"{toolName} 接受的参数: {names}\n提示: 非位置参数请用 --参数名 值 的形式";
    }

    private static string DuplicateParamError(string name)
        => CliErrorCatalog.ArgParseError($"参数重复指定: {name}（位置参数与 --{name} 只能二选一）").ToRustStyleString($"--{name}");

    private static string UnknownOptionError(string key, IReadOnlyList<GhParam> parameters)
    {
        var names = string.Join(", ", parameters.Select(p => $"--{p.Name}"));
        return $"{CliErrorCatalog.ArgUnknownOption($"--{key}").ToRustStyleString($"--{key}")}\n可用选项: {names}";
    }

    private static string MissingPositionalError(string toolName, string missingName, IReadOnlyList<GhParam> slots)
    {
        var positionalHint = string.Join(' ', slots.Select(s => $"<{s.Name}>"));
        return $"{CliErrorCatalog.ArgMissingRequired(missingName).ToRustStyleString(toolName)}\n用法: {positionalHint}（示例见 jcc gh --help）";
    }
}
