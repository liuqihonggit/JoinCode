namespace JoinCode.App.Builder;

/// <summary>
/// 模块上下文 — 跨模块共享数据，替代方法参数传递
/// </summary>
public sealed class AppModuleContext
{
    /// <summary>命令行选项</summary>
    public required CommandLineOptions Options { get; init; }

    /// <summary>工作流配置</summary>
    public required WorkflowConfig Config { get; init; }
}
