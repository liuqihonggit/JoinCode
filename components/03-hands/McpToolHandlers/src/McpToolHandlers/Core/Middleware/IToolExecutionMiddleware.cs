
namespace McpToolRegistry;

/// <summary>
/// 工具执行中间件接口 — 统一使用标准 IMiddleware&lt;ToolExecutionContext&gt; 管道
/// </summary>
public interface IToolExecutionMiddleware : IMiddleware<ToolExecutionContext>
{
}
