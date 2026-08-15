using JoinCode.Abstractions.Configuration.Llm;

namespace Abs.Tests.LLM;

/// <summary>
/// ModelModalityKind [Flags] 枚举 + JsonConverter 单元测试
/// </summary>
public class ModelModalityKindTests
{
    #region [Flags] 位运算

    [Fact]
    public void HasFlag_SingleFlag_ShouldReturnTrue()
    {
        var modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ToolUse;

        modalities.HasFlag(ModelModalityKind.Text).Should().BeTrue();
        modalities.HasFlag(ModelModalityKind.ReadImage).Should().BeTrue();
        modalities.HasFlag(ModelModalityKind.ToolUse).Should().BeTrue();
    }

    [Fact]
    public void HasFlag_MissingFlag_ShouldReturnFalse()
    {
        var modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage;

        modalities.HasFlag(ModelModalityKind.Thinking).Should().BeFalse();
        modalities.HasFlag(ModelModalityKind.GenerateImage).Should().BeFalse();
        modalities.HasFlag(ModelModalityKind.ReadVideo).Should().BeFalse();
    }

    [Fact]
    public void None_HasNoFlags()
    {
        ModelModalityKind.None.HasFlag(ModelModalityKind.Text).Should().BeFalse();
        ModelModalityKind.None.HasFlag(ModelModalityKind.ReadImage).Should().BeFalse();
    }

    [Fact]
    public void AllInput_CombinesReadModalities()
    {
        var allInput = ModelModalityKind.AllInput;

        allInput.HasFlag(ModelModalityKind.ReadImage).Should().BeTrue();
        allInput.HasFlag(ModelModalityKind.ReadGif).Should().BeTrue();
        allInput.HasFlag(ModelModalityKind.ReadVideo).Should().BeTrue();
        allInput.HasFlag(ModelModalityKind.ReadAudio).Should().BeTrue();
        allInput.HasFlag(ModelModalityKind.ReadPdf).Should().BeTrue();
        allInput.HasFlag(ModelModalityKind.GenerateImage).Should().BeFalse();
    }

    [Fact]
    public void AllOutput_CombinesGenerateModalities()
    {
        var allOutput = ModelModalityKind.AllOutput;

        allOutput.HasFlag(ModelModalityKind.GenerateImage).Should().BeTrue();
        allOutput.HasFlag(ModelModalityKind.GenerateVideo).Should().BeTrue();
        allOutput.HasFlag(ModelModalityKind.GenerateAudio).Should().BeTrue();
        allOutput.HasFlag(ModelModalityKind.ReadImage).Should().BeFalse();
    }

    [Fact]
    public void All_ContainsEverything()
    {
        var all = ModelModalityKind.All;

        all.HasFlag(ModelModalityKind.Text).Should().BeTrue();
        all.HasFlag(ModelModalityKind.AllInput).Should().BeTrue();
        all.HasFlag(ModelModalityKind.AllOutput).Should().BeTrue();
        all.HasFlag(ModelModalityKind.Thinking).Should().BeTrue();
        all.HasFlag(ModelModalityKind.ToolUse).Should().BeTrue();
    }

    [Fact]
    public void BitValues_ArePowersOfTwo()
    {
        var singleFlags = new[] {
            ModelModalityKind.Text, ModelModalityKind.ReadImage, ModelModalityKind.ReadGif,
            ModelModalityKind.ReadVideo, ModelModalityKind.ReadAudio, ModelModalityKind.ReadPdf,
            ModelModalityKind.GenerateImage, ModelModalityKind.GenerateVideo, ModelModalityKind.GenerateAudio,
            ModelModalityKind.Thinking, ModelModalityKind.CodeExecution, ModelModalityKind.WebSearch,
            ModelModalityKind.ToolUse
        };

        foreach (var flag in singleFlags)
        {
            var value = (int)flag;
            (value & (value - 1)).Should().Be(0, $"{flag} 应该是 2 的幂次方");
        }
    }

    #endregion

    #region [EnumValue] 字符串映射

    [Fact]
    public void ToValue_ReturnsCorrectStrings()
    {
        ModelModalityKind.Text.ToValue().Should().Be("text");
        ModelModalityKind.ReadImage.ToValue().Should().Be("readImage");
        ModelModalityKind.ReadGif.ToValue().Should().Be("readGif");
        ModelModalityKind.ReadVideo.ToValue().Should().Be("readVideo");
        ModelModalityKind.ReadAudio.ToValue().Should().Be("readAudio");
        ModelModalityKind.ReadPdf.ToValue().Should().Be("readPdf");
        ModelModalityKind.GenerateImage.ToValue().Should().Be("generateImage");
        ModelModalityKind.GenerateVideo.ToValue().Should().Be("generateVideo");
        ModelModalityKind.GenerateAudio.ToValue().Should().Be("generateAudio");
        ModelModalityKind.Thinking.ToValue().Should().Be("thinking");
        ModelModalityKind.CodeExecution.ToValue().Should().Be("codeExecution");
        ModelModalityKind.WebSearch.ToValue().Should().Be("webSearch");
        ModelModalityKind.ToolUse.ToValue().Should().Be("toolUse");
    }

    [Fact]
    public void FromValue_ReturnsCorrectEnum()
    {
        ModelModalityKindExtensions.FromValue("text").Should().Be(ModelModalityKind.Text);
        ModelModalityKindExtensions.FromValue("readImage").Should().Be(ModelModalityKind.ReadImage);
        ModelModalityKindExtensions.FromValue("thinking").Should().Be(ModelModalityKind.Thinking);
        ModelModalityKindExtensions.FromValue("toolUse").Should().Be(ModelModalityKind.ToolUse);
        ModelModalityKindExtensions.FromValue("generateImage").Should().Be(ModelModalityKind.GenerateImage);
    }

