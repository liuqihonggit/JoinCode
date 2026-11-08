namespace McpProtocol;

/// <summary>
/// MCP 提示处理器接口 — 实现 prompts/get 请求的提示模板渲染
/// </summary>
public interface IPromptHandler {
    /// <summary>提示名称(唯一标识,作为提示字典键)</summary>
    string Name { get; }
    /// <summary>提示描述,可为 null</summary>
    string? Description { get; }
    /// <summary>提示参数列表,可为 null</summary>
    List<McpPromptArgument>? Arguments { get; }
    /// <summary>
    /// 异步获取提示渲染后的消息
    /// </summary>
    /// <param name="arguments">提示参数字典,可为 null</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提示消息</returns>
    Task<McpPromptMessage> GetAsync(Dictionary<string, string>? arguments = null, CancellationToken cancellationToken = default);
}