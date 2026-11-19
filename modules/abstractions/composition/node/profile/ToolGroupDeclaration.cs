namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// 工具组声明 — 13 个独立工具 exe 组的节点 ID 与启停判定的唯一来源。
/// <para>
/// TASK100 S6：<c>resources/tools/cli_groups.yaml</c> 的组（共 388 个不经宿主的隔离工具）
/// 此前全量可见：首轮提示词无条件列出全部组，<c>jcc tool tree/find/help/call</c> 一律放行。
/// profile 能裁宿主的 368 个工具（S4），却裁不掉另外 388 个 —— 而后者对模型同样可见。
/// </para>
/// <para>
/// 为什么纯语义住这里而不是和节点类放一起：节点类需要 <c>[NodeFactory]</c>（住装配层 Composition），
/// 而提示词层只引用 <c>Abstractions.Composition</c>，看不到装配层。把 ID 清单与判定放在这一层，
/// 提示词与 CLI 才能共用同一份判定 —— 否则会各写一份，最后「提示词列了 13 组、tool tree 只 12 组」。
/// </para>
/// </summary>
public static class ToolGroupDeclaration {
    /// <summary>工具组在 profile 中的消费点 ID（contract: capability）。</summary>
    public const string ConsumerId = "tool-groups";

    /// <summary>子代理与 worktree 管理组。</summary>
    public const string Agent = "agent";

    /// <summary>notebook 与代码工作流组。</summary>
    public const string Code = "code";

    /// <summary>文件读写与编辑组。</summary>
    public const string File = "file";

    /// <summary>git 操作组。</summary>
    public const string Git = "git";

    /// <summary>GitHub 全量操作组（173 个 delegate 工具）。</summary>
    public const string Github = "github";

    /// <summary>MCP 连接与配置组。</summary>
    public const string Mcp = "mcp";

    /// <summary>桌面自动化与图像处理组。</summary>
    public const string Media = "media";

    /// <summary>文件与符号搜索组。</summary>
    public const string Search = "search";

    /// <summary>shell 命令组。</summary>
    public const string Shell = "shell";

    /// <summary>斜杠命令组。</summary>
    public const string Slash = "slash";

    /// <summary>系统与进程组。</summary>
    public const string System = "system";

    /// <summary>工具自省与基础工具组。</summary>
    public const string Tools = "tools";

    /// <summary>web 抓取与检索组。</summary>
    public const string Web = "web";

    /// <summary>
    /// 全部工具组 ID（顺序与 <c>cli_groups.yaml</c> 一致 —— 提示词按此序渲染，保证输出稳定）。
    /// <para>增删组时必须同步改这里与 <c>cli_groups.yaml</c>；门禁 <c>Verify-ToolGroupNodes.py</c> 双向核对。</para>
    /// </summary>
    public static IReadOnlyList<string> AllNodeIds { get; } = [
        Agent, Code, File, Git, Github, Mcp, Media, Search, Shell, Slash, System, Tools, Web
    ];

    /// <summary>
    /// 判断某工具组在当前 profile 是否启用（语义见 <see cref="ConsumerDeclaration"/>）。
    /// </summary>
    /// <param name="profile">已解析的 profile；<see langword="null"/> 时视为全开。</param>
    /// <param name="groupId">工具组 ID。</param>
    /// <returns>启用返回 <see langword="true"/>。</returns>
    public static bool IsEnabled(ResolvedProfile? profile, string groupId) =>
        ConsumerDeclaration.IsEnabled(profile, ConsumerId, groupId);

    /// <summary>
    /// 取当前 profile 下启用的工具组 ID。
    /// <para>
    /// 消费点未声明时返回 <see cref="AllNodeIds"/> 本身而不是空集 ——
    /// 「没配 = 全开」与「配了但一个都没开」是相反的语义，不能让每个调用方各自解释一次。
    /// </para>
    /// </summary>
    /// <param name="profile">已解析的 profile；<see langword="null"/> 时视为全开。</param>
    /// <returns>启用的组 ID 列表（保持声明顺序）。</returns>
    public static IReadOnlyList<string> EnabledIds(ResolvedProfile? profile) {
        if (ConsumerDeclaration.DeclaredIds(profile, ConsumerId) is null)
            return AllNodeIds;

        var enabled = new List<string>(AllNodeIds.Count);
        foreach (var groupId in AllNodeIds) {
            if (IsEnabled(profile, groupId))
                enabled.Add(groupId);
        }
        return enabled;
    }
}
