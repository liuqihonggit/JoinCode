namespace Core.Configuration;

/// <summary>
/// 设置变更中间件接口 — 继承通用中间件契约，用于设置变更管道
/// </summary>
public interface ISettingsMiddleware : IMiddleware<SettingsContext> { }
