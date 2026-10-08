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
internal static class GhCommandResolver {
    /// <summary>gh 分组清单 — 与 <c>gh_*</c> 工具前缀一一对应，用于用法提示。</summary>
    private static readonly string[] KnownGroups = [
        GhGroupEnumConstants.Pr,
        GhGroupEnumConstants.Issue,
        GhGroupEnumConstants.Repo,
        GhGroupEnumConstants.Release,
        GhGroupEnumConstants.Run,
        GhGroupEnumConstants.Branch,
        GhGroupEnumConstants.Api,
        GhGroupEnumConstants.Label,
        GhGroupEnumConstants.Search,
        GhGroupEnumConstants.Workflow,
        GhGroupEnumConstants.Auth,
        GhGroupEnumConstants.Config,
        GhGroupEnumConstants.Gist,
        GhGroupEnumConstants.Org,
        GhGroupEnumConstants.SshKey,
        GhGroupEnumConstants.GpgKey,
        GhGroupEnumConstants.Secret,
        GhGroupEnumConstants.Variable,
        GhGroupEnumConstants.Cache,
        GhGroupEnumConstants.Ruleset,
        GhGroupEnumConstants.Codespace,
        GhGroupEnumConstants.Discussion,
        GhGroupEnumConstants.Project,
        GhGroupEnumConstants.Alias,
        GhGroupEnumConstants.Extension,
        GhGroupEnumConstants.Browse,
        GhGroupEnumConstants.Status,
        GhGroupEnumConstants.Licenses,
    ];

    /// <summary>单级命令 — 工具名就是 <c>gh_{group}</c>，不需要 action（如 gh_api/gh_browse/gh_status/gh_licenses）。</summary>
    private static readonly string[] SingleLevelGroups = ["api", "browse", "status", "licenses"];

