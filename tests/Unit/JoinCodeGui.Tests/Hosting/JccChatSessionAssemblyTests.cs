using FluentAssertions;

using IO.FileSystem;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Configuration;
using JoinCode.Abstractions.Configuration.Providers;
using JoinCode.Abstractions.Configuration.Settings;
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
    [Fact]
    public void VendorModelMap_DumpAllData()
    {
        var session = new PlaceholderChatSession();
        var map = session.VendorModelMap;

        map.Keys.Should().BeEquivalentTo(["anthropic", "openai", "deepseek", "agnes", "sensenova"]);
        map.Count.Should().Be(5);

        map["deepseek"].Should().BeEquivalentTo(["deepseek-v4-flash", "deepseek-v4-pro"]);
        map["deepseek"].Count.Should().Be(2);

        map["openai"].Should().BeEquivalentTo([
            "gpt-4o-mini", "gpt-4o", "gpt-4-turbo", "gpt-4", "gpt-3.5-turbo",
            "gpt-4.1-nano", "gpt-4.1-mini", "gpt-4.1", "o4-mini", "o3-mini", "o3", "o1-mini", "o1"
        ]);
        map["openai"].Count.Should().Be(13);

        map["anthropic"].Should().BeEquivalentTo([
            "claude-opus-4-7-20250701", "claude-opus-4-6-20250514", "claude-opus-4-5-20251101",
            "claude-opus-4-1-20250805", "claude-opus-4-20250514", "claude-sonnet-4-6-20250514",
            "claude-sonnet-4-5-20250514", "claude-sonnet-4-20250514", "claude-haiku-4-5-20251001",
            "claude-3-7-sonnet-20250219", "claude-3-5-sonnet-20241022", "claude-3-5-haiku-20241022",
            "claude-3-opus-20240229", "claude-3-sonnet-20240229", "claude-3-haiku-20240307"
        ]);
        map["anthropic"].Count.Should().Be(15);

        map["agnes"].Should().BeEquivalentTo([
            "agnes-1.5-flash", "agnes-2.0-flash", "agnes-image-2.0-flash", "agnes-image-2.1-flash", "agnes-video-v2.0"
        ]);
        map["agnes"].Count.Should().Be(5);

        map["sensenova"].Should().BeEquivalentTo([
            "sensenova-6.7-flash-lite", "sensenova-u1-fast", "deepseek-v4-flash"
        ]);
        map["sensenova"].Count.Should().Be(3);
    }

    [Fact]
    public void VendorModelMap_MultipleInstances_AreIdentical()
    {
        var s1 = new PlaceholderChatSession();
        var s2 = new PlaceholderChatSession();
        var s3 = new PlaceholderChatSession();

        s1.VendorModelMap.Keys.Should().BeEquivalentTo(s2.VendorModelMap.Keys);
        s2.VendorModelMap.Keys.Should().BeEquivalentTo(s3.VendorModelMap.Keys);

        foreach (var key in s1.VendorModelMap.Keys)
        {
            s1.VendorModelMap[key].Should().BeEquivalentTo(s2.VendorModelMap[key]);
            s2.VendorModelMap[key].Should().BeEquivalentTo(s3.VendorModelMap[key]);
        }
    }

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
                Vendor = "openai",
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
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        session.CurrentVendor.Should().Be("openai");
        session.CurrentModelId.Should().Be("gpt-4o");
    }

    [Fact]
    public void ModelSurface_VendorModelMap_ComesFromSharedModelConfigLoader()
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        var expected = JoinCode.Abstractions.Configuration.Llm.ModelConfigLoader
            .GetModels("openai").Select(m => m.Id).ToArray();

        session.VendorModelMap["openai"].Should().BeEquivalentTo(expected);
        session.VendorModelMap["openai"].Should().Contain("gpt-4o");
    }

    [Fact]
    public void ModelSurface_VendorModelMap_DoesNotIncludeCustomModel()
    {
        // VendorModelMap 是纯配置数据，不追加当前模型（追加逻辑在 MainViewModel.RebuildModelOptionsCache）
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Vendor = "openai",
                ModelId = "sensenova-6.7-flash-lite"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        session.VendorModelMap["openai"].Should().NotContain("sensenova-6.7-flash-lite");
    }

    [Fact]
    public void ModelSurface_VendorModelMap_DoesNotDuplicateCatalogModel()
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        session.VendorModelMap["openai"].Count(m => m == "gpt-4o").Should().Be(1);
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
                Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
            },
            new InMemoryFileSystem(),
            null!);

        provider.EffortLevel.Should().Be(EffortLevel.Auto);
    }

    [Fact]
    public async Task SetEffortLevelAsync_PersistsHighToSettingsJson()
    {
        // 对齐 CLI EffortCommand.PersistEffortAsync：非 auto 级别写入 settings.json（键 effortLevel）
        var fs = new InMemoryFileSystem();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddSingleton<IConfigurationService, Core.Configuration.ConfigurationService>();
        var sp = services.BuildServiceProvider();

        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(sp, null!, config);

        await session.SetEffortLevelAsync(EffortLevel.High);

        var value = await sp.GetRequiredService<IConfigurationService>()
            .GetAsync(ConfigKeyConstants.EffortLevel);
        value.Should().Be("high");
    }

    [Fact]
    public async Task SetEffortLevelAsync_AutoRemovesPersistedKey()
    {
        // 对齐 CLI EffortCommand：auto → 移除 effortLevel 键（恢复模型默认）
        var fs = new InMemoryFileSystem();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddSingleton<IConfigurationService, Core.Configuration.ConfigurationService>();
        var sp = services.BuildServiceProvider();

        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(sp, null!, config);

        await session.SetEffortLevelAsync(EffortLevel.High);
        await session.SetEffortLevelAsync(EffortLevel.Auto);

        var value = await sp.GetRequiredService<IConfigurationService>()
            .GetAsync(ConfigKeyConstants.EffortLevel);
        value.Should().BeNull();
    }

    [Fact]
    public void Session_EffortLevel_DefaultsToAuto_WithoutRegisteredProvider()
    {
        // 未注册 IExecutionSettingsProvider 时，门面回退 Auto（对齐 CLI ShowCurrentEffort fallback）
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            config);

        session.EffortLevel.Should().Be(EffortLevel.Auto);
    }

    [Fact]
    public void Session_EffortLevel_ReflectsRegisteredProviderValue()
    {
        // 注册 IExecutionSettingsProvider 后，门面读取其当前 EffortLevel
        var provider = new ExecutionSettingsProvider(
            new WorkflowConfig
            {
                Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
            },
            new InMemoryFileSystem(),
            null!)
        {
            EffortLevel = EffortLevel.Medium
        };
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            new WorkflowConfig
            {
                Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
            },
            provider);

        session.EffortLevel.Should().Be(EffortLevel.Medium);
    }

    [Fact]
    public async Task SetTemperatureAndMaxTokens_WritesBackToSharedProvider()
    {
        // GUI 滑块写回：门面 SetTemperature/SetMaxTokens 应写入共享 IExecutionSettingsProvider，
        // 使 ChatOptionsFactory 下次创建时覆盖 LlmParameters.Chat 默认值。
        var provider = new ExecutionSettingsProvider(
            new WorkflowConfig
            {
                Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
            },
            new InMemoryFileSystem(),
            null!);
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null!,
            new WorkflowConfig
            {
                Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
            },
            provider);

        session.Temperature.Should().BeNull();
        session.MaxTokens.Should().BeNull();

        await session.SetTemperatureAsync(0.9f);
        await session.SetMaxTokensAsync(6000);

        provider.Temperature.Should().Be(0.9f);
        provider.MaxTokens.Should().Be(6000);
        session.Temperature.Should().Be(0.9f);
        session.MaxTokens.Should().Be(6000);
    }

    [Fact]
    public async Task SetSystemPromptAsync_ForwardsToChatService()
    {
        // 对齐 CLI SystemPromptApplyStep：GUI 编辑系统提示词后应经 IChatService.SetSystemPromptAsync
        // 应用（admin 管道，替换静态系统提示词），而非仅本地存储占位。
        var fakeChat = new RecordingChatService();
        var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            fakeChat,
            new WorkflowConfig
            {
                Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
            });

        await session.SetSystemPromptAsync("你是测试助手，请简洁回答");

        fakeChat.LastSystemPrompt.Should().Be("你是测试助手，请简洁回答");
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
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };
        var session = new JccChatSession(sp, null!, config);

        await session.SetModelAsync("sensenova-6.7-flash-lite");

        var value = await sp.GetRequiredService<IConfigurationService>()
            .GetAsync("model");
        value.Should().Be("sensenova-6.7-flash-lite");
    }

    /// <summary>
    /// 记录 SetSystemPromptAsync 调用的假 ChatService — 验证门面转发。
    /// </summary>
    private sealed class RecordingChatService : IChatService
    {
        public string? LastSystemPrompt { get; private set; }

        public Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public async IAsyncEnumerable<string> SendMessageStreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<ApiMessageRecord>)[]);

        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
        {
            LastSystemPrompt = systemPrompt;
            return Task.CompletedTask;
        }

        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        public Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.TruncateToIndex, 1, 0));

        public Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.ClearHistory, 0, 0));

        public Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}