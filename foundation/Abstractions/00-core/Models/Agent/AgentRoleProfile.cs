namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 角色档案 — 值对象，封装角色的提示词/工具集/权限等配置
/// 通过 IAgentRoleRegistry.GetProfile(role, variant) 获取
/// </summary>
public sealed class AgentRoleProfile
{
    /// <summary>
    /// 角色 — Coordinator 或 Executor
    /// </summary>
    public required AgentRole Role { get; init; }

    /// <summary>
    /// 执行者变体 — 仅 Executor 有值，Coordinator 为 null
    /// </summary>
    public ExecutorVariant? Variant { get; init; }

    /// <summary>
    /// 何时使用此角色/变体
    /// </summary>
    public required string WhenToUse { get; init; }

    /// <summary>
    /// 角色描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// 允许的工具白名单 — null 表示全量工具集
    /// </summary>
    public IEnumerable<string>? AllowedTools { get; init; }

    /// <summary>
    /// 禁止的工具黑名单
    /// </summary>
    public IEnumerable<string>? DisallowedTools { get; init; }

    /// <summary>
    /// 权限模式
    /// </summary>
    public string? PermissionMode { get; init; }

    /// <summary>
    /// 是否后台运行
    /// </summary>
    public bool IsBackground { get; init; }

    /// <summary>
    /// 是否省略 CLAUDE.md 上下文 — Explore/Plan 不需要
    /// </summary>
    public bool OmitClaudeMd { get; init; }

    /// <summary>
    /// 是否省略 git status 上下文 — Explore/Plan 不需要
    /// </summary>
    public bool OmitGitStatus { get; init; }

    /// <summary>
    /// 是否一次性执行者 — Explore/Plan 运行一次即返回
    /// </summary>
    public bool IsOneShot { get; init; }

    /// <summary>
    /// 模型名称覆盖
    /// </summary>
    public string? ModelName { get; init; }

    /// <summary>
    /// 温度参数
    /// </summary>
    public float? Temperature { get; init; }

    /// <summary>
    /// 最大 token 数
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// 记忆作用域
    /// </summary>
    public AgentMemoryScope? Memory { get; init; }

    /// <summary>
    /// 预加载技能
    /// </summary>
    public IEnumerable<string>? Skills { get; init; }

    /// <summary>
    /// 定义来源路径（用户/项目自定义 .md 文件）
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// 获取显示标识 — "coordinator" 或 "executor:code"
    /// </summary>
    public string DisplayId => Variant.HasValue
        ? $"{Role.ToValue()}:{Variant.Value.ToValue()}"
        : Role.ToValue();
}
