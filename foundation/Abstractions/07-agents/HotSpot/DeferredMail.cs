namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 延迟邮件 — 标记"将在 N 轮后自动打开，或任务结束之后注入"
/// Worker 可继续当前任务稍后再看或立即查看，减少中断
/// 不可变 record，线程安全
/// </summary>
public sealed record DeferredMail
{
    /// <summary>
    /// 收件人 Agent ID
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// 发件人 Agent ID
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// 邮件主题
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// 邮件正文
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// 延迟轮次（默认20轮后自动打开）
    /// </summary>
    public int OpenAfterTurns { get; init; } = 20;

    /// <summary>
    /// 邮件标记（优先级分类）
    /// </summary>
    public MailMarker Marker { get; init; } = MailMarker.ResourceRefChange;

    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 是否高优先级（热文件冲突）
    /// </summary>
    public bool IsHighPriority => Marker == MailMarker.HotFileConflict;
}