    /// <summary>
    /// 解析 <c>jcc gh ...</c> 命令行（<paramref name="args"/>[0] 为子命令名 <c>gh</c>）。
    /// </summary>
    /// <param name="args">完整命令行参数，首个元素必须是 <c>gh</c>。</param>
    /// <returns>解析成功返回结果；失败返回 null 并填充 <paramref name="error"/>。</returns>
    internal static GhResolvedCommand? Resolve(string[] args, out string? error) {
        error = null;

        // args[0] == "gh"，从 args[1] 开始取分组
        var group = NextPositional(args, 1);
        if (group is null) {
            error = MissingGroupError();
            return null;
        }

        // --json 或 --format json 可能出现在任意位置，先全局扫描，不参与位置参数计数
        var json = FlatSubCommandRouter.ShouldOutputJson(args);

        // 连字符分组名转下划线拼接工具名: ssh-key → gh_ssh_key_*
        var toolGroup = group.Replace('-', '_');

        // 单级命令: jcc gh api <path> / gh browse / gh status / gh licenses → gh_{group}
        if (SingleLevelGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
            return new GhResolvedCommand($"gh_{toolGroup}", group, null, CollectTail(args, 2), json);

        var action = NextPositional(args, 2);
        if (action is null) {
            error = MissingActionError(group);
            return null;
        }

        var toolName = $"gh_{toolGroup}_{action.Replace('-', '_')}";
        return new GhResolvedCommand(toolName, group, action, CollectTail(args, 3), json);
    }

    /// <summary>
    /// 从指定下标开始取下一个位置参数（跳过 <c>--option</c> 及其值）。
    /// </summary>
    private static string? NextPositional(string[] args, int startIndex) {
        for (var i = startIndex; i < args.Length; i++) {
            var token = args[i];
            if (token.StartsWith("--")) {
                // --key=value 自带值，不吞下一个 token
                if (i + 1 < args.Length && !token.Contains('=') && !args[i + 1].StartsWith("--"))
                    i++;
                continue;
            }
            return token;
        }
        return null;
    }

    /// <summary>
    /// 收集待绑定的剩余参数（跳过分组、动作与所有全局选项）。
    /// <para>全局选项（--json/--format/--help 等）对 gh 工具无意义，剥离后不传给 GhArgsBinder，
    /// 避免被报为未知选项。布尔标志剥单 token，带值选项剥 token+值（--key=value 形式只剥单 token）。</para>
    /// <para>宽容: 系统 gh CLI 的 --json field1,field2 转为 --json_fields=field1,field2（缺陷1a）。</para>
    /// </summary>
    private static string[] CollectTail(string[] args, int startIndex) {
        var tail = new List<string>(args.Length - startIndex);
        for (var i = startIndex; i < args.Length; i++) {
            var token = args[i];
            if (!token.StartsWith("--") || !CliArgCliOptionConstants.AllOptionNames.Contains(token)) {
                tail.Add(token);
                continue;
            }
            if (token.Contains('='))
                continue;
            if (CliArgCliOptionConstants.BooleanFlags.Contains(token)) {
                TryConvertJsonFieldsToTail(token, args, ref i, tail);
                continue;
            }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                i++;
        }
        return tail.ToArray();
    }

    /// <summary>
    /// 系统 gh CLI 用 <c>--json field1,field2</c> 表示 JSON 输出+字段选择，
    /// jcc 的 <c>--json</c> 只是 JSON 输出标志，字段用 <c>--json_fields</c>。
    /// 当 --json 后跟非选项值时，将其转为 <c>--json_fields=值</c> 加入 tail。
    /// </summary>
    private static void TryConvertJsonFieldsToTail(string token, string[] args, ref int i, List<string> tail) {
        if (!string.Equals(token, "--json", StringComparison.OrdinalIgnoreCase))
            return;
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            return;
        tail.Add($"--json_fields={args[i + 1]}");
        i++;
    }

    /// <summary>缺少分组时的 Rust 风格报错 + 用法。</summary>
    private static string MissingGroupError()
        => $"{CliErrorCatalog.ArgParseError("缺少 gh 分组").ToRustStyleString("gh")}\n{Usage}";

    /// <summary>缺少动作时的 Rust 风格报错 + 该分组用法。</summary>
    private static string MissingActionError(string group) {
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
          jcc gh run wait 123                    等待 Run 完成(指数退避轮询,完成才返回)
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
internal static class GhParamSchemaParser {
    /// <summary>
    /// 抽取参数列表：必填参数保持 required 声明顺序在前，其余按 properties 顺序在后。
    /// </summary>
    internal static List<GhParam> Parse(ToolSchema schema) {
        var result = new List<GhParam>(schema.Properties.Count);
        var requiredSet = new HashSet<string>(schema.Required, StringComparer.Ordinal);
        foreach (var required in schema.Required) {
            var isBoolean = schema.Properties.TryGetValue(required, out var prop)
                && string.Equals(prop.Type, "boolean", StringComparison.OrdinalIgnoreCase);
            result.Add(new GhParam(required, IsRequired: true, IsBoolean: isBoolean));
        }
        foreach (var (name, prop) in schema.Properties) {
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
internal static class GhArgsBinder {
    /// <summary>
    /// 执行绑定。
    /// </summary>
    /// <param name="tail">待绑定的剩余参数。</param>
    /// <param name="parameters">工具参数元信息。</param>
    /// <param name="toolName">工具名，仅用于报错文案。</param>
    /// <param name="error">失败时的 Rust 风格报错。</param>
    /// <returns>成功返回参数字典；失败返回 null。</returns>
    internal static Dictionary<string, string>? Bind(
        string[] tail, IReadOnlyList<GhParam> parameters, string toolName, out string? error) {
        error = null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var byName = new Dictionary<string, GhParam>(StringComparer.Ordinal);
        foreach (var p in parameters)
            byName[p.Name] = p;

        var slots = parameters.Where(p => p.IsRequired).ToList();
        slots.AddRange(GetOptionalPositionalSlots(toolName, parameters));
        var slotIndex = 0;

        for (var i = 0; i < tail.Length; i++) {
            var token = tail[i];
            if (!token.StartsWith("--")) {
                // 短选项: -X 或 -Xvalue (单 dash + 字母), 如 -L 5 / -L5 / -d
                if (token.Length >= 2 && token[0] == '-' && char.IsLetter(token[1])
                    && TryBindShortOption(token, toolName, tail, ref i, result, byName, out error))
                    continue;
                // 宽容策略: AI 习惯写 key=value(不带 -- 前缀),优先解析为命名参数
                if (TryBindBareKeyValue(token, byName, result))
                    continue;
                if (slotIndex >= slots.Count) {
                    error = TooManyPositionalError(toolName, token, parameters);
                    return null;
                }
                var slot = slots[slotIndex++];
                if (result.ContainsKey(slot.Name)) {
                    error = DuplicateParamError(slot.Name);
                    return null;
                }
                result[slot.Name] = token;
                continue;
            }

            var key = token[2..];
            string? inlineValue = null;
            var eqIdx = key.IndexOf('=');
            if (eqIdx > 0) {
                inlineValue = key[(eqIdx + 1)..];
                key = key[..eqIdx];
            }

            // 宽容策略: 真实 gh CLI 用连字符（--max-lines），工具 schema 用下划线（max_lines）
            if (!byName.TryGetValue(key, out var param) && !byName.TryGetValue(key.Replace('-', '_'), out param)) {
                // 宽容策略: 系统 gh CLI 缩写别名（--auto→auto_merge, --squash→merge_method=squash, --job→job_id 等）
                if (TryBindAlias(key, toolName, tail, ref i, result, token, inlineValue, out error))
                    continue;
                error = UnknownOptionError(key, parameters);
                return null;
            }
            key = param.Name;

            if (inlineValue is not null) {
                result[key] = param.IsBoolean ? NormalizeBoolValue(inlineValue) : inlineValue;
                continue;
            }

            if (param.IsBoolean) {
                // 宽容: --log true / --log false / --log 1 / --log 0 / --log yes / --log no
                if (i + 1 < tail.Length && TryParseBoolValue(tail[i + 1], out var boolVal)) {
                    result[key] = boolVal;
                    i++;
                } else {
                    result[key] = "true";
                }
                continue;
            }

            if (i + 1 >= tail.Length || tail[i + 1].StartsWith("--")) {
                error = $"{CliErrorCatalog.ArgMissingRequired($"--{key} 的值").ToRustStyleString(token)}\n提示: 用法 --{key} <值> 或 --{key}=<值>";
                return null;
            }

            result[key] = tail[i + 1];
            i++;
        }

        var missing = slots.FirstOrDefault(s => s.IsRequired && !result.ContainsKey(s.Name));
        if (missing is not null) {
            error = MissingPositionalError(toolName, missing.Name, slots);
            return null;
        }

        return result;
    }

    /// <summary>
    /// 系统 gh CLI 缩写别名 — 三种映射模式：
    /// <list type="bullet">
    /// <item><see cref="AliasKind.FixedValue"/>: 固定值（--auto → auto_merge=true）</item>
    /// <item><see cref="AliasKind.TakeNextToken"/>: 取下一 token 作值（--job 456 → job_id=456）</item>
    /// <item><see cref="AliasKind.RenameOnly"/>: 仅重命名，值按 inlineValue 或 bool flag 逻辑（--enable-issues → has_issues=true）</item>
    /// </list>
    /// </summary>
    private enum AliasKind { FixedValue, TakeNextToken, RenameOnly }

    /// <summary>系统 gh CLI 缩写别名描述。</summary>
    /// <param name="TargetKey">jcc 工具参数名。</param>
    /// <param name="FixedValue">固定值（仅 <see cref="AliasKind.FixedValue"/> 模式非 null）。</param>
    /// <param name="Kind">映射模式。</param>
    private sealed record GhCliAlias(string TargetKey, string? FixedValue, AliasKind Kind);

    /// <summary>某 gh 工具的长选项别名组 — 按 gh CLI 选项名查 <see cref="GhCliAlias"/>。</summary>
    private sealed record GhCliAliasGroup(params (string Key, GhCliAlias Alias)[] Pairs) {
        private readonly FrozenDictionary<string, GhCliAlias> _map = Pairs.ToFrozenDictionary(p => p.Key, p => p.Alias);
        internal GhCliAlias? Resolve(string key) => _map.TryGetValue(key, out var a) ? a : null;
    }

    /// <summary>某 gh 工具的短选项映射组 — 按短选项字符查 jcc 工具参数名。</summary>
    private sealed record GhShortOptionGroup(params (char Key, string LongName)[] Pairs) {
        private readonly FrozenDictionary<char, string> _map = Pairs.ToFrozenDictionary(p => p.Key, p => p.LongName);
        internal string? Resolve(char key) => _map.TryGetValue(key, out var n) ? n : null;
    }

    /// <summary>某 gh 工具的短选项固定值别名组 — 按短选项字符查 <see cref="GhCliAlias"/>。</summary>
    private sealed record GhShortAliasGroup(params (char Key, GhCliAlias Alias)[] Pairs) {
        private readonly FrozenDictionary<char, GhCliAlias> _map = Pairs.ToFrozenDictionary(p => p.Key, p => p.Alias);
        internal GhCliAlias? Resolve(char key) => _map.TryGetValue(key, out var a) ? a : null;
    }

    /// <summary>
    /// 系统 gh CLI 长选项别名表 — key 为工具名，value 为该工具的别名组。
    /// AI 习惯用真实 gh CLI 的 --auto/--squash/--failed/--job 等，映射到 jcc 工具参数。
    /// </summary>
    private static readonly FrozenDictionary<string, GhCliAliasGroup> LongAliasMap = new Dictionary<string, GhCliAliasGroup> {
        ["gh_pr_merge"] = new(
            ("auto",          new GhCliAlias("auto_merge", "true", AliasKind.FixedValue)),
            ("squash",        new GhCliAlias("merge_method", "squash", AliasKind.FixedValue)),
            ("merge",         new GhCliAlias("merge_method", "merge", AliasKind.FixedValue)),
            ("rebase",        new GhCliAlias("merge_method", "rebase", AliasKind.FixedValue)),
            ("delete-branch", new GhCliAlias("delete_branch", null, AliasKind.RenameOnly))
        ),
        ["gh_pr_close"] = new(
            ("delete-branch", new GhCliAlias("delete_branch", "true", AliasKind.FixedValue))
        ),
        ["gh_pr_review"] = new(
            ("approve",        new GhCliAlias("action", "approve", AliasKind.FixedValue)),
            ("request-changes",new GhCliAlias("action", "request_changes", AliasKind.FixedValue)),
            ("comment",        new GhCliAlias("action", "comment", AliasKind.FixedValue))
        ),
        ["gh_pr_create"] = new(
            ("fill",         new GhCliAlias("fill", "true", AliasKind.FixedValue)),
            ("fill-first",   new GhCliAlias("fill_first", "true", AliasKind.FixedValue)),
            ("fill-verbose", new GhCliAlias("fill_verbose", "true", AliasKind.FixedValue))
        ),
        ["gh_run_rerun"] = new(("failed", new GhCliAlias("failed_only", "true", AliasKind.FixedValue))),
        ["gh_run_view"]  = new(("job", new GhCliAlias("job_id", null, AliasKind.TakeNextToken))),
        ["gh_run_list"]  = new(("event", new GhCliAlias("event_type", null, AliasKind.TakeNextToken))),
        ["gh_repo_create"] = new(
            ("private",  new GhCliAlias("visibility", "private", AliasKind.FixedValue)),
            ("public",   new GhCliAlias("visibility", "public", AliasKind.FixedValue)),
            ("internal", new GhCliAlias("visibility", "internal", AliasKind.FixedValue)),
            ("source",   new GhCliAlias("source", null, AliasKind.TakeNextToken)),
            ("clone",    new GhCliAlias("clone", "true", AliasKind.FixedValue)),
            ("push",     new GhCliAlias("push", "true", AliasKind.FixedValue)),
            ("template", new GhCliAlias("template", null, AliasKind.TakeNextToken))
        ),
        ["gh_repo_edit"] = new(
            ("enable-issues",   new GhCliAlias("has_issues", null, AliasKind.RenameOnly)),
            ("enable-wiki",     new GhCliAlias("has_wiki", null, AliasKind.RenameOnly)),
            ("enable-projects", new GhCliAlias("has_projects", null, AliasKind.RenameOnly))
        ),
        ["gh_repo_deploy_key_add"] = new(("allow-write", new GhCliAlias("allow_write", null, AliasKind.RenameOnly))),
        ["gh_issue_close"] = new(
            ("duplicate",   new GhCliAlias("duplicate_of", null, AliasKind.TakeNextToken)),
            ("completed",   new GhCliAlias("reason", "completed", AliasKind.FixedValue)),
            ("not-planned", new GhCliAlias("reason", "not_planned", AliasKind.FixedValue))
        ),
        ["gh_release_create"] = new(("latest", new GhCliAlias("make_latest", null, AliasKind.RenameOnly))),
        ["gh_release_edit"]   = new(("latest", new GhCliAlias("make_latest", null, AliasKind.RenameOnly))),
    }.ToFrozenDictionary();

    /// <summary>查长选项别名表。</summary>
    private static GhCliAlias? ResolveGhCliAlias(string key, string toolName)
        => LongAliasMap.TryGetValue(toolName, out var group) ? group.Resolve(key) : null;

    /// <summary>
    /// 获取工具的可选位置参数 — 某些 gh CLI 命令的 optional 参数可作位置参数传递（如 gh repo clone owner/repo target-dir）。
    /// 返回按声明顺序排列的 GhParam 列表，追加到 required 位置参数槽位之后。
    /// </summary>
    private static List<GhParam> GetOptionalPositionalSlots(string toolName, IReadOnlyList<GhParam> parameters)
        => toolName switch {
            "gh_repo_clone"   => parameters.Where(p => p.Name == "dir").ToList(),
            "gh_pr_checkout"  => parameters.Where(p => p.Name == "branch").ToList(),
            _ => []
        };

    /// <summary>尝试绑定系统 gh CLI 别名 — 成功返回 true 并更新 result/i，失败设 error 返回 false</summary>
    /// <param name="inlineValue">--key=value 形式的内联值（已由调用方剥离），null 表示无内联值。</param>
    private static bool TryBindAlias(string key, string toolName, string[] tail, ref int i,
        Dictionary<string, string> result, string token, string? inlineValue, out string? error) {
        error = null;
        if (ResolveGhCliAlias(key, toolName) is not { } alias)
            return false;
        switch (alias.Kind) {
            case AliasKind.FixedValue:
                result[alias.TargetKey] = alias.FixedValue!;
                return true;
            case AliasKind.RenameOnly:
                result[alias.TargetKey] = inlineValue ?? "true";
                return true;
            default: // TakeNextToken
                if (i + 1 >= tail.Length || tail[i + 1].StartsWith("--")) {
                    error = $"{CliErrorCatalog.ArgMissingRequired($"--{key} 的值").ToRustStyleString(token)}\n提示: 用法 --{key} <值>";
                    return false;
                }
                result[alias.TargetKey] = tail[i + 1];
                i++;
                return true;
        }
    }

    /// <summary>
    /// 短选项固定值别名表 — key 为工具名，value 为该工具的短选项别名组。
    /// 如 <c>-m</c>→<c>merge_method=merge</c>, <c>-a</c>→<c>action=approve</c>。
    /// </summary>
    private static readonly FrozenDictionary<string, GhShortAliasGroup> ShortAliasMap = new Dictionary<string, GhShortAliasGroup> {
        ["gh_pr_merge"] = new(
            ('m', new GhCliAlias("merge_method", "merge", AliasKind.FixedValue)),
            ('r', new GhCliAlias("merge_method", "rebase", AliasKind.FixedValue)),
            ('s', new GhCliAlias("merge_method", "squash", AliasKind.FixedValue))
        ),
        ["gh_pr_review"] = new(
            ('a', new GhCliAlias("action", "approve", AliasKind.FixedValue)),
            ('c', new GhCliAlias("action", "comment", AliasKind.FixedValue)),
            ('r', new GhCliAlias("action", "request_changes", AliasKind.FixedValue))
        ),
    }.ToFrozenDictionary();

    /// <summary>查短选项固定值别名表。</summary>
    private static GhCliAlias? ResolveGhShortOptionAlias(char shortKey, string toolName)
        => ShortAliasMap.TryGetValue(toolName, out var group) ? group.Resolve(shortKey) : null;

    /// <summary>
    /// 歧义短选项映射表 — 仅放计算规则无法消解的歧义项。
    /// key 为工具名，value 为该工具的短选项组。
    /// </summary>
    private static readonly FrozenDictionary<string, GhShortOptionGroup> ShortOptionMap = new Dictionary<string, GhShortOptionGroup> {
        ["gh_pr_list"]        = new(('d', "draft")),
        ["gh_pr_view"]        = new(('c', "comments")),
        ["gh_pr_create"]      = new(('d', "draft"), ('e', "editor"), ('f', "fill"), ('p', "project"), ('r', "reviewer")),
        ["gh_pr_merge"]       = new(('A', "author_email"), ('b', "body"), ('F', "body_file"), ('d', "delete_branch"), ('t', "subject")),
        ["gh_pr_review"]      = new(('b', "body"), ('F', "body_file")),
        ["gh_pr_checkout"]    = new(('b', "branch"), ('f', "force")),
        ["gh_pr_checks"]      = new(('i', "interval")),
        ["gh_pr_close"]       = new(('c', "comment"), ('d', "delete_branch")),
        ["gh_pr_diff"]        = new(('e', "exclude")),
        ["gh_pr_lock"]        = new(('r', "reason")),
        ["gh_pr_reopen"]      = new(('c', "comment")),
        ["gh_issue_list"]     = new(('m', "milestone")),
        ["gh_issue_view"]     = new(('c', "comments")),
        ["gh_issue_create"]   = new(('e', "editor"), ('p', "project")),
        ["gh_issue_close"]    = new(('c', "comment"), ('r', "reason")),
        ["gh_issue_reopen"]   = new(('c', "comment")),
        ["gh_issue_lock"]     = new(('r', "reason")),
        ["gh_repo_list"]      = new(('l', "language")),
        ["gh_repo_view"]      = new(('b', "branch")),
        ["gh_repo_create"]    = new(('c', "clone"), ('d', "description"), ('g', "gitignore"), ('h', "homepage"), ('l', "license"), ('n', "name"), ('p', "template"), ('r', "remote"), ('s', "source"), ('t', "team")),
        ["gh_repo_edit"]      = new(('d', "description"), ('h', "homepage")),
        ["gh_repo_clone"]     = new(('u', "upstream_remote_name")),
        ["gh_repo_rename"]    = new(('y', "yes")),
        ["gh_repo_archive"]   = new(('y', "yes")),
        ["gh_repo_unarchive"] = new(('y', "yes")),
        ["gh_repo_set_default"] = new(('u', "unset"), ('v', "view")),
        ["gh_repo_sync"]      = new(('b', "branch"), ('s', "source")),
        ["gh_repo_deploy_key_add"] = new(('w', "allow_write"), ('t', "title")),
        ["gh_run_list"]       = new(('a', "all"), ('b', "branch"), ('c', "commit"), ('e', "event"), ('u', "user"), ('w', "workflow")),
        ["gh_run_view"]       = new(('a', "attempt"), ('j', "job"), ('l', "log"), ('v', "verbose")),
        ["gh_run_rerun"]      = new(('d', "debug"), ('j', "job")),
        ["gh_run_download"]   = new(('n', "name"), ('p', "pattern")),
        ["gh_run_watch"]      = new(('i', "interval")),
        ["gh_workflow_list"]  = new(('a', "all")),
        ["gh_workflow_view"]  = new(('r', "ref"), ('y', "yaml")),
        ["gh_workflow_run"]   = new(('f', "raw_field"), ('r', "ref")),
        ["gh_release_list"]   = new(('O', "order")),
        ["gh_release_create"] = new(('d', "draft"), ('n', "notes"), ('p', "prerelease")),
        ["gh_release_edit"]   = new(('n', "notes")),
        ["gh_release_delete"] = new(('y', "yes")),
        ["gh_release_download"] = new(('A', "archive"), ('O', "output"), ('p', "pattern")),
        ["gh_release_delete_asset"] = new(('y', "yes")),
        ["gh_label_create"]   = new(('c', "color"), ('d', "description"), ('f', "force")),
        ["gh_label_edit"]     = new(('c', "color"), ('d', "description"), ('n', "name")),
        ["gh_label_clone"]    = new(('f', "force")),
        ["gh_gist_view"]      = new(('f', "filename"), ('r', "raw")),
        ["gh_gist_create"]    = new(('d', "desc"), ('f', "filename"), ('p', "public")),
        ["gh_gist_edit"]      = new(('a', "add"), ('d', "desc"), ('f', "filename"), ('r', "remove")),
        ["gh_secret_list"]    = new(('a', "app"), ('e', "env"), ('u', "user")),
        ["gh_secret_set"]     = new(('a', "app"), ('b', "body"), ('e', "env"), ('f', "env_file"), ('r', "repos"), ('u', "user"), ('v', "visibility")),
        ["gh_secret_delete"]  = new(('a', "app"), ('e', "env"), ('u', "user")),
        ["gh_variable_list"]  = new(('e', "env")),
        ["gh_variable_set"]   = new(('b', "body"), ('e', "env"), ('f', "env_file"), ('r', "repos"), ('v', "visibility")),
        ["gh_variable_delete"] = new(('e', "env")),
        ["gh_ssh_key_add"]    = new(('t', "title")),
        ["gh_ssh_key_delete"] = new(('y', "yes")),
        ["gh_gpg_key_add"]    = new(('t', "title")),
        ["gh_gpg_key_delete"] = new(('y', "yes")),
        ["gh_api"]            = new(('H', "header"), ('i', "include"), ('X', "method"), ('p', "preview"), ('f', "fields")),
        ["gh_auth_status"]    = new(('a', "active"), ('h', "hostname"), ('t', "show_token")),
        ["gh_auth_token"]     = new(('h', "hostname"), ('u', "user")),
        ["gh_config_get"]     = new(('h', "host")),
        ["gh_config_set"]     = new(('h', "host")),
        ["gh_alias_set"]      = new(('s', "shell")),
        ["gh_ruleset_list"]   = new(('p', "parents")),
        ["gh_ruleset_view"]   = new(('p', "parents")),
        ["gh_cache_list"]     = new(('k', "key"), ('O', "order"), ('r', "ref"), ('S', "sort")),
        ["gh_cache_delete"]   = new(('a', "all"), ('r', "ref")),
        ["gh_codespace_list"] = new(('u', "user")),
        ["gh_codespace_view"] = new(('c', "codespace")),
        ["gh_codespace_create"] = new(('b', "branch"), ('d', "display_name"), ('l', "location"), ('m', "machine"), ('s', "status")),
        ["gh_codespace_delete"] = new(('c', "codespace"), ('f', "force"), ('u', "user")),
        ["gh_codespace_stop"] = new(('c', "codespace"), ('u', "user")),
        ["gh_codespace_ssh"]  = new(('c', "codespace"), ('d', "debug")),
        ["gh_codespace_cp"]   = new(('c', "codespace"), ('e', "expand"), ('p', "profile"), ('r', "recursive")),
        ["gh_codespace_logs"] = new(('c', "codespace"), ('f', "follow")),
        ["gh_codespace_code"] = new(('c', "codespace")),
        ["gh_codespace_rebuild"] = new(('c', "codespace")),
    }.ToFrozenDictionary();

    /// <summary>
    /// 系统 gh CLI 短选项映射 — 优先用计算规则处理通用映射（按工具名前缀/后缀），
    /// 仅歧义项查 <see cref="ShortOptionMap"/> 字典。
    /// </summary>
    private static string? ResolveGhShortOption(char shortKey, string toolName) {
        var isList = toolName.EndsWith("_list");
        var isView = toolName.EndsWith("_view");
        var isCreateOrEdit = toolName.EndsWith("_create") || toolName.EndsWith("_edit");

        // 通用: -L→limit, -w→web
        if (shortKey == 'L' && isList) return "limit";
        if (shortKey == 'w' && isView) return "web";

        var isPr = toolName.StartsWith("gh_pr_");
        var isIssue = toolName.StartsWith("gh_issue_");
        var isPrOrIssue = isPr || isIssue;
        var isRelease = toolName.StartsWith("gh_release_");
        var isSecretOrVar = toolName.StartsWith("gh_secret_") || toolName.StartsWith("gh_variable_");
        var isCodespace = toolName.StartsWith("gh_codespace_");
        var isRuleset = toolName.StartsWith("gh_ruleset_");

        // pr/issue 通用短选项
        if (isPrOrIssue) {
            if (shortKey == 'a' && (isList || toolName.EndsWith("_create"))) return "assignee";
            if (shortKey == 'A' && isList) return "author";
            if (shortKey == 'l' && (isList || toolName.EndsWith("_create"))) return "label";
            if (shortKey == 'm') return "milestone";
            if (shortKey == 'b' && isCreateOrEdit) return "body";
            if (shortKey == 'F') return "body_file";
            if (shortKey == 't' && isCreateOrEdit) return "title";
            if (shortKey == 's' && isList) return "state";
            if (shortKey == 'S' && isList) return "search";
        }

        // pr 通用: -B→base (list/create/edit), -H→head (list/create)
        if (isPr) {
            if (shortKey == 'B') return "base";
            if (shortKey == 'H' && (isList || toolName.EndsWith("_create"))) return "head";
        }
        if (toolName == "gh_search_prs" && (shortKey == 'B' || shortKey == 'H'))
            return shortKey == 'B' ? "base" : "head";

        // release: -F→notes_file, -t→title
        if (isRelease) {
            if (shortKey == 'F') return "notes_file";
            if (shortKey == 't' && isCreateOrEdit) return "title";
        }

        // api: -F→fields, workflow run: -F→field
        if (shortKey == 'F' && toolName == "gh_api") return "fields";
        if (shortKey == 'F' && toolName == "gh_workflow_run") return "field";

        // run list: -s→status
        if (shortKey == 's' && toolName == "gh_run_list") return "status";

        // secret/variable/codespace/ruleset: -o→org
        if (shortKey == 'o' && (isSecretOrVar || isCodespace || isRuleset)) return "org";

        // run/release download: -D→dir
        if (shortKey == 'D' && toolName.EndsWith("_download") && (toolName.StartsWith("gh_run_") || isRelease)) return "dir";

        // 歧义项查字典
        return ShortOptionMap.TryGetValue(toolName, out var group) ? group.Resolve(shortKey) : null;
    }

    /// <summary>尝试绑定系统 gh CLI 短选项 — 成功返回 true 并更新 result/i，失败返回 false（不设 error，fall through 到位置参数）</summary>
    private static bool TryBindShortOption(string token, string toolName, string[] tail, ref int i,
        Dictionary<string, string> result, Dictionary<string, GhParam> byName, out string? error) {
        error = null;
        var shortKey = token[1];
        var shortInline = token.Length > 2 ? token[2..] : null;
        if (ResolveGhShortOptionAlias(shortKey, toolName) is { } shortAlias) {
            switch (shortAlias.Kind) {
                case AliasKind.FixedValue:
                    result[shortAlias.TargetKey] = shortAlias.FixedValue!;
                    return true;
                case AliasKind.RenameOnly:
                    result[shortAlias.TargetKey] = shortInline ?? "true";
                    return true;
                default: // TakeNextToken
                    if (shortInline is not null) {
                        result[shortAlias.TargetKey] = shortInline;
                        return true;
                    }
                    if (i + 1 < tail.Length && !tail[i + 1].StartsWith("-")) {
                        result[shortAlias.TargetKey] = tail[i + 1];
                        i++;
                        return true;
                    }
                    result[shortAlias.TargetKey] = "true";
                    return true;
            }
        }
        if (ResolveGhShortOption(shortKey, toolName) is not { } longName)
            return false;
        // 重复短选项追加(逗号分隔): -f name=test -f color=ff0000 → fields="name=test,color=ff0000"
        if (shortInline is not null) {
            result[longName] = result.TryGetValue(longName, out var prev1) ? $"{prev1},{shortInline}" : shortInline;
            return true;
        }
        if (byName.TryGetValue(longName, out var param) && param.IsBoolean) {
            result[longName] = "true";
            return true;
        }
        if (i + 1 < tail.Length && !tail[i + 1].StartsWith("-")) {
            var val = tail[i + 1];
            result[longName] = result.TryGetValue(longName, out var prev2) ? $"{prev2},{val}" : val;
            i++;
            return true;
        }
        result[longName] = "true";
        return true;
    }

    /// <summary>找最接近的参数名: 优先前缀匹配(如 auto→auto_merge),其次包含匹配(如 merge→merge_method),最后 key 包含参数名(如 add-label→label)。</summary>
    private static string? SuggestOption(string key, IReadOnlyList<GhParam> parameters) {
        var prefix = parameters.FirstOrDefault(p => p.Name.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        if (prefix is not null) return prefix.Name;
        var contains = parameters.FirstOrDefault(p => p.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
        if (contains is not null) return contains.Name;
        var keyContains = parameters.FirstOrDefault(p => key.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
        return keyContains?.Name;
    }

    /// <summary>
    /// 尝试解析不带 -- 前缀的 key=value 格式 — AI 习惯写 job_id=123 而非 --job_id 123
    /// <para>宽容策略: 精确匹配或连字符→下划线转换(如 max-lines=50 → max_lines=50)</para>
    /// </summary>
    private static bool TryBindBareKeyValue(string token, Dictionary<string, GhParam> byName, Dictionary<string, string> result) {
        var eqIdx = token.IndexOf('=');
        if (eqIdx <= 0) return false;
        var bareKey = token[..eqIdx];
        var bareValue = token[(eqIdx + 1)..];
        if (!byName.TryGetValue(bareKey, out var param) && !byName.TryGetValue(bareKey.Replace('-', '_'), out param))
            return false;
        result[param.Name] = param.IsBoolean ? NormalizeBoolValue(bareValue) : bareValue;
        return true;
    }

    private static string TooManyPositionalError(string toolName, string token, IReadOnlyList<GhParam> parameters) {
        var names = string.Join(", ", parameters.Select(p => p.Name));
        var hint = "非位置参数请用 --参数名 值 的形式";
        // key=value 诱导: AI 习惯写 key=value 不带 -- 前缀
        if (token.Contains('=')) {
            var eqIdx = token.IndexOf('=');
            var key = token[..eqIdx];
            hint = $"参数名需加 -- 前缀,正确写法: --{key} {token[(eqIdx + 1)..]} 或 --{token}";
        }
        // --json 诱导: jcc 默认 JSON 输出,--json 被剥离后字段列表(含逗号)变位置参数
        else if (token.Contains(','))
            hint += "\n提示: jcc 默认 JSON 输出,不需要 --json;如需 text 格式用 --format text";
        return $"{CliErrorCatalog.ArgParseError($"多余的位置参数: {token}").ToRustStyleString(token)}\n"
             + $"{toolName} 接受的参数: {names}\n提示: {hint}";
    }

    private static string DuplicateParamError(string name)
        => CliErrorCatalog.ArgParseError($"参数重复指定: {name}（位置参数与 --{name} 只能二选一）").ToRustStyleString($"--{name}");

    private static string UnknownOptionError(string key, IReadOnlyList<GhParam> parameters) {
        var names = string.Join(", ", parameters.Select(p => $"--{p.Name}"));
        var hint = $"可用选项: {names}";
        if (SuggestOption(key, parameters) is { } suggestion)
            hint += $"\n你是不是想用 --{suggestion}?";
        return $"{CliErrorCatalog.ArgUnknownOption($"--{key}").ToRustStyleString($"--{key}")}\n{hint}";
    }

    private static string MissingPositionalError(string toolName, string missingName, IReadOnlyList<GhParam> slots) {
        var positionalHint = string.Join(' ', slots.Select(s => $"<{s.Name}>"));
        return $"{CliErrorCatalog.ArgMissingRequired(missingName).ToRustStyleString(toolName)}\n用法: {positionalHint}（示例见 jcc gh --help）";
    }

    /// <summary>
    /// 尝试解析布尔值 — 宽容接受 true/false/1/0/yes/no（不区分大小写）。
    /// 返回 false 表示 token 不是布尔值，调用方应将其视为位置参数或选项值。
    /// </summary>
    private static bool TryParseBoolValue(string token, out string result) {
        switch (token.ToLowerInvariant()) {
            case "true" or "1" or "yes" or "on":
                result = "true";
                return true;
            case "false" or "0" or "no" or "off":
                result = "false";
                return true;
            default:
                result = "true";
                return false;
        }
    }

    /// <summary>归一化布尔值 — 用于 --key=value 内联形式（--log=1 → true, --log=0 → false）。</summary>
    private static string NormalizeBoolValue(string value)
        => TryParseBoolValue(value, out var result) ? result : value;
}