namespace Core.Skills;

/// <summary>
/// 技能执行中间件 — 拦截和转换技能执行流程
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// </summary>
public interface ISkillMiddleware : IMiddleware<SkillContext> { }
