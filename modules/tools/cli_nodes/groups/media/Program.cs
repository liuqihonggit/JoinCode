// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.Media;

/// <summary>
/// media 分组独立 CLI.exe — 「外部可调用工具」下沉样板。
/// <para>20 个纯 Win32/BCL 工具（进程/输入注入/窗口/覆盖层/测量）在本 exe 内零依赖实现；
/// 依赖 vision LLM、跨调用共享状态（场景/撤销栈/宏录制）、voice-vcr 单例或 SkiaSharp 原生库的工具
/// 由宿主 jcc 内执行——它们属于 Agent 功能，本就不该是独立 exe。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "media",
        Title = "media 分组",
        HelpResource = "JoinCode.CliNodes.Groups.media.help.txt",
        Resolve = MediaCommands.Find,
        LocalToolIds = () => MediaCommands.LocalToolIds,
        HostReason = "依赖宿主 vision LLM / 跨调用共享状态（场景/撤销栈/录制）/ 单例服务（voice/vcr）/ SkiaSharp 原生库的工具属于 Agent 功能，在 jcc 内执行。"
    }, args);
}
