namespace JoinCode.Abstractions.Models.Skill;

/// <summary>
/// 技能描述截断器 — 对齐 TS SkillTool/prompt.ts formatCommandsWithinBudget
/// 三层截断策略: 单条硬上限 → 预算内分区 → 极端退化
/// 上下文预算: 1% 上下文窗口 = 字符预算
/// </summary>
public static class SkillDescriptionTruncator
{
    /// <summary>
    /// 技能预算上下文百分比 — 对齐 TS SKILL_BUDGET_CONTEXT_PERCENT
    /// 技能列表占用上下文窗口的 1%
    /// </summary>
    public const double SkillBudgetContextPercent = 0.01;

    /// <summary>
    /// 每 token 字符数 — 对齐 TS CHARS_PER_TOKEN
    /// </summary>
    public const int CharsPerToken = 4;

    /// <summary>
    /// 最大列表描述字符数 — 对齐 TS MAX_LISTING_DESC_CHARS
    /// </summary>
    public const int MaxListingDescChars = 250;

    /// <summary>
    /// 最小描述长度 — 对齐 TS MIN_DESC_LENGTH
    /// 低于此值则退化为仅显示名称
    /// </summary>
    public const int MinDescLength = 20;

    /// <summary>
    /// 默认字符预算 — 对齐 TS DEFAULT_CHAR_BUDGET
    /// 1% of 200k × 4 = 8,000
    /// </summary>
    public const int DefaultCharBudget = 8_000;

    /// <summary>
    /// 获取字符预算 — 对齐 TS getCharBudget
    /// 优先级: 环境变量覆盖 → 上下文窗口计算 → 默认值
    /// </summary>
    /// <param name="contextWindowTokens">上下文窗口 token 数（可选）</param>
    /// <returns>字符预算</returns>
    public static int GetCharBudget(int? contextWindowTokens = null)
    {
        // 环境变量覆盖 — 对齐 TS SLASH_COMMAND_TOOL_CHAR_BUDGET
        var envValue = Environment.GetEnvironmentVariable(JccEnvVarConstants.SkillCharBudget);
        if (int.TryParse(envValue, out var envBudget) && envBudget > 0)
        {
            return envBudget;
        }

        // 上下文窗口计算: tokens × 4 × 1%
        if (contextWindowTokens.HasValue && contextWindowTokens.Value > 0)
        {
            return (int)Math.Floor(contextWindowTokens.Value * CharsPerToken * SkillBudgetContextPercent);
        }

        return DefaultCharBudget;
    }

    /// <summary>
    /// 在字符预算内格式化技能列表 — 对齐 TS formatCommandsWithinBudget
    /// bundled 技能（内置）永远不截断，其余技能共享剩余预算
    /// </summary>
    /// <param name="skills">技能列表</param>
    /// <param name="contextWindowTokens">上下文窗口 token 数（可选，用于动态计算预算）</param>
    /// <returns>格式化后的技能列表文本</returns>
    public static string FormatSkillsWithinBudget(IReadOnlyList<SkillDefinition> skills, int? contextWindowTokens = null)
    {
        return FormatSkillsWithinBudget(skills, GetCharBudget(contextWindowTokens));
    }

    /// <summary>
    /// 在指定字符预算内格式化技能列表
    /// </summary>
    /// <param name="skills">技能列表</param>
    /// <param name="charBudget">字符预算</param>
    /// <returns>格式化后的技能列表文本</returns>
    public static string FormatSkillsWithinBudget(IReadOnlyList<SkillDefinition> skills, int charBudget)
    {
        if (skills.Count == 0) return string.Empty;

        // 分离 bundled 和非 bundled 技能
        var bundled = new List<SkillDefinition>();
        var nonBundled = new List<SkillDefinition>();
        foreach (var skill in skills)
        {
            if (IsBundledSkill(skill))
                bundled.Add(skill);
            else
                nonBundled.Add(skill);
        }

        // 第一层: 尝试全部完整显示
        var fullLines = new List<string>(skills.Count);
        foreach (var skill in skills)
        {
            var desc = GetDescription(skill);
            fullLines.Add($"- {skill.Name}: {desc}");
        }
        var fullText = string.Join('\n', fullLines);
        if (fullText.Length <= charBudget) return fullText;

        // 第二层: 分区处理 — bundled 不截断，其余共享剩余预算
        var bundledLines = new List<string>(bundled.Count);
        var bundledCharCount = 0;
        foreach (var skill in bundled)
        {
            var desc = GetDescription(skill);
            var line = $"- {skill.Name}: {desc}";
            bundledLines.Add(line);
            bundledCharCount += line.Length + 1; // +1 for newline
        }

        if (nonBundled.Count == 0)
        {
            return string.Join('\n', bundledLines);
        }

        var remainingBudget = charBudget - bundledCharCount;
        if (remainingBudget <= 0)
        {
            return string.Join('\n', bundledLines);
        }

        // 计算非 bundled 技能的名称开销
        var nameOverhead = 0;
        foreach (var skill in nonBundled)
        {
            nameOverhead += $"- {skill.Name}: ".Length + 1; // +1 for newline
        }

        var availableForDesc = remainingBudget - nameOverhead;
        if (availableForDesc <= 0)
        {
            // 极端退化: 仅显示名称
            var nameOnlyLines = new List<string>(bundledLines.Count + nonBundled.Count);
            nameOnlyLines.AddRange(bundledLines);
            foreach (var skill in nonBundled)
            {
                nameOnlyLines.Add($"- {skill.Name}");
            }
            return string.Join('\n', nameOnlyLines);
        }

        var maxDescLen = availableForDesc / nonBundled.Count;

        // 第三层: 极端退化检查
        if (maxDescLen < MinDescLength)
        {
            var nameOnlyLines = new List<string>(bundledLines.Count + nonBundled.Count);
            nameOnlyLines.AddRange(bundledLines);
            foreach (var skill in nonBundled)
            {
                nameOnlyLines.Add($"- {skill.Name}");
            }
            return string.Join('\n', nameOnlyLines);
        }

        // 正常截断
        var result = new List<string>(bundledLines.Count + nonBundled.Count);
        result.AddRange(bundledLines);
        foreach (var skill in nonBundled)
        {
            var desc = GetDescription(skill);
            var truncated = TruncateDescription(desc, maxDescLen);
            result.Add($"- {skill.Name}: {truncated}");
        }
        return string.Join('\n', result);
    }

    /// <summary>
    /// 获取技能描述 — 对齐 TS getCommandDescription
    /// </summary>
    internal static string GetDescription(SkillDefinition skill)
    {
        var desc = skill.Description;
        // 单条硬上限: 超过 250 字符截取
        if (desc.Length > MaxListingDescChars)
        {
            desc = string.Concat(desc.AsSpan(0, MaxListingDescChars - 1), "\u2026");
        }
        return desc;
    }

    private static string TruncateDescription(string desc, int maxLen) => StringTruncator.Truncate(desc, maxLen, "\u2026");

    /// <summary>
    /// 判断是否为 bundled（内置）技能 — 对齐 TS isBundledCommand
    /// bundled 技能永远不截断
    /// </summary>
    private static bool IsBundledSkill(SkillDefinition skill)
    {
        // 内置技能的 Namespace 为空或为 "builtin"
        return string.IsNullOrEmpty(skill.Namespace) ||
               skill.Namespace.Equals("builtin", StringComparison.OrdinalIgnoreCase);
    }
}
