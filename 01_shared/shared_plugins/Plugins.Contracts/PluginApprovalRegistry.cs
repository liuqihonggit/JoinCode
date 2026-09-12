namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 审批请求 ID — 自增铸造，格式 approval-&lt;n&gt;
/// <para>对齐 DSH ApprovalRequestId：mintApprovalRequestId 自增计数器</para>
/// </summary>
public readonly struct ApprovalRequestId : IEquatable<ApprovalRequestId>
{
    private readonly int _value;

    /// <summary>构造</summary>
    public ApprovalRequestId(int value) => _value = value;

    /// <summary>数值</summary>
    public int Value => _value;

    /// <inheritdoc/>
    public bool Equals(ApprovalRequestId other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ApprovalRequestId id && Equals(id);

    /// <inheritdoc/>
    public override int GetHashCode() => _value;

    /// <inheritdoc/>
    public override string ToString() => $"approval-{_value}";

    /// <summary>相等</summary>
    public static bool operator ==(ApprovalRequestId left, ApprovalRequestId right) => left.Equals(right);

    /// <summary>不等</summary>
    public static bool operator !=(ApprovalRequestId left, ApprovalRequestId right) => !left.Equals(right);
}

/// <summary>
/// 审批状态
/// </summary>
public enum ApprovalState
{
    /// <summary>待审批</summary>
    Pending,

    /// <summary>已批准</summary>
    Approved,

    /// <summary>已拒绝</summary>
    Declined,
}

/// <summary>
/// 插件审批请求 — 对齐 DSH ApprovalRequest
/// <para>首个回答者获胜：TryResolve 原子 CAS，仅 Pending→target 成功</para>
/// <para>用于动态插件运行时(#8)的激活前置审批</para>
/// </summary>
public sealed class PluginApprovalRequest
{
    private int _state = (int)ApprovalState.Pending;

    /// <summary>请求 ID</summary>
    public ApprovalRequestId RequestId { get; }

    /// <summary>插件 ID</summary>
    public string PluginId { get; }

    /// <summary>请求原因</summary>
    public string? Reason { get; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>当前状态（原子读取）</summary>
    public ApprovalState State => (ApprovalState)Volatile.Read(ref _state);

    /// <summary>是否批准未来版本（批准后设置）</summary>
    public bool ApproveFutureVersions { get; private set; }

    /// <summary>拒绝反馈（拒绝后设置）</summary>
    public string? Feedback { get; private set; }

    internal PluginApprovalRequest(ApprovalRequestId requestId, string pluginId, string? reason, DateTimeOffset createdAt)
    {
        RequestId = requestId;
        PluginId = pluginId;
        Reason = reason;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// 首个回答者获胜：原子 CAS，仅 Pending→target 成功
    /// </summary>
    internal bool TryResolve(ApprovalState target)
    {
        return Interlocked.CompareExchange(ref _state, (int)target, (int)ApprovalState.Pending)
            == (int)ApprovalState.Pending;
    }

    internal void MarkApproved(bool approveFutureVersions) => ApproveFutureVersions = approveFutureVersions;

    internal void MarkDeclined(string? feedback) => Feedback = feedback;
}

/// <summary>
/// 插件审批注册表 — 对齐 DSH arm/peek/claim/disarm/pendingRequestFor
/// <para>首个回答者获胜：Approve/Decline 原子 CAS，已处理返回 false</para>
/// <para>线程安全：ConcurrentDictionary + Interlocked</para>
/// <para>时钟可注入：Func&lt;DateTimeOffset&gt;? clock = null，测试可控、生产用 UtcNow</para>
/// </summary>
public sealed class PluginApprovalRegistry
{
    private int _counter;
    private readonly ConcurrentDictionary<ApprovalRequestId, PluginApprovalRequest> _requests = new();
    private readonly Func<DateTimeOffset>? _clock;

    /// <param name="clock">时钟注入，null 用 DateTimeOffset.UtcNow</param>
    public PluginApprovalRegistry(Func<DateTimeOffset>? clock = null) => _clock = clock;

    /// <summary>铸造新 ID — 自增递增</summary>
    public ApprovalRequestId MintId() => new(Interlocked.Increment(ref _counter));

    /// <summary>创建待审批请求</summary>
    public PluginApprovalRequest ArmRequest(string pluginId, string? reason = null)
    {
        var id = MintId();
        var now = _clock?.Invoke() ?? DateTimeOffset.UtcNow;
        var req = new PluginApprovalRequest(id, pluginId, reason, now);
        _requests[id] = req;
        return req;
    }

    /// <summary>查看请求（不改变状态）</summary>
    public PluginApprovalRequest? PeekRequest(ApprovalRequestId id) => _requests.GetValueOrDefault(id);

    /// <summary>认领请求（供调用方决定 approve/decline，首个回答者获胜由 Approve/Decline 保证）</summary>
    public PluginApprovalRequest? ClaimRequest(ApprovalRequestId id) => _requests.GetValueOrDefault(id);

    /// <summary>移除请求</summary>
    public bool DisarmRequest(ApprovalRequestId id) => _requests.TryRemove(id, out _);

    /// <summary>查找某插件的待审批请求</summary>
    public PluginApprovalRequest? PendingRequestFor(string pluginId)
    {
        foreach (var req in _requests.Values)
        {
            if (req.PluginId == pluginId && req.State == ApprovalState.Pending)
                return req;
        }
        return null;
    }

    /// <summary>批准 — 首个回答者获胜，已处理返回 false</summary>
    public bool Approve(ApprovalRequestId id, bool approveFutureVersions = false)
    {
        if (!_requests.TryGetValue(id, out var req)) return false;
        if (!req.TryResolve(ApprovalState.Approved)) return false;
        req.MarkApproved(approveFutureVersions);
        return true;
    }

    /// <summary>拒绝 — 首个回答者获胜，已处理返回 false</summary>
    public bool Decline(ApprovalRequestId id, string? feedback = null)
    {
        if (!_requests.TryGetValue(id, out var req)) return false;
        if (!req.TryResolve(ApprovalState.Declined)) return false;
        req.MarkDeclined(feedback);
        return true;
    }
}
