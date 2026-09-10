namespace JoinCode.CodeIndex;

/// <summary>
/// C# 语言索引插件 — 注册 CSharpSymbolExtractor 作为语言插件
/// <para>万物皆插件(ADR 0098): 从 CodeIndexer 构造时硬编码迁移为插件加载</para>
/// <para>卸载时重置为默认工厂,实现可逆效应</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class CSharpLanguagePlugin : WorkflowPluginBase
{
    private CodeIndexer? _indexer;

    public CSharpLanguagePlugin() : base("CSharpLanguage") { }

    /// <summary>插件名称</summary>
    public override string Name => "CSharpLanguage";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "C#语言索引插件(CSharpSymbolExtractor)";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 从 DI 获取 CodeIndexer,设置 C# 语言插件工厂</summary>
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        _indexer = serviceProvider.GetRequiredService<CodeIndexer>();
        _indexer.SetLanguagePluginFactory(static () => new CSharpSymbolExtractor());
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>插件特定清理 — 重置为默认工厂</summary>
    protected override void OnUnload()
    {
        _indexer?.SetLanguagePluginFactory(static () => new CSharpSymbolExtractor());
        _indexer = null;
    }
}
