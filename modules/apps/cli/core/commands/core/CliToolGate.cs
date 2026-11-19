namespace JoinCode.CliCommands;

/// <summary>
/// 工具组门禁 — 按 profile 的 <c>tool-groups</c> 消费点决定某个独立工具 exe 组是否可用。
/// <para>
/// TASK100 S6：13 个组、388 个不经宿主的隔离工具此前是全量可见的。宿主的 368 个工具在 S4 已经能裁，
/// 但模型用工具的入口其实是「首轮提示词列出组 + bash 调 <c>jcc tool call</c>」——
/// 这条链路不裁，「关掉 todo 能力」就只关了一半（另一半在 exe 里）。
/// </para>
/// <para>
/// 与 <see cref="CliCommandGate"/> 同一个 profile、同一份 <c>ConsumerDeclaration</c> 语义，
/// 只是消费点不同。组名与节点 ID 同名（<c>agent</c> / <c>github</c> …），不需要字面量换算 ——
/// 这一点与命令表面不同：那里 CLI 字面量（<c>mcp_call</c>）和节点 ID（<c>mcp-call</c>）是两套词。
/// </para>
/// </summary>
internal static class CliToolGate {
    /// <summary>判断某工具组在当前 profile 是否启用。</summary>
    /// <param name="groupId">工具组 ID（与 <c>cli_groups.yaml</c> 的组 ID 相同）。</param>
    /// <returns>启用返回 <see langword="true"/>。</returns>
    public static bool IsEnabled(string groupId) =>
        CliProfileGate.IsEnabled(Composition.NodeRegistry.ToolGroupNodes.ConsumerId, groupId);
}
