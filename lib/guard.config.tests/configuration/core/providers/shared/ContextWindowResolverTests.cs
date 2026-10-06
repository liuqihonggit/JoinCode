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
    public void Resolve_EnvNegative_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "-100");
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public void Resolve_EnvZero_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "0");
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public void Resolve_EnvNonNumeric_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "abc");
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public void Resolve_EnvEmpty_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, string.Empty);
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public void Resolve_EnvUnset_Should_Fallback_To_Default() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, null);
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(DefaultContextWindow);
    }

    [Fact]
    public void Resolve_EnvPositive_Should_Use_EnvValue() {
        using var env = EnvVarScope.Set(JccEnvVarEnumConstants.MaxContextTokens, "100000");
        await using var resolver = CreateResolver();
        resolver.ResolveCurrentContextWindow().Should().Be(100_000);
    }
}
