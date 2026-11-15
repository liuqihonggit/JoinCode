namespace Core.Prompts.Sections;

/// <summary>
/// 工具使用部分 —— 首轮只声明 <c>resources/tools/base.yaml</c> 里的 4 个基础工具（ADR 0157 步骤 8），
/// 其余 540+ 能力是独立 CLI 工具节点，由 LLM 用 bash 逐层发现与调用。
/// <para>本段文案由 <see cref="BaseToolRegistry"/> 驱动：只有真正进入协议 tools 字段的工具才会被推荐，
/// 避免出现「提示词让你用 Glob，但首轮清单里根本没有 Glob」这类自相矛盾。</para>
/// </summary>
[PromptSection(Name = "tools", Order = 11)]
public static class ToolsSection {
    /// <summary>
    /// 获取工具使用部分的提示词内容。根据 base.yaml 的实际声明拼接工具使用指导。
    /// </summary>
    /// <returns>工具使用指导文本。</returns>
    public static string? GetContent() {
        var declared = BaseToolRegistry.LoadDefault().All
            .Select(tool => tool.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<string>();

        var direct = new List<string>();
        if (declared.Contains("file_read"))
            direct.Add("使用 file_read 读取文件，而不是 cat、head、tail 或 sed");
        if (declared.Contains("file_edit"))
            direct.Add("使用 file_edit 做局部修改，而不是 sed 或 awk");
        if (declared.Contains("file_write"))
            direct.Add("使用 file_write 创建或整体覆写文件，而不是 heredoc 或 echo 重定向");
        if (direct.Count > 0) {
            items.Add("首轮直接可用的文件工具（本清单外不要再假设同名工具存在）：");
            items.AddRange(direct.Select(line => "  - " + line));
        }

        if (declared.Contains("bash")) {
            items.Add("bash 是本轮唯一的通用执行入口：既用于系统命令，也用于探索和调用其余全部 CLI 工具节点。这不是退而求其次，而是设计上的唯一通道 —— 该用就用。");
        }

        items.Add("其余能力（搜索、GitHub、Git、斜杠命令、媒体、系统等 540+ 工具）都不在首轮 tools 清单里，它们是独立可执行文件，用 bash 按下面四步发现与调用：");
        items.Add("  1. `jcc tool tree` —— 列出全部分组，先看有没有对口的组；");
        items.Add("  2. `jcc tool find <关键词>` —— 记不清归属时按关键词搜，命中会给出分组与工具名；");
        items.Add("  3. `jcc tool help <组>` —— 看该组全部工具与参数（也可直接 `<exe> -h`）；");
        items.Add("  4. `jcc tool call <组> <工具> --参数 值` —— 执行；组名可省略，给出工具名会自动反查所属分组。");
        items.Add("探索一次就记住：同一任务内不要反复 tree/find 同一个关键词，查到就往下做；探索结果用完即弃，不要复述进回答。");
        items.Add($"不要因为首轮清单没有列出某个能力就断言它不存在 —— 先用 `{ShellToolNameEnumConstants.Bash}` 跑 `jcc tool tree` 或 `jcc tool find` 确认。");
        items.Add("当专用 CLI 节点能完成时优先用它，而不是用裸 shell 命令硬拼（例如用文件节点的 file_edit 而不是 sed -i），这样用户能看懂并审查你的改动。");
        items.Add("您可以在单个响应中调用多个工具。如果多个调用之间没有依赖关系，请并行发起；如果后一个调用依赖前一个的结果，则必须顺序执行。尽可能并行以提高效率。");

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 使用您的工具");
        foreach (var item in items) {
            result.AppendLine($" - {item}");
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 创建工具使用 Section 实例（内容缓存）。
    /// </summary>
    /// <returns>工具使用 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("tools", GetContent);
}
