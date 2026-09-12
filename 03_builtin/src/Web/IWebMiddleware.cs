namespace Services.Web;

/// <summary>
/// Web获取中间件 — 拦截和转换Web内容获取流程
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// </summary>
public interface IWebMiddleware : IMiddleware<WebContext> { }
