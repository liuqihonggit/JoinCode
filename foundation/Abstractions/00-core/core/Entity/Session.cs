namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时会话（区别于 SessionState record，后者是 UI 状态层 DTO）
/// ObjectId + 会话描述 + 创建时间 + 独立注册器 + 静态属性暴露
/// </summary>
public sealed class Session : Entity
{
    public string? SystemPrompt { get; init; }
    public string? CurrentModel { get; set; }
    public bool IsPlanMode { get; set; }
    public string? CurrentPlan { get; set; }

    /// <summary>
    /// 全局唯一 Session 注册器 — 静态属性暴露，无需DI
    /// </summary>
    public static SessionRegistry Registry { get; } = new();

    public Session(
        string? systemPrompt = null,
        string? currentModel = null,
        string? displayName = null)
        : base(ObjectType.Session, displayName)
    {
        SystemPrompt = systemPrompt;
        CurrentModel = currentModel;
        LastActivityAt = DateTime.UtcNow;

        Registry.Add(ObjectId, this);
    }

    /// <summary>
    /// 惰性释放 — 持久化服务确认数据全部写入后才调用
    /// </summary>
    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    /// <summary>
    /// 转换为 SessionState DTO（供 UI 状态层使用）
    /// </summary>
    public SessionState ToSessionState(ImmutableList<ApiMessageState> messages)
        => new()
        {
            SessionId = UniqueId,
            SystemPrompt = SystemPrompt ?? string.Empty,
            MessageList = messages,
            StartedAt = CreatedAt,
            LastActivityAt = LastActivityAt,
            CurrentModel = CurrentModel,
            IsPlanMode = IsPlanMode,
            CurrentPlan = CurrentPlan
        };
}
