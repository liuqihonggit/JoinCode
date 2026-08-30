namespace Guard.Tests.Configuration;

/// <summary>
/// 配置 JSON 序列化测试 — 验证通过 RelaxedJsonSerializer 输出真实中文（非 \uXXXX 转义）+ camelCase 字段名。
/// 回归背景：settings.json 曾因 JsonSerializerOptions 未设 Encoder 导致中文转义为 \u8F7B\u91CF...，
/// 且 ModelItemConfig 无 [JsonPropertyName] 导致字段名 PascalCase（Id/DisplayName/Description），
/// 与顶层 [JsonPropertyName("vendor")] 的 camelCase 混用，用户阅读困难。
/// </summary>
public class ConfigJsonOptionsTests
{
    [Fact]
    public void SerializeIndented_含中文Description_输出真实中文而非Unicode转义()
    {
        var settings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>
            {
                ["sensenova"] = new ProfileSettings
                {
                    Provider = "sensenova",
                    Model = "sensenova-6.7-flash-lite",
                    Models =
                    [
                        new ModelItemConfig
                        {
                            Id = "sensenova-6.7-flash-lite",
                            DisplayName = "SenseNova 6.7 Flash-Lite",
                            Description = "轻量多模态智能体模型，支持文本对话与图像输入理解",
                        }
                    ]
                }
            }
        };

        var json = RelaxedJsonSerializer.SerializeIndented(settings, ConfigIndentedJsonContext.Default);

        json.Should().Contain("轻量多模态智能体模型");
        json.Should().NotContain("\\u8F7B");
        json.Should().NotContain("\\u91CF");
    }

    [Fact]
    public void SerializeIndented_模型字段_输出camelCase而非PascalCase()
    {
        var settings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>
            {
                ["test"] = new ProfileSettings
                {
                    Models =
                    [
                        new ModelItemConfig { Id = "m1", DisplayName = "Model1", Description = "描述" }
                    ]
                }
            }
        };

        var json = RelaxedJsonSerializer.SerializeIndented(settings, ConfigIndentedJsonContext.Default);

        json.Should().Contain("\"displayName\"");
        json.Should().Contain("\"description\"");
        json.Should().Contain("\"contextWindow\"");
        json.Should().Contain("\"canonicalId\"");
        json.Should().NotContain("\"DisplayName\"");
        json.Should().NotContain("\"Description\"");
        json.Should().NotContain("\"ContextWindow\"");
    }

    [Fact]
    public void SerializeCompact_含中文_输出真实中文()
    {
        var data = new Dictionary<string, string>
        {
            ["endpoint"] = "https://example.com",
            ["note"] = "中文备注"
        };

        var json = RelaxedJsonSerializer.SerializeCompact(data, ConfigJsonContext.Default);

        json.Should().Contain("中文备注");
        json.Should().NotContain("\\u4E2D\\u6587");
    }
}
