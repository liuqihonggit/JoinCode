namespace JoinCode.CliCommands;

/// <summary>
/// 子命令门禁 — 按当前 profile 的 <c>cli-commands</c> 消费点决定某条子命令是否可用。
/// <para>
/// TASK100 S4：命令表面的启停此前只能靠改代码/改编译宏。现在由 profile 决定：
/// 删掉一条节点声明，命令就从 CLI 表面消失（而不是「未知子命令」那种看不出原因的错误）。
/// </para>
/// <para>
/// 语义完全复用 <see cref="Abstractions.Composition.Profile.ConsumerDeclaration"/>：
/// 未声明整个 <c>cli-commands</c> 段 = 全开（老 profile 不必逐条补声明），声明了 = 白名单。
/// </para>
/// <para>
/// 为什么门禁失败要 fail-open：子命令分发发生在 DI 容器构建之前，profile 读不到时整条
/// CLI 会连 <c>--help</c> 之外的命令都不可用 —— 门禁是「裁剪」手段，不该变成「停机」原因。
/// 失败会打 <c>[CLI-GATE]</c> 诊断行，便于排查。
/// </para>
/// </summary>
internal static class CliCommandGate {
    /// <summary>当前生效的 profile 名（用于报错措辞）。</summary>
    public static string ProfileName => CliProfileGate.ProfileName;

    /// <summary>
    /// 判断某节点在当前 profile 是否启用。
    /// </summary>
    /// <param name="nodeId">节点 ID（kebab-case）。</param>
    /// <returns>启用返回 <see langword="true"/>。</returns>
    public static bool IsEnabled(string nodeId) => CliProfileGate.IsEnabled(CliCommandNodes.ConsumerId, nodeId);

    /// <summary>
    /// 按命令行字面量判断启用（<c>mcp_call</c> 这种下划线写法）。
    /// <para>
    /// 帮助文本里的命令名是 CLI 字面量，节点 ID 是 kebab —— 两套词。换算只在这里做一次：
    /// 否则「帮助把 10 条命令误判成未启用」这种错会同时出现在分类概览、分类列表、命令详情三处，
    /// 而且看起来像 profile 坏了（S4 首次接帮助时就踩了这个：只删 probe，却少了 4 个分类）。
    /// </para>
    /// </summary>
    /// <param name="literal">命令行字面量。</param>
    /// <returns>启用返回 <see langword="true"/>；未识别的字面量也返回 <see langword="true"/>（交由常规「未知命令」路径处理）。</returns>
    public static bool IsEnabledLiteral(string literal) {
        var command = CliSubCommandExtensions.FromValue(literal);
        if (command is null)
            return true;

        var node = CliCommandNodes.For(command.Value);
        return node is null || IsEnabled(node.NodeId);
    }
}