    #endregion

    #region JsonConverter 序列化/反序列化

    [Fact]
    public void JsonConverter_Serialize_WritesStringArray()
    {
        var config = new ModelCapabilitiesConfig
        {
            FastMode = true,
            Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ToolUse
        };

        var json = JsonSerializer.Serialize(config);
        json.Should().Contain("\"Modalities\"");
        json.Should().Contain("\"text\"");
        json.Should().Contain("\"readImage\"");
        json.Should().Contain("\"toolUse\"");
        json.Should().NotContain("\"thinking\"");
    }

    [Fact]
    public void JsonConverter_Deserialize_ReadsStringArray()
    {
        var json = """{"FastMode":true,"Modalities":["text","readImage","toolUse"]}""";

        var config = JsonSerializer.Deserialize<ModelCapabilitiesConfig>(json)!;

        config.Modalities.HasFlag(ModelModalityKind.Text).Should().BeTrue();
        config.Modalities.HasFlag(ModelModalityKind.ReadImage).Should().BeTrue();
        config.Modalities.HasFlag(ModelModalityKind.ToolUse).Should().BeTrue();
        config.Modalities.HasFlag(ModelModalityKind.Thinking).Should().BeFalse();
    }

    [Fact]
    public void JsonConverter_Deserialize_ReadsIntegerBackwardCompat()
    {
        var json = """{"FastMode":true,"Modalities":4103}""";

        var config = JsonSerializer.Deserialize<ModelCapabilitiesConfig>(json)!;

        config.Modalities.HasFlag(ModelModalityKind.Text).Should().BeTrue();
        config.Modalities.HasFlag(ModelModalityKind.ReadImage).Should().BeTrue();
        config.Modalities.HasFlag(ModelModalityKind.ToolUse).Should().BeTrue();
    }

    [Fact]
    public void JsonConverter_RoundTrip_PreservesFlags()
    {
        var original = new ModelCapabilitiesConfig
        {
            FastMode = false,
            ThinkingMode = true,
            Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ReadGif | ModelModalityKind.ReadPdf | ModelModalityKind.Thinking | ModelModalityKind.ToolUse
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ModelCapabilitiesConfig>(json)!;

        deserialized.Modalities.Should().Be(original.Modalities);
        deserialized.FastMode.Should().Be(original.FastMode);
        deserialized.ThinkingMode.Should().Be(original.ThinkingMode);
    }

    [Fact]
    public void JsonConverter_NoneSerializesAsEmptyArray()
    {
        var config = new ModelCapabilitiesConfig
        {
            Modalities = ModelModalityKind.None
        };

        var json = JsonSerializer.Serialize(config);
        json.Should().Contain("\"Modalities\":[]");
    }

    #endregion

    #region ModelConfigLoader.SupportsModality

    [Fact]
    public void ModelConfigLoader_SupportsModality_ReturnsTrueForConfiguredFlag()
    {
        var loader = new ModelConfigLoader();
        loader.ApplyProviders(new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = new ModelProviderConfig
            {
                DefaultModelId = "test-model",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "test-model",
                        DisplayName = "Test Model",
                        ContextWindow = 128000,
                        Capabilities = new ModelCapabilitiesConfig
                        {
                            Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ToolUse
                        }
                    }
                ]
            }
        });

        loader.SupportsModality("test", "test-model", ModelModalityKind.Text).Should().BeTrue();
        loader.SupportsModality("test", "test-model", ModelModalityKind.ReadImage).Should().BeTrue();
        loader.SupportsModality("test", "test-model", ModelModalityKind.ToolUse).Should().BeTrue();
        loader.SupportsModality("test", "test-model", ModelModalityKind.Thinking).Should().BeFalse();
        loader.SupportsModality("test", "test-model", ModelModalityKind.GenerateImage).Should().BeFalse();
    }

    [Fact]
    public void ModelConfigLoader_GetModalities_ReturnsConfiguredFlags()
    {
        var loader = new ModelConfigLoader();
        var expected = ModelModalityKind.Text | ModelModalityKind.Thinking | ModelModalityKind.ToolUse;
        loader.ApplyProviders(new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = new ModelProviderConfig
            {
                DefaultModelId = "reasoner",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "reasoner",
                        DisplayName = "Reasoner",
                        ContextWindow = 200000,
                        Capabilities = new ModelCapabilitiesConfig { Modalities = expected }
                    }
                ]
            }
        });

        loader.GetModalities("test", "reasoner").Should().Be(expected);
    }

    [Fact]
    public void ModelConfigLoader_SupportsModality_UnknownModel_DefaultIsText()
    {
        var loader = new ModelConfigLoader();
        loader.SupportsModality("test", "unknown", ModelModalityKind.Text).Should().BeTrue();
        loader.SupportsModality("test", "unknown", ModelModalityKind.ReadImage).Should().BeFalse();
        loader.SupportsModality("test", "unknown", ModelModalityKind.Thinking).Should().BeFalse();
    }

    [Fact]
    public void ModelConfigLoader_GetModalities_UnknownModel_ReturnsText()
    {
        var loader = new ModelConfigLoader();
        loader.GetModalities("test", "unknown").Should().Be(ModelModalityKind.Text);
    }

    #endregion
}
