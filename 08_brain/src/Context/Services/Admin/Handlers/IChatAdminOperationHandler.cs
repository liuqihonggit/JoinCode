namespace Core.Context;

/// <summary>
/// 管理操作处理器接口 — 每种 ChatAdminOperation 一个实现
/// 替代 SessionAdminMiddleware 中的 switch 分派，每个 Handler 只注入自己需要的依赖
/// </summary>
public interface IChatAdminOperationHandler
{
    /// <summary>
    /// 此 Handler 处理的操作类型
    /// </summary>
    ChatAdminOperation Operation { get; }

    /// <summary>
    /// 执行管理操作
    /// </summary>
    Task ExecuteAsync(ChatAdminContext context, CancellationToken ct);
}
