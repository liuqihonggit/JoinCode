namespace JoinCode.ChatCommands;

/// <summary>
/// 统一命令门面 — 归纳 ChatCommandRegistry（斜杠）和 IToolRegistry（MCP），提供统一查询和执行入口
/// 门面不持有自己的数据，数据仍在两个原注册表里
/// </summary>
public sealed class CmdMap : ICmdMap
{
    private readonly ISlashCommandRegistry _slash;
    private readonly IToolRegistry _mcp;

    /// <summary>
    /// 构造 — 注入两个已填好数据的注册表
    /// </summary>
    public CmdMap(ISlashCommandRegistry slash, IToolRegistry mcp)
    {
        _slash = slash;
        _mcp = mcp;
    }

    /// <summary>
    /// 解析命令名称 — 先查斜杠（同步），再查 MCP（异步）
    /// </summary>
    public async Task<CmdDescriptor?> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        // 1. 先查斜杠命令
        var slashCmd = _slash.GetCommand(name);
        if (slashCmd is not null)
            return CmdDescriptor.FromSlash(slashCmd);

        // 2. 再查 MCP 工具
        var mcpHandler = await _mcp.GetToolAsync(name, cancellationToken).ConfigureAwait(false);
        if (mcpHandler is not null)
            return CmdDescriptor.FromMcp(mcpHandler);

        return null;
    }

    /// <summary>
    /// 获取 LLM 可见的工具定义列表 — MCP 全部工具 + 斜杠中 ExposeToMcp=true 的
    /// </summary>
    public async Task<IReadOnlyList<CmdToolDef>> GetToolDefsForLlmAsync(CancellationToken cancellationToken = default)
    {
        // MCP 全部工具
        var mcpTools = await _mcp.GetAllToolsAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CmdToolDef>(mcpTools.Count);
        foreach (var kvp in mcpTools)
        {
            result.Add(CmdToolDef.FromMcp(kvp.Value));
        }

        // 斜杠中 ExposeToMcp=true 的
        foreach (var kvp in _slash.GetAllCommands())
        {
            if (kvp.Value is ChatCommandBase { ExposeToMcp: true } cmd)
            {
                result.Add(CmdToolDef.FromSlash(cmd, cmd.Kind));
            }
        }

        return result;
    }

    /// <summary>
    /// 执行命令 — 先 Resolve 再按源分发
    /// </summary>
    public async Task<CmdResult> ExecuteAsync(string name, CmdContext ctx)
    {
        var descriptor = await ResolveAsync(name, ctx.CancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"未知命令: {name}");
        return await descriptor.ExecuteAsync(ctx).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取所有斜杠命令描述符
    /// </summary>
    public IReadOnlyList<CmdDescriptor> GetAllSlashCommands()
    {
        var all = _slash.GetAllCommands();
        var result = new List<CmdDescriptor>(all.Count);
        foreach (var kvp in all)
        {
            result.Add(CmdDescriptor.FromSlash(kvp.Value));
        }
        return result;
    }

    /// <summary>
    /// 获取所有 MCP 工具描述符
    /// </summary>
    public async Task<IReadOnlyList<CmdDescriptor>> GetAllMcpCommandsAsync(CancellationToken cancellationToken = default)
    {
        var all = await _mcp.GetAllToolsAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CmdDescriptor>(all.Count);
        foreach (var kvp in all)
        {
            result.Add(CmdDescriptor.FromMcp(kvp.Value));
        }
        return result;
    }
}
