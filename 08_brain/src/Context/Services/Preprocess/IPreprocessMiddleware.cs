namespace Core.Context;

/// <summary>
/// 预处理中间件接口 — 扩展自通用中间件管道
/// </summary>
public interface IPreprocessMiddleware : IMiddleware<PreprocessContext> { }
