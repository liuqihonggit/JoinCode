namespace JoinCode.Llm.Plugins;

/// <summary>
/// LLM 供应商插件 — 注册 Anthropic/Azure/Agnes/OpenAIResponses/OpenAI 五个供应商
/// <para>万物皆插件(ADR 0098): 从 QueryServiceFactory switch 硬编码迁移为注册表+插件加载</para>
/// <para>卸载时撤销注册,实现可逆效应</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class LlmProvidersPlugin : WorkflowPluginBase
{
    public LlmProvidersPlugin() : base("LlmProviders") { }

    /// <summary>插件名称</summary>
    public override string Name => "LlmProviders";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "LLM供应商插件(Anthropic/Azure/Agnes/OpenAIResponses/OpenAI)";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 向 QueryServiceFactory 注册5个供应商</summary>
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var factory = JoinCode.Llm.DependencyInjection.ServiceRegistration.s_factory;

        factory.RegisterProvider(ProtocolKind.Anthropic,
            (config, http, logger, fs, executor) => new AnthropicQueryService(config, http, logger, fs, executor));
        factory.RegisterProvider(ProtocolKind.Azure,
            (config, http, logger, fs, executor) => new AzureQueryService(config, http, logger, fs, executor));
        factory.RegisterProvider(ProtocolKind.Agnes,
            (config, http, logger, fs, executor) => new AgnesQueryService(config, http, logger, fs, executor));
        factory.RegisterProvider(ProtocolKind.OpenAiResponses,
            (config, http, logger, fs, executor) => new ResponsesQueryService(config, http, logger, fs, executor));
        factory.RegisterDefault(
            (config, http, logger, fs, executor) => new OpenAIQueryService(config, http, logger, fs, executor));

        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>插件特定清理 — 撤销5个供应商注册</summary>
    protected override void OnUnload()
    {
        var factory = JoinCode.Llm.DependencyInjection.ServiceRegistration.s_factory;
        factory.UnregisterProvider(ProtocolKind.Anthropic);
        factory.UnregisterProvider(ProtocolKind.Azure);
        factory.UnregisterProvider(ProtocolKind.Agnes);
        factory.UnregisterProvider(ProtocolKind.OpenAiResponses);
    }
}
