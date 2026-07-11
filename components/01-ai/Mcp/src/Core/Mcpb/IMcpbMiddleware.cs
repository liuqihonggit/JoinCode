namespace McpClient.Mcpb;

/// <summary>
/// MCPB 中间件标记接口 — DI 注册时区分管道
/// </summary>
public interface IMcpbMiddleware : IMiddleware<McpbLoadContext> { }
