namespace Core.Prompts.Sections;

/// <summary>
/// 工具使用部分 —— 首轮只声明 <c>resources/tools/base.yaml</c> 里的 4 个基础工具（ADR 0157 步骤 8），
/// 其余能力是独立 CLI 工具节点，由 LLM 用 bash 按需调用。
/// <para>本段文案由 <see cref="BaseToolRegistry"/> 与 <see cref="CliGroupIndex"/> 驱动：前者决定推荐哪些
/// 首轮工具，后者决定列出哪些 exe。两者都是生成物，提示词里不手写任何工具名或组名 ——
/// 手写清单必然漂移成「提示词让你用一个根本不存在的命令」或「新加的组模型永远发现不了」。</para>
/// </summary>
[PromptSection(Name = "tools")]
public static class ToolsSection {
    /// <summary>
    /// 获取工具使用部分的提示词内容。根据 base.yaml 的实际声明拼接工具使用指导。
    /// </summary>
    /// <returns>工具使用指导文本。</returns>
    public static string? GetContent() {
        var registry = BaseToolRegistry.LoadDefault();
        var declared = registry.All
            .Select(tool => tool.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<string>();

        var direct = new List<string>();
        if (declared.Contains(FileToolNameEnumConstants.FileRead))
            direct.Add($"使用 {FileToolNameEnumConstants.FileRead} 读取文件，而不是 cat、head、tail 或 sed");
        if (declared.Contains(FileToolNameEnumConstants.FileEdit))
            direct.Add($"使用 {FileToolNameEnumConstants.FileEdit} 做局部修改，而不是 sed 或 awk");
        if (declared.Contains(FileToolNameEnumConstants.FileWrite))
            direct.Add($"使用 {FileToolNameEnumConstants.FileWrite} 创建或整体覆写文件，而不是 heredoc 或 echo 重定向");
        if (direct.Count > 0) {
            // 一条 item 内含多行：逐行 AddRange 会被外层统一加 “ - ” 前缀，渲染成 “ -   - xxx” 的双项目符号。
            items.Add("首轮直接可用的文件工具（本清单外不要再假设同名工具存在）：" + Environment.NewLine
                + string.Join(Environment.NewLine, direct.Select(line => "   - " + line)));
        }

        AddAlwaysRegisteredTools(items, registry.All);

        if (declared.Contains("bash")) {
            items.Add("bash 是本轮唯一的通用执行入口：既用于系统命令，也用于探索和调用其余全部 CLI 工具节点。这不是退而求其次，而是设计上的唯一通道 —— 该用就用。");
        }

        AddGroupCatalog(items);
        items.Add("用法四条：① `<exe> -h` 看该组工具清单（大组只给索引）② `<exe> -h <关键词>` 在组内检索 ③ `<exe> <工具> -h` 看单个工具的参数表 ④ `jcc tool call <组> <工具> --参数 值` 执行（组名可省略，给工具名会自动反查所属分组）。");
        items.Add("记不清归哪个组时：`jcc tool tree` 看全部分组，`jcc tool find <关键词>` 按关键词搜，命中会给出分组与工具名。");
        items.Add("探索一次就记住：同一任务内不要反复 tree/find 同一个关键词，查到就往下做；探索结果用完即弃，不要复述进回答。");
        items.Add($"上面这些组就是能力的全部范围 —— 组里搜不到才是真没有；不要因为首轮清单没列出某个能力就断言它不存在。");
        items.Add("当专用 CLI 节点能完成时优先用它，而不是用裸 shell 命令硬拼（例如用文件节点的 edit 而不是 sed -i），这样用户能看懂并审查你的改动。");
        items.Add("您可以在单个响应中调用多个工具。如果多个调用之间没有依赖关系，请并行发起；如果后一个调用依赖前一个的结果，则必须顺序执行。尽可能并行以提高效率。");

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 使用您的工具");
        foreach (var item in items) {
            result.AppendLine($" - {item}");
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 追加「本轮 tools 字段里到底有哪些工具」的清单 —— 必须逐个列出，不能只举几个例子。
    /// <para>Agent 内部基础工具（规划模式、目标读写等）依赖活会话，既不能放进独立 exe，
    /// 也不能只在别处提到一句；它们必须每轮注册并出现在首轮提示词，否则模型不会调用。</para>
    /// </summary>
    private static void AddAlwaysRegisteredTools(List<string> items, IReadOnlyList<CliToolDescriptor> tools) {
        var lines = tools.Select(tool => $"   - {tool.Id}（{tool.Name}）— {tool.Summary}");
        items.Add($"本轮 tools 字段中始终注册的工具（共 {tools.Count} 个，无需再发现）：" + Environment.NewLine
            + string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// 追加「有哪些 exe」的目录段落 —— 一条 item 内含多行，避免逐行加前缀把缩进撑成五层。
    /// <para>没有索引（资源缺失或生成失败）时退化为一句指引，而不是让提示词里出现一个空的清单标题：
    /// 空标题会让模型以为「没有工具可用」。</para>
    /// <para>TASK100 S6：组清单按当前 profile 的 <c>tool-groups</c> 消费点裁剪。此前这里恒列全部 13 组，
    /// 于是「profile 关掉 github 组」只关了 CLI 侧，模型仍被告知有它 —— 提示词是这些 exe 的唯一入口，
    /// 不裁就等于没关。数字（组数/工具数/隔离数）按裁剪后的集合重算：留着全量数字会与行数自相矛盾。</para>
    /// </summary>
    private static void AddGroupCatalog(List<string> items) {
        var index = CliGroupIndex.LoadDefault();
        if (index.GroupCount == 0) {
            items.Add("分组索引未随包发布 —— 用 `jcc tool tree` 现场发现，不要假设某个命令存在。");
            return;
        }

        var enabled = PromptToolGroupScope.Enabled;
        var keep = enabled is null ? null : new HashSet<string>(enabled, StringComparer.Ordinal);
        var matcher = new CliToolScopeMatcher(PromptToolGroupScope.Domain);
        var restricted = matcher.HasRestriction;

        var groups = index.Groups
            .Where(group => keep is null || keep.Contains(group.Id))
            .Where(group => VisibleCount(group) > 0)
            .OrderBy(group => group.Id, StringComparer.Ordinal)
            .ToArray();

        if (groups.Length == 0) {
            // 「全被裁」与「索引缺失」是两件事：前者是配置/授权意愿，后者是打包问题，提示词里不能混成一句。
            items.Add(restricted
                ? "当前 agent 的工具域内没有独立 CLI 工具组 —— 只用首轮 tools 清单里的工具；越域调用会被集中鉴权拒绝。"
                : "当前 profile 未启用任何独立工具组 —— 只用首轮 tools 清单里的工具，不要假设其它命令存在。");
            return;
        }

        if (restricted) {
            var visibleCount = groups.Sum(VisibleCount);
            var restrictedHeader = $"其余能力按当前 agent 工具域裁剪后，分装在 {groups.Length} 个独立 exe 里（对当前 agent 可见 {visibleCount} 个工具）。"
                + "未列出的组或工具对当前 agent 不可执行 —— 先按定位挑组，再用 `<exe> -h` 按需读，不要预先整段读：";
            var restrictedLines = groups
                .Select(group => $"   - {group.Id}（{group.Exe}，可见 {VisibleCount(group)} 个）{group.Blurb}");
            items.Add(restrictedHeader + Environment.NewLine + string.Join(Environment.NewLine, restrictedLines));
            return;
        }

        var toolCount = groups.Sum(group => group.ToolCount);
        var isolatedCount = groups.Sum(group => group.IsolatedCount);
        var header = $"其余能力不在首轮 tools 清单里，而是分装在 {groups.Length} 个独立 exe 里（共 {toolCount} 个工具，"
            + $"其中 {isolatedCount} 个不经过宿主直接执行）。每个 exe 的 `-h` 就是它的说明书 —— 先按定位挑组，再按需读，不要预先整段读：";
        var lines = groups
            .Select(group => $"   - {group.Id}（{group.Exe}，{group.ToolCount} 个）{group.Blurb}");

        items.Add(header + Environment.NewLine + string.Join(Environment.NewLine, lines));

        int VisibleCount(CliGroupEntry group) =>
            group.ToolIds.Count == 0 ? group.ToolCount : matcher.CountVisible(group.ToolIds);
    }

    /// <summary>
    /// 创建工具使用 Section 实例（内容缓存）。
    /// </summary>
    /// <returns>工具使用 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("tools", GetContent);
}
