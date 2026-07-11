namespace Core.Context;

/// <summary>
/// 聊天初始化中间件 — 拦截和转换初始化步骤（加载上下文、恢复成本、配置监控、会话 Hook）
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// </summary>
public interface IChatInitMiddleware : IMiddleware<ChatInitContext> { }
