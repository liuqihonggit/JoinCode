namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// 配置加载中间件标记接口
/// </summary>
public interface IConfigLoadMiddleware : IMiddleware<ConfigLoadContext> { }
