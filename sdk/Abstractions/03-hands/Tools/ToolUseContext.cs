namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具执行上下文 — 对齐 TS ToolUseContext
/// 包含技能执行时 contextModifier 可修改的字段，以及内容替换状态
/// </summary>
public sealed class ToolUseContext
{
    /// <summary>
    /// 允许的工具列表 — 对齐 TS toolPermissionContext.alwaysAllowRules.command
    /// 技能执行期间自动授权的工具
    /// </summary>
    public HashSet<string> AllowedTools { get; init; } = new();

    /// <summary>
    /// 模型覆盖 — 对齐 TS options.mainLoopModel
    /// 技能执行期间使用的模型
    /// </summary>
    public string? ModelOverride { get; set; }

    /// <summary>
    /// 推理努力级别 — 对齐 TS appState.effortValue
    /// 技能执行期间的推理努力级别
    /// </summary>
    public string? Effort { get; set; }

    /// <summary>
    /// 内容替换状态 — 对齐 TS ToolUseContext.contentReplacementState
    /// 主线程: REPL 初始化一次（永不清除 — 过期 UUID key 无害）
    /// 子智能体: 默认克隆父级状态（缓存共享 fork 需要相同决策）
    /// 恢复: 从 sidechain 记录重建
    /// </summary>
    public LLM.Chat.ContentReplacementState? ContentReplacementState { get; set; }

