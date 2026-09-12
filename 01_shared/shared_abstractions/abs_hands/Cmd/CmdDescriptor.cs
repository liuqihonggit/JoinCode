namespace JoinCode.Abstractions.Cmd;

/// <summary>
/// 统一命令描述符 — 包装 IChatCommand 或 IToolHandler 的引用，不复制数据
/// </summary>
public sealed record CmdDescriptor
{
    /// <summary>命令名称</summary>
    public required string Name { get; init; }

    /// <summary>命令来源</summary>
    public required CmdSource Source { get; init; }

    /// <summary>描述</summary>
    public string Description { get; init; } = "";

    /// <summary>斜杠命令实例 — Source=Slash 时非 null</summary>
    public IChatCommand? SlashCommand { get; init; }

    /// <summary>MCP 工具处理器 — Source=Mcp 时非 null</summary>
    public IToolHandler? McpHandler { get; init; }

    // === 工厂方法 ===

    /// <summary>从斜杠命令创建</summary>
    public static CmdDescriptor FromSlash(IChatCommand cmd) => new()
    {
        Name = cmd.Name,
        Source = CmdSource.Slash,
        Description = cmd.Description,
        SlashCommand = cmd,
    };

    /// <summary>从 MCP 工具创建</summary>
    public static CmdDescriptor FromMcp(IToolHandler handler) => new()
    {
        Name = handler.Name,
        Source = CmdSource.Mcp,
        Description = handler.Description,
        McpHandler = handler,
    };

    // === 执行 — 按源分发到原接口 ===

    /// <summary>
    /// 执行命令 — 根据来源调用 IChatCommand.ExecuteAsync 或 IToolHandler.ExecuteAsync
    /// </summary>
    public async Task<CmdResult> ExecuteAsync(CmdContext ctx)
    {
        if (SlashCommand is not null)
        {
            var slashResult = await SlashCommand.ExecuteAsync(ctx.ToSlashContext()).ConfigureAwait(false);
            return CmdResult.FromSlashResult(slashResult);
        }

        if (McpHandler is not null)
        {
            var mcpResult = await McpHandler.ExecuteAsync(
                ctx.ToMcpArgs(),
                ctx.CancellationToken,
                ctx.OnProgress).ConfigureAwait(false);
            return CmdResult.FromMcpResult(mcpResult);
        }

        throw new InvalidOperationException($"CmdDescriptor '{Name}' 无有效处理器");
    }
}
