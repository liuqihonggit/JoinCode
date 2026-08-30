namespace Abs.Tests.Utils;

/// <summary>
/// RelaxedJsonSerializer 单元测试 — 验证真实中文输出（非 \uXXXX 转义）与命名策略继承。
/// </summary>
public sealed partial class RelaxedJsonSerializerTests
{
    private sealed record TestDto(string DisplayName, string Description);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
    [JsonSerializable(typeof(TestDto))]
    private sealed partial class TestCamelCaseContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(TestDto))]
    private sealed partial class TestDefaultContext : JsonSerializerContext;

    [Fact]
    public void Serialize_WithRelaxedOptions_OutputsRealChineseNotEscaped()
    {
        var dto = new TestDto("轻量多模态", "这是一个中文描述");

        var json = RelaxedJsonSerializer.Serialize(dto, TestCamelCaseContext.Default);

        json.Should().Contain("轻量多模态");
        json.Should().Contain("这是一个中文描述");
        json.Should().NotContain("\\u");
    }

    [Fact]
    public void Serialize_WithCamelCaseContext_UsesCamelCaseFieldNames()
    {
        var dto = new TestDto("test", "desc");

        var json = RelaxedJsonSerializer.Serialize(dto, TestCamelCaseContext.Default);

        json.Should().Contain("\"displayName\"");
        json.Should().Contain("\"description\"");
        json.Should().NotContain("\"DisplayName\"");
    }

    [Fact]
    public void Serialize_WithDefaultContext_PreservesPascalCaseButStillRealChinese()
    {
        var dto = new TestDto("轻量", "描述");

        var json = RelaxedJsonSerializer.Serialize(dto, TestDefaultContext.Default);

        json.Should().Contain("轻量");
        json.Should().Contain("描述");
        json.Should().NotContain("\\u");
        json.Should().Contain("\"DisplayName\"");
    }

    [Fact]
    public void RelaxedOptions_CachesIndependently_EachCallCreatesNewInstance()
    {
        var opts1 = TestCamelCaseContext.Default.RelaxedOptions();
        var opts2 = TestCamelCaseContext.Default.RelaxedOptions();

        opts1.Should().NotBeSameAs(opts2);
        opts1.Encoder.Should().Be(JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
        opts2.Encoder.Should().Be(JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    }

    [Fact]
    public void Serialize_DefaultSerializerProducesEscaped_ProvingHelperMakesDifference()
    {
        var dto = new TestDto("中文", "描述");

        var defaultJson = JsonSerializer.Serialize(dto, TestCamelCaseContext.Default.Options);
        var relaxedJson = RelaxedJsonSerializer.Serialize(dto, TestCamelCaseContext.Default);

        defaultJson.Should().Contain("\\u");
        relaxedJson.Should().NotContain("\\u");
    }
}