    /// <summary>
    /// 已调用的技能 — 对齐 TS STATE.invokedSkills
    /// 技能调用后注册，压缩时截断保留（5K token/技能, 25K总预算）
    /// key: 技能名, value: (技能路径, 技能内容, 调用时间)
    /// </summary>
    public Dictionary<string, InvokedSkillEntry> InvokedSkills { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 待确认的 sed 编辑 — 对齐 TS _simulatedSedEdit
    /// 首次 sed -i 调用时存储预计算的新内容，模型确认后二次调用时取出写入
    /// key: 文件路径, value: 预计算的新文件内容
    /// </summary>
    public Dictionary<string, PendingSedEdit> PendingSedEdits { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 最近读取的文件 — 对齐 TS compact post-compact file restoration
    /// key: 文件路径, value: 最后读取时间
    /// 压缩后重新读取最近 5 个文件注入上下文
    /// </summary>
    public Dictionary<string, DateTime> RecentlyReadFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册已调用的技能 — 对齐 TS addInvokedSkill
    /// </summary>
    public void AddInvokedSkill(string skillName, string? skillPath, string? skillContent)
    {
        InvokedSkills[skillName] = new InvokedSkillEntry
        {
            Name = skillName,
            Path = skillPath,
            Content = skillContent,
            InvokedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 记录文件读取 — 对齐 TS fileReadListeners 追踪最近读取的文件
    /// </summary>
    public void RecordFileRead(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            RecentlyReadFiles[filePath] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 生成压缩后文件恢复附件 — 对齐 TS createPostCompactFileAttachments
    /// 重新读取最近 5 个文件（50K token 预算），注入到压缩后的上下文中
    /// </summary>
    public async Task<string?> BuildPostCompactFileAttachmentsAsync(
        IFileSystem fs,
        int maxFiles = 5,
        int totalTokenBudget = 50000,
        CancellationToken cancellationToken = default)
    {
        if (RecentlyReadFiles.Count == 0) return null;

        var recentFiles = RecentlyReadFiles
            .OrderByDescending(kv => kv.Value)
            .Take(maxFiles)
            .Select(kv => kv.Key)
            .ToList();

        var sb = new StringBuilder();
        var totalTokens = 0;
        var charsPerToken = 4;

        foreach (var filePath in recentFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!fs.FileExists(filePath)) continue;

            try
            {
                var content = await fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(content)) continue;

                var contentTokens = content.Length / charsPerToken;
                if (totalTokens + contentTokens > totalTokenBudget) break;

                sb.AppendLine($"## File: {filePath}");
                sb.AppendLine(content);
                sb.AppendLine();

                totalTokens += contentTokens;
            }
            catch (Exception)
            {
                // 文件读取失败时跳过，不影响压缩流程
                System.Diagnostics.Trace.WriteLine($"[PostCompactFileTracker] 读取文件失败: {filePath}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// 清除已调用的技能 — 对齐 TS clearInvokedSkills
    /// </summary>
    public void ClearInvokedSkills() => InvokedSkills.Clear();

    /// <summary>
    /// 生成压缩保留附件 — 对齐 TS createSkillAttachmentIfNeeded
    /// 按调用时间降序排列，截断保留（5K token/技能, 25K总预算）
    /// </summary>
    public string? BuildInvokedSkillsAttachment(int maxTokensPerSkill = 5000, int totalTokenBudget = 25000)
    {
        if (InvokedSkills.Count == 0) return null;

        var ordered = InvokedSkills.Values
            .OrderByDescending(s => s.InvokedAt)
            .ToList();

        var sb = new StringBuilder();
        var totalTokens = 0;
        var charsPerToken = 4;

        foreach (var skill in ordered)
        {
            if (string.IsNullOrEmpty(skill.Content)) continue;

            var contentTokens = skill.Content.Length / charsPerToken;
            var truncated = contentTokens > maxTokensPerSkill
                ? skill.Content[..(maxTokensPerSkill * charsPerToken)] + "\n[... skill content truncated for compaction; use Read on the skill path if you need the full text]"
                : skill.Content;

            var entryTokens = truncated.Length / charsPerToken;
            if (totalTokens + entryTokens > totalTokenBudget) break;

            sb.AppendLine($"## Skill: {skill.Name}");
            if (!string.IsNullOrEmpty(skill.Path))
                sb.AppendLine($"Path: {skill.Path}");
            sb.AppendLine(truncated);
            sb.AppendLine();

            totalTokens += entryTokens;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// 应用技能的 contextModifier — 对齐 TS SkillTool.call() 中的 contextModifier
    /// 将技能定义的 AllowedTools/Model/Effort 合并到当前上下文
    /// </summary>
    public void ApplySkillModifier(Models.Skill.SkillDefinition skill)
    {
        // 合并 allowedTools — 对齐 TS: Set([...existing, ...skill.allowedTools])
        if (skill.AllowedTools.Count > 0)
        {
            AllowedTools.UnionWith(skill.AllowedTools);
        }

        // 覆盖 model — 对齐 TS: resolveSkillModelOverride(model, mainLoopModel)
        if (skill.Model is not null)
        {
            ModelOverride = skill.Model;
        }

        // 覆盖 effort — 对齐 TS: appState.effortValue = effort
        if (skill.Effort is not null)
        {
            Effort = skill.Effort;
        }

        // 注册已调用的技能 — 对齐 TS addInvokedSkill
        AddInvokedSkill(skill.Name, skill.SourcePath, skill.Steps.FirstOrDefault()?.Prompt);
    }
}

/// <summary>
/// 已调用的技能条目 — 对齐 TS STATE.invokedSkills Map entry
/// </summary>
public sealed class InvokedSkillEntry
{
    public required string Name { get; init; }
    public string? Path { get; init; }
    public string? Content { get; init; }
    public DateTime InvokedAt { get; init; }
}

/// <summary>
/// 待确认的 sed 编辑 — 对齐 TS _simulatedSedEdit
/// 存储 sed -i 预计算的替换结果，等待用户/模型确认后写入
/// </summary>
public sealed class PendingSedEdit
{
    /// <summary>
    /// 文件完整路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 原始内容
    /// </summary>
    public required string OldContent { get; init; }

    /// <summary>
    /// 替换后的新内容
    /// </summary>
    public required string NewContent { get; init; }

    /// <summary>
    /// sed 编辑信息
    /// </summary>
    public required Models.Shell.SedEditInfo SedInfo { get; init; }

    /// <summary>
    /// 创建时间（用于过期清理）
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
