namespace Integration.Tests.PrefixCache.Unit;

public sealed class SystemPromptBuilderPartitionTests
{
    [Fact]
    public void BuildPartitioned_SeparatesStaticAndDynamic()
    {
        var builder = new SystemPromptBuilder();
        builder.AddSection(SystemPromptSection.Cached("static_a", () => "Static content A"));
        builder.AddSection(SystemPromptSection.Cached("static_b", () => "Static content B"));
        builder.AddSection(SystemPromptSection.Dynamic("dynamic_a", () => "Dynamic content A"));
        builder.AddSection(SystemPromptSection.Dynamic("dynamic_b", () => "Dynamic content B"));

        var (staticPrefix, dynamicSuffix) = builder.BuildPartitioned();

        staticPrefix.Should().Contain("Static content A");
        staticPrefix.Should().Contain("Static content B");
        staticPrefix.Should().NotContain("Dynamic content");

        dynamicSuffix.Should().Contain("Dynamic content A");
        dynamicSuffix.Should().Contain("Dynamic content B");
        dynamicSuffix.Should().NotContain("Static content");
    }

    [Fact]
    public void BuildPartitioned_StaticPrefix_StableAcrossCalls()
    {
        var builder = new SystemPromptBuilder();
        builder.AddSection(SystemPromptSection.Cached("static", () => "Fixed content"));
        builder.AddSection(SystemPromptSection.Dynamic("dynamic", () => $"Time: {DateTimeOffset.UtcNow.Ticks}"));

        var (static1, _) = builder.BuildPartitioned();
        var (static2, _) = builder.BuildPartitioned();

        static1.Should().Be(static2,
            "static prefix must be byte-identical across calls");
    }

    [Fact]
    public void BuildPartitioned_DynamicSuffix_MayChangeAcrossCalls()
    {
        var callCount = 0;
        var builder = new SystemPromptBuilder();
        builder.AddSection(SystemPromptSection.Cached("static", () => "Fixed"));
        builder.AddSection(SystemPromptSection.Dynamic("counter", () => $"Count: {++callCount}"));

        var (_, dynamic1) = builder.BuildPartitioned();
        var (_, dynamic2) = builder.BuildPartitioned();

        dynamic1.Should().NotBe(dynamic2,
            "dynamic suffix should reflect changes");
    }

    [Fact]
    public void BuildPartitioned_NoDynamicSections_ReturnsEmptyDynamicSuffix()
    {
        var builder = new SystemPromptBuilder();
        builder.AddSection(SystemPromptSection.Cached("static", () => "Only static"));

        var (staticPrefix, dynamicSuffix) = builder.BuildPartitioned();

        staticPrefix.Should().Contain("Only static");
        dynamicSuffix.Should().BeEmpty();
    }

    [Fact]
    public void BuildPartitioned_NoStaticSections_ReturnsEmptyStaticPrefix()
    {
        var builder = new SystemPromptBuilder();
        builder.AddSection(SystemPromptSection.Dynamic("dynamic", () => "Only dynamic"));

        var (staticPrefix, dynamicSuffix) = builder.BuildPartitioned();

        staticPrefix.Should().BeEmpty();
        dynamicSuffix.Should().Contain("Only dynamic");
    }

    [Fact]
    public void Build_BackwardCompatible_ReturnsCombinedResult()
    {
        var builder = new SystemPromptBuilder();
        builder.AddSection(SystemPromptSection.Cached("static", () => "Static part"));
        builder.AddSection(SystemPromptSection.Dynamic("dynamic", () => "Dynamic part"));

        var combined = builder.Build();
        var (staticPrefix, dynamicSuffix) = builder.BuildPartitioned();

        var expected = string.IsNullOrWhiteSpace(dynamicSuffix)
            ? staticPrefix
            : $"{staticPrefix}\n\n{dynamicSuffix}";

        combined.Should().Be(expected,
            "Build() should return the same result as combining partitioned output");
    }

    [Fact]
    public void BuildPartitioned_SkipsNullSections()
    {
        var builder = new SystemPromptBuilder();
        builder.AddSection(SystemPromptSection.Cached("static_a", () => "Content A"));
        builder.AddSection(SystemPromptSection.Cached("static_null", new Func<string?>(() => null)));
        builder.AddSection(SystemPromptSection.Dynamic("dynamic_a", () => "Dynamic A"));
        builder.AddSection(SystemPromptSection.Dynamic("dynamic_null", new Func<string?>(() => null)));

        var (staticPrefix, dynamicSuffix) = builder.BuildPartitioned();

        staticPrefix.Should().Contain("Content A");
        staticPrefix.Should().NotContain("null");

        dynamicSuffix.Should().Contain("Dynamic A");
        dynamicSuffix.Should().NotContain("null");
    }
}
