
namespace McpToolRegistry;

/// <summary>
/// 工具执行中间件接口 — 统一使用标准 IMiddleware&lt;ToolExecutionContext&gt; 管道
/// 排序由 [ToolMiddleware(Order = N)] 特性在编译时决定，不在运行时
/// </summary>
public interface IToolExecutionMiddleware : IMiddleware<ToolExecutionContext>
{
}
