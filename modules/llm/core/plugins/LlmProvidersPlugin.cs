namespace JoinCode.Llm.Plugins;

/// <summary>
/// LLM 供应商插件 — 注册 Anthropic/Azure/Agnes/OpenAIResponses/Jev/Hub 六个协议实现
/// <para>万物皆插件(ADR 0098): 从 QueryServiceFactory switch 硬编码迁移为注册表+插件加载</para>
/// <para>TASK100 S3：清单收敛到 <see cref="Api.LLM.QueryServices.Nodes.LlmProviderNodes.All"/>，
/// 本插件不再自带一份重复表（此前它与 <c>ServiceRegistration.EnsureDefaultProvidersRegistered</c> 各写一遍）。</para>
/// <para>卸载时撤销注册,实现可逆效应</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class LlmProvidersPlugin : WorkflowPluginBase {
    public LlmProvidersPlugin() : base("LlmProviders") { }

    /// <summary>插件名称</summary>
    public override string Name => "LlmProviders";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "LLM供应商插件(Anthropic/Azure/Agnes/OpenAIResponses/Jev/Hub)";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 按节点清单向 QueryServiceFactory 注册供应商（幂等）</summary>
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) {
        JoinCode.Llm.DependencyInjection.ServiceRegistration.EnsureDefaultProvidersRegistered();
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>插件特定清理 — 撤销全部供应商注册并允许重新加载</summary>
    protected override void OnUnload()
        => JoinCode.Llm.DependencyInjection.ServiceRegistration.ResetProviders();
}
