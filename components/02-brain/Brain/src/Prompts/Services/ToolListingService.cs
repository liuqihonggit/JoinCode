namespace Core.Prompts.Services;

/// <summary>
/// 工具列表注入服务 — 对齐 TS agent_listing_delta / skill_listing 附件
/// 通过 SystemReminderManager 注入 Agent/Skill 列表到 system-reminder 标签
/// 增量机制：只发送新增/移除的列表项，避免重复注入
/// </summary>
[Register]
public sealed partial class ToolListingService
{
    [Inject] private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly IAgentDefinitionProvider? _agentProvider;
    [Inject] private readonly ISkillService? _skillService;
    [Inject] private readonly ILogger<ToolListingService>? _logger;

    /// <summary>
    /// 增量追踪：已宣布的 Agent 类型集合
    /// </summary>
    private HashSet<string> _announcedAgentTypes = [];

    /// <summary>
    /// 增量追踪：已发送的 Skill 名称集合
    /// </summary>
    private HashSet<string> _sentSkillNames = [];

    /// <summary>
    /// 注入 Agent 列表（增量） — 对齐 TS getAgentListingDeltaAttachment
    /// 计算当前 Agent 列表与已宣布列表的差量，只发送新增/移除的项
    /// </summary>
    public async Task InjectAgentListingAsync(string? workingDirectory = null, CancellationToken ct = default)
    {
        if (_agentProvider is null) return;

        var agents = await _agentProvider.GetAgentDefinitionsAsync(workingDirectory, ct).ConfigureAwait(false);
        if (agents.Count == 0) return;

        var currentTypes = new HashSet<string>(agents.Select(a => a.AgentType));

        // 计算增量：新增的 Agent
        var added = agents.Where(a => !_announcedAgentTypes.Contains(a.AgentType)).ToList();

        // 计算增量：移除的 Agent
        var removed = _announcedAgentTypes.Where(t => !currentTypes.Contains(t)).ToList();

        if (added.Count == 0 && removed.Count == 0) return;

        var isInitial = _announcedAgentTypes.Count == 0;
        var sb = new System.Text.StringBuilder();

        if (isInitial)
        {
            sb.AppendLine("可用代理类型及其可访问的工具：");
        }
        else
        {
            if (added.Count > 0)
            {
                sb.AppendLine("新增代理类型：");
            }
        }

        foreach (var agent in added)
        {
            var toolsDesc = AgentToolSection.GetToolsDescription(agent);
            sb.AppendLine($"- {agent.AgentType}: {agent.WhenToUse} (工具: {toolsDesc})");
        }

        if (removed.Count > 0 && !isInitial)
        {
            sb.AppendLine();
            sb.AppendLine($"已移除代理类型：{string.Join(", ", removed)}");
        }

        // 更新追踪集合
        _announcedAgentTypes = currentTypes;

        var content = sb.ToString().TrimEnd();
        await _reminderManager.AddReminderAsync("agent-listing", content, priority: 50, ct: ct).ConfigureAwait(false);

        _logger?.LogDebug("[ToolListing] Agent 列表已注入: {Added} 新增, {Removed} 移除, 初始={IsInitial}",
            added.Count, removed.Count, isInitial);
    }

    /// <summary>
    /// 注入 Skill 列表（增量） — 对齐 TS getSkillListingAttachments
    /// 只发送新增的 Skill，已发送的不重复
    /// 使用 SkillDescriptionTruncator.FormatSkillsWithinBudget 进行预算内截断
    /// </summary>
    public async Task InjectSkillListingAsync(int? contextWindowTokens = null, CancellationToken ct = default)
    {
        if (_skillService is null) return;

        var skills = await _skillService.GetAvailableSkillsAsync(ct).ConfigureAwait(false);
        if (skills.Count == 0) return;

        // 过滤掉禁止模型自动调用的技能
        var visibleSkills = skills.Where(s => !s.DisableModelInvocation).ToList();

        // 计算增量：只发送新增的 Skill
        var newSkills = visibleSkills.Where(s => !_sentSkillNames.Contains(s.Name)).ToList();
        if (newSkills.Count == 0) return;

        var isInitial = _sentSkillNames.Count == 0;

        // 对齐 TS formatCommandsWithinBudget: 预算内截断
        var formattedList = SkillDescriptionTruncator.FormatSkillsWithinBudget(newSkills, contextWindowTokens);

        var sb = new System.Text.StringBuilder();

        if (isInitial)
        {
            sb.AppendLine("可用技能列表：");
        }

        sb.Append(formattedList);

        // 标记已发送的技能
        foreach (var skill in newSkills)
        {
            _sentSkillNames.Add(skill.Name);
        }

        var content = sb.ToString().TrimEnd();
        await _reminderManager.AddReminderAsync("skill-listing", content, priority: 45, ct: ct).ConfigureAwait(false);

        _logger?.LogDebug("[ToolListing] Skill 列表已注入: {NewSkills} 新增, 初始={IsInitial}",
            newSkills.Count, isInitial);
    }

    /// <summary>
    /// 重置追踪状态（新会话时调用）
    /// </summary>
    public void Reset()
    {
        _announcedAgentTypes = [];
        _sentSkillNames = [];
    }

}
