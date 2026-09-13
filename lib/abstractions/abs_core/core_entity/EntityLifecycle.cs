namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 实体生命周期状态 — 统一状态机，所有 Entity 共享
/// Created → Active → Suspended → Completed → Persisted → Disposed
/// </summary>
public enum EntityLifecycle
{
    [EnumValue("created")] Created,
    [EnumValue("active")] Active,
    [EnumValue("suspended")] Suspended,
    [EnumValue("completed")] Completed,
    [EnumValue("persisted")] Persisted,
    [EnumValue("disposed")] Disposed,
}
