namespace Core.Context;

/// <summary>
/// 聊天中间件 — 拦截和转换聊天事件流
/// 继承通用 Stream 中间件接口，复用管道构建和异常捕获机制
/// </summary>
public interface IChatMiddleware : IStreamMiddleware<ChatMiddlewareContext, ChatStreamEvent> { }
