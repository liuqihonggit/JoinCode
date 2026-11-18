// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.Slash;

/// <summary>
/// 斜杠命令独立 CLI.exe — ADR 0157「mcp 和斜杠命令都拆成 exe」。
/// <para>-h 文本来自现有 [ChatCommand] / [ChatCommandArg] 特性（编译期内嵌），本文件不重复维护任何说明。</para>
/// <para>5 个纯文本命令（version/color/env/help/heapdump）在 exe 内本地实现；
/// 其余 94 个依赖会话状态（聊天上下文 / 服务容器 / TUI 回调 / LLM 会话流），转发宿主 <c>jcc slash_call</c>。</para>
/// <para>入口行为（索引渲染 / 参数解析 / 转发 / 退出码）统一在 <see cref="CliNodeRunner"/>，
/// 本文件只声明两组差异：转发动词是 <c>slash_call</c>，命令名需要还原 Git Bash 的路径展开。</para>
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "slash",
        Title = "斜杠命令节点",
        HelpResource = "JoinCode.CliNodes.Groups.slash.help.txt",
        Resolve = id => SlashLocalCommands.Find(id),
        LocalToolIds = () => SlashLocalCommands.LocalToolIds,
        HostVerb = "slash_call",
        // Git Bash 会把 /agents 展开成 <Git 安装根>/agents；归一化规则与 jcc 执行器共用同一份实现。
        NameNormalizer = CliArgNormalizer.NormalizeNodeName,
        // 转发宿主时去掉前导 '/'：slash_call 期望命令名 agents，不是 /agents。
        HostNameNormalizer = static name => name.TrimStart('/'),
        HostReason = "其余斜杠命令依赖当前会话的聊天上下文、TUI 回调、LLM 会话流或进程内长连接，独立 exe 内没有这些状态。"
    }, CliArgNormalizer.RestoreMsysPathExpansion(args));
}
