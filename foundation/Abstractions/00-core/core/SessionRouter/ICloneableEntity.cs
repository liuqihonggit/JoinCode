namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 跨会话拷贝接口 — Entity 跨会话传递时必须深拷贝，两个独立副本互不影响
/// Clone 返回 Entity, 调用方强转为具体类型
/// 引用重映射通过 CloneContext 处理: 找不到对应 Entity 抛异常
/// </summary>
public interface ICloneableEntity
{
    /// <summary>
    /// 深拷贝到目标会话 — 新 ObjectId, 新 SessionId, 注册到目标 SessionScope
    /// 返回 Entity, 调用方强转为具体类型
    /// </summary>
    Entity Clone(CloneContext context);
}
