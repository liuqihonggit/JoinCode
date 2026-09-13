namespace Core.Scheduling.Tasks;

/// <summary>
/// Teammate 执行中间件接口 — 统一 Teammate 执行管道中各中间件（校验、派生、注册、执行、连续模式等）的契约
/// </summary>
public interface ITeammateExecutionMiddleware : IMiddleware<TeammateExecutionContext> { }
