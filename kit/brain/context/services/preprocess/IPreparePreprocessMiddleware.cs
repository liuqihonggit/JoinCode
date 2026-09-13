namespace Core.Context;

/// <summary>
/// 准备阶段预处理中间件接口 — 构建系统提示、注入提醒等
/// </summary>
public interface IPreparePreprocessMiddleware : IMiddleware<PreprocessContext> { }
