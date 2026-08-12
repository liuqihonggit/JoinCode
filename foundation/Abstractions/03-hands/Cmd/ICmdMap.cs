namespace JoinCode.Abstractions.Cmd;

/// <summary>
/// 统一命令门面接口 — 归纳斜杠命令注册表和 MCP 工具注册表，提供统一查询和执行入口
/// </summary>
public interface ICmdMap
{
    /// <summary>
    /// 解析命令名称 — 先查斜杠，再查 MCP（斜杠优先，避免重名时 MCP 覆盖用户命令）
    /// </summary>
    /// <param name="name">命令名称（不带 /）</param>
    /// <returns>命令描述符，未找到时 null</returns>
    CmdDescriptor? Resolve(string name);

    /// <summary>
    /// 获取 LLM 可见的工具定义列表 — MCP 全部工具 + 斜杠中 ExposeToMcp=true 的
    /// </summary>
    Task<IReadOnlyList<CmdToolDef>> GetToolDefsForLlmAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行命令 — 先 Resolve 再按源分发
    /// </summary>
    /// <param name="name">命令名称</param>
    /// <param name="ctx">命令上下文</param>
    /// <returns>执行结果</returns>
    Task<CmdResult> ExecuteAsync(string name, CmdContext ctx);

    /// <summary>
    /// 获取所有斜杠命令描述符（供 /help 等遍历场景）
    /// </summary>
    IReadOnlyList<CmdDescriptor> GetAllSlashCommands();

    /// <summary>
    /// 获取所有 MCP 工具描述符
    /// </summary>
    Task<IReadOnlyList<CmdDescriptor>> GetAllMcpCommandsAsync(CancellationToken cancellationToken = default);
}
