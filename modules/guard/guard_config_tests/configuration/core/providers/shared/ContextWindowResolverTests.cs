namespace Core.Configuration.Tests.Providers;

/// <summary>
/// ContextWindowResolver 环境变量边界测试 — 锁定 JCC_MAX_CONTEXT_TOKENS 解析守卫行为
/// 覆盖: 负数/零/非数字/空/null/正常值
/// 守卫位置: ContextWindowResolver.cs:40 — envValue &gt; 0
/// </summary>
public sealed class ContextWindowResolverTests {
    private const int DefaultContextWindow = 200_000;

    private static ContextWindowResolver CreateResolver() {
        var fastModeMock = new Mock<IFastModeService>();
        fastModeMock.SetupGet(x => x.IsFastModeActive).Returns(false);
        fastModeMock.SetupGet(x => x.PrimaryModelId).Returns("test-model");
        fastModeMock.SetupGet(x => x.FastModelId).Returns("test-fast-model");

        var registryMock = new Mock<IProviderDefinitionRegistry>();
        registryMock.Setup(x => x.TryGet(It.IsAny<string>())).Returns((IProviderDefinition?)null);

        return new ContextWindowResolver(fastModeMock.Object, registryMock.Object);
    }

    [Fact]
    public async Task Resolve_EnvNegative_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "-100");
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public async Task Resolve_EnvZero_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "0");
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public async Task Resolve_EnvNonNumeric_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "abc");
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public async Task Resolve_EnvEmpty_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, string.Empty);
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public async Task Resolve_EnvUnset_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, null);
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public async Task Resolve_EnvPositive_Should_Use_EnvValue() {
         using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "100000");
         await using var resolver = CreateResolver();
         resolver.ResolveCurrentContextWindow().Should().Be(100_000);
     }

    /// <summary>TASK061 — 模型配置窗口为 0（agnes 脏数据形态）：不再静默透传 0，
    /// 抛 ConfigurationException 带模型/供应商/修复指引（对齐"不对的直接报错"）。</summary>
    [Fact]
    public async Task Resolve_ModelConfigWindowZero_ThrowsWithGuidance() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, null);
        var loader = new ModelConfigLoader();
        loader.ApplyProviders(new Dictionary<string, ModelProviderConfig> {
            ["agnes"] = new() {
                Models = [new ModelItemConfig { Id = "test-model", DisplayName = "T", ContextWindow = 0 }]
            }
        });
        await using var resolver = CreateResolver(loader: loader, vendor: "agnes");

        var act = () => resolver.ResolveCurrentContextWindow();

        var ex = act.Should().Throw<JoinCode.Abstractions.Exceptions.ConfigurationException>().Which;
        ex.Message.Should().Contain("agnes/test-model");
        ex.Message.Should().Contain("providers_llm");
    }

    /// <summary>TASK061 — 合法 1M 窗口模型：透传真实窗口（不被 200K 兜底截断 — 1M 时代误折叠防护）。</summary>
    [Fact]
    public async Task Resolve_ModelConfigWindow1M_PassesThrough() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, null);
        var loader = new ModelConfigLoader();
        loader.ApplyProviders(new Dictionary<string, ModelProviderConfig> {
            ["sensenova"] = new() {
                Models = [new ModelItemConfig { Id = "test-model", DisplayName = "T", ContextWindow = 1_048_576 }]
            }
        });
        await using var resolver = CreateResolver(loader: loader, vendor: "sensenova");

        resolver.ResolveCurrentContextWindow().Should().Be(1_048_576);
    }

    private static ContextWindowResolver CreateResolver(IModelConfigLoader? loader = null, string? vendor = null) {
        var fastModeMock = new Mock<IFastModeService>();
        fastModeMock.SetupGet(x => x.IsFastModeActive).Returns(false);
        fastModeMock.SetupGet(x => x.PrimaryModelId).Returns("test-model");
        fastModeMock.SetupGet(x => x.FastModelId).Returns("test-fast-model");

        var registryMock = new Mock<IProviderDefinitionRegistry>();
        registryMock.Setup(x => x.TryGet(It.IsAny<string>())).Returns((IProviderDefinition?)null);

        var config = vendor is null ? null : new WorkflowConfig { Provider = new ProviderConfig { Vendor = vendor } };
        return new ContextWindowResolver(fastModeMock.Object, registryMock.Object, config, loader);
    }
}
