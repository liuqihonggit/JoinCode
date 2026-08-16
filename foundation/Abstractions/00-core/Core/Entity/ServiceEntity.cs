namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 服务实体基类 — 所有 DI 服务的统一基类
/// 继承 Entity 获得 ObjectId + CreatedAt + LifecycleState + LastActivityAt + TraceId 全套生命周期追踪
/// 提供默认空 OnDispose，子类按需覆写释放资源
/// 构造函数有默认参数，子类构造函数不用显式调 base()
/// </summary>
public abstract class ServiceEntity : Entity
{
    protected ServiceEntity(string? displayName = null) : base(ObjectType.Service, ObjectId.Empty, displayName, registerToSessionRouter: false) { }

    protected override void OnDispose() { }
}
