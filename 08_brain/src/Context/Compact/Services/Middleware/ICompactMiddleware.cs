namespace Core.Context.Compact;

/// <summary>
/// 压缩中间件接口 — 扩展自通用中间件管道
/// </summary>
public interface ICompactMiddleware : IMiddleware<CompactContext> { }
