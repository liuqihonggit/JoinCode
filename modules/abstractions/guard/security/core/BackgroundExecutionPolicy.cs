namespace JoinCode.Abstractions.Security;

/// <summary>
/// 后台执行统一策略 — 后台任务（async agent / fork / resume / Goal）不参与交互。
/// <para>后台任务必须：① 权限模式固定为 <see cref="PermissionMode.Unattended"/>；
/// ② 工具白名单收敛到 <see cref="AllowedTools"/>；③ 显式拒绝 <see cref="DeniedTools"/>（ask/confirm 会阻塞等待用户）。</para>
/// <para>本类是所有后台入口的唯一数据源，禁止在 spawn/fork/resume 各自维护白名单。</para>
/// </summary>
public static class BackgroundExecutionPolicy {
    /// <summary>后台 Agent 允许的工具集合 — 排除交互提问、递归 Agent、停止其他任务等工具。</summary>
    public static readonly FrozenSet<string> AllowedTools = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        FileToolNameEnumConstants.FileRead,
        FileToolNameEnumConstants.FileWrite,
        FileToolNameEnumConstants.FileEdit,
        SearchToolNameEnumConstants.Glob,
        SearchToolNameEnumConstants.Grep,
        SearchToolNameEnumConstants.SearchCodebase,
        ShellToolNameEnumConstants.Bash,
        NotebookToolNameEnumConstants.NotebookEdit,
        WebToolNameEnumConstants.WebFetch,
        WebToolNameEnumConstants.WebSearch,
        TodoToolNameEnumConstants.TodoWrite,
        SkillToolNameEnumConstants.Skill,
        SystemToolNameEnumConstants.ToolSearch,
        WorktreeToolNameEnumConstants.EnterWorktree,
        WorktreeToolNameEnumConstants.ExitWorktree);

    /// <summary>后台 Agent 显式拒绝的工具集合 — 交互类工具在后台没有宿主可回答，必须拒绝而不是挂起。</summary>
    public static readonly FrozenSet<string> DeniedTools = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        UserInteractionToolNameEnumConstants.AskUserQuestion,
        UserInteractionToolNameEnumConstants.ConfirmAction);

    /// <summary>后台任务统一权限模式 — 无人值守，不弹 ask。</summary>
    public static PermissionMode Mode => PermissionMode.Unattended;

    /// <summary>后台任务统一权限模式字符串 — 用于 <c>SubAgentOptions.PermissionMode</c>。</summary>
    public static string ModeValue => PermissionMode.Unattended.ToValue();

    /// <summary>
    /// 后台任务保留调用方已有工具集，只剔除 <see cref="DeniedTools"/>；
    /// 调用方未指定工具集时返回空列表（表示不做白名单限制，由拒绝集合兜底）。
    /// <para>用于 fork / resume 等「继承父代理工具池」的路径：不能像 async agent 一样
    /// 收敛到默认白名单，否则会丢掉 Git 等父代理工具；交互工具仍由拒绝集合移除。</para>
    /// </summary>
    public static List<string> PreserveToolsExceptDenied(IEnumerable<string>? requested) {
        if (requested is null)
            return [];

        var filtered = requested.Where(tool => !DeniedTools.Contains(tool)).ToList();
        return filtered.Count > 0 ? filtered : [.. AllowedTools];
    }

    /// <summary>
    /// 计算后台 async agent 最终允许的工具集 — 调用方给的更窄白名单优先，但永远不能超出 <see cref="AllowedTools"/>。
    /// <para>调用方为空时使用后台默认白名单；交集为空（调用方只给了交互工具）时回退默认白名单，
    /// 因为空集合在 <c>ToolFilterPolicy</c> 中表示“无白名单限制”，会造成反向放大。</para>
    /// </summary>
    public static List<string> EffectiveAllowedTools(IEnumerable<string>? requested) {
        if (requested is null)
            return [.. AllowedTools];

        var filtered = requested.Where(AllowedTools.Contains).ToList();
        return filtered.Count > 0 ? filtered : [.. AllowedTools];
    }

    /// <summary>合并后台必须拒绝的工具与调用方已有拒绝集合。</summary>
    public static List<string> MergeDeniedTools(IEnumerable<string>? existing) {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existing is not null)
            merged.UnionWith(existing);
        merged.UnionWith(DeniedTools);
        return [.. merged];
    }
}
