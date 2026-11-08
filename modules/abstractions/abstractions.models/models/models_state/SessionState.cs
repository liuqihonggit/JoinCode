namespace JoinCode.Abstractions.State;

/// <summary>
/// 会话状态
/// </summary>
public sealed record SessionState {
    /// <summary>
    /// 会话 ID
    /// </summary>
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// 聊天历史（不可变列表）
    /// </summary>
    public ImmutableList<ApiMessageState> MessageList { get; init; } = ImmutableList<ApiMessageState>.Empty;

    /// <summary>
    /// 会话开始时间
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 当前模型名称
    /// </summary>
    public string? CurrentModel { get; init; }

    /// <summary>
    /// 是否处于计划模式
    /// </summary>
    public bool IsPlanMode { get; init; }

    /// <summary>
    /// 当前计划
    /// </summary>
    public string? CurrentPlan { get; init; }
}

/// <summary>
/// 聊天消息状态（用于序列化）
/// </summary>
public sealed record ApiMessageState {
    /// <summary>
    /// 消息角色
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 消息时间戳
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 消息元数据
    /// </summary>
    public ImmutableHamT<string, string> Metadata { get; init; } = ImmutableHamT<string, string>.Empty;
}
