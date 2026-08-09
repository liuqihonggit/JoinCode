using FluentAssertions;

using IO.FileSystem;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Configuration;
using JoinCode.Abstractions.Configuration.Providers;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Core.DependencyInjection;
using JoinCode.Pipelines;
using Api.Chat;
using JoinCode.Gui.Hosting;

namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// 引擎会话组装测试 — 验证 GUI 进程内引擎接入的关键假设：
/// <c>WorkflowConfig.PipeEndpoint = null</c> 时应走标准 HTTP QueryService，
/// 而非命名管道服务（GUI 不连接 Bridge 管道服务进程）。
/// 不依赖真实磁盘配置，直接复现 JccChatSession.CreateAsync 的 DI 组装，保证确定性。
/// </summary>
public class JccChatSessionAssemblyTests
{
    /// <summary>
    /// 组装引擎会话所需的完整 DI（与 Jcc 一致）：AiWorkflowServices + 共享管道 + ChatService 注册。
    /// </summary>
    private static IServiceProvider BuildEngineProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().Build());
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = "sk-test-non-pipe",
                ModelId = "gpt-4o"
            },
            PipeEndpoint = null
        };

        services.AddAiWorkflowServices(config);
        services.AddAllPipelines();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void EngineAssembly_WithNullPipeEndpoint_ResolvesChatService()
    {
        var sp = BuildEngineProvider();

        var chat = sp.GetRequiredService<IChatService>();

        chat.Should().NotBeNull();
    }

    [Fact]
    public void EngineAssembly_WithNullPipeEndpoint_DoesNotUsePipeQueryService()
    {
        var sp = BuildEngineProvider();

        var query = sp.GetRequiredService<IQueryService>();
        var impl = query.GetType();

        impl.Should().NotBe(typeof(Api.Chat.PipeQueryService),
            "PipeEndpoint=null 时应注册标准 HTTP QueryService，而非命名管道服务");
    }

    [Fact]
    public void ModelSurface_ExposesProviderAndCurrentModelFromSharedConfig()
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        session.CurrentProvider.Should().Be("openai");
        session.CurrentModelId.Should().Be("gpt-4o");
    }

    [Fact]
    public void ModelSurface_AvailableModels_ComesFromSharedModelConfigLoader()
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        var expected = JoinCode.Abstractions.Configuration.Llm.ModelConfigLoader
            .GetModels("openai").Select(m => m.Id).ToArray();

        session.AvailableModels.Should().BeEquivalentTo(expected);
        session.AvailableModels.Should().Contain("gpt-4o");
    }

    [Fact]
    public void ModelSurface_AvailableModels_IncludesCurrentModelWhenNotInCatalog()
    {
        // 用户场景：provider=openai 但 endpoint 是商汤 senseNova（OpenAI 兼容），
        // 模型 sensenova-6.7-flash-lite 不在内置 models.json 的 openai 组。
        // 对齐 CLI ModelCatalog.EnsureCurrentModelInList：当前模型必须出现在列表中，
        // 否则下拉切换模型会把请求发往错误 endpoint 导致 404。
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ModelId = "sensenova-6.7-flash-lite"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        session.AvailableModels.Should().Contain("sensenova-6.7-flash-lite");
    }

    [Fact]
    public void ModelSurface_AvailableModels_DoesNotDuplicateCatalogModel()
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        session.AvailableModels.Count(m => m == "gpt-4o").Should().Be(1);
    }

    [Fact]
    public void EngineAssembly_ResolvesExecutionSettingsProvider()
    {
        // 对齐 CLI /effort：GUI 的 AddAiWorkflowServices 必须能解析 IExecutionSettingsProvider，
        // 否则 ChatOptionsFactory._executionSettingsProvider 为 null，EffortLevel 永不生效。
        var sp = BuildEngineProvider();

        var settings = sp.GetService<IExecutionSettingsProvider>();

        settings.Should().NotBeNull();
    }

    [Fact]
    public void EngineAssembly_ExecutionSettings_DefaultsToAuto()
    {
        // 无持久化设置时，EffortLevel 默认 Auto（模型默认级别）— 对齐 CLI ShowCurrentEffort。
        // 直接构造 ExecutionSettingsProvider + InMemoryFileSystem，隔离真实磁盘，保证确定性
        // （物理磁盘 ~/.jcc/settings.json 可能含用户 effortLevel=low）。
        var provider = new ExecutionSettingsProvider(
            new WorkflowConfig
            {
                Provider = new ProviderConfig { Provider = "openai", ModelId = "gpt-4o" }
            },
            new InMemoryFileSystem(),
            null!);

        provider.EffortLevel.Should().Be(EffortLevel.Auto);
    }

    [Fact]
    public async Task SetModelAsync_PersistsModelToSettingsJson()
    {
        // 对齐 CLI ModelCommand.ApplyModelSwitchAsync：切换模型需持久化 modelId
        // 到 settings.json（键 "model"，与 SettingsJson 生成器 jsonName 一致），
        // 否则 GUI 重启后回到默认模型再次触发 404。
        var fs = new InMemoryFileSystem();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddSingleton<IConfigurationService, Core.Configuration.ConfigurationService>();
        var sp = services.BuildServiceProvider();

        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(sp, null!, config);

        await session.SetModelAsync("sensenova-6.7-flash-lite");

        var value = await sp.GetRequiredService<IConfigurationService>()
            .GetAsync("model");
        value.Should().Be("sensenova-6.7-flash-lite");
    }
}