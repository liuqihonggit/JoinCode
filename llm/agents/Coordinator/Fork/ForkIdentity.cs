namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 身份信息值对象 — 创建时设定,生命周期内不可变。
/// <para>从 ForkEntry 提取的不可变身份字段集合,与可变的 <see cref="ForkRuntime"/> 分离。</para>
/// <para>身份字段用于 Fork 唯一标识、父子关系溯源与超时检测,创建后不再变更。</para>
/// </summary>
/// <param name="ParentSessionId">父会话标识(必填),发起 Fork 的主代理会话 ID</param>
/// <param name="CreatedAt">Fork 条目创建时间(UTC),用于超时检测与统计</param>
internal sealed record ForkIdentity(
    string ParentSessionId,
    DateTime CreatedAt);
