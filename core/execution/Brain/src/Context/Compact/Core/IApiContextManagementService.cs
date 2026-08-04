namespace Core.Context.Compact;

/// <summary>
/// API 端上下文管理服务 — 对齐 TS apiMicrocompact.ts getAPIContextManagement
/// 通过 context_management 请求参数让 Anthropic API 在服务端自动清理工具结果
/// 不破坏 prompt cache，优于客户端 microcompact
/// </summary>
public interface IApiContextManagementService
{
    /// <summary>
    /// 获取 API 端上下文管理配置
    /// </summary>
    /// <param name="thinking">Thinking 模式上下文，null 表示无 thinking</param>
    /// <returns>配置，或 null 表示不需要 API 端上下文管理</returns>
    ContextManagementConfig? GetConfig(ThinkingContext? thinking = null);
}
