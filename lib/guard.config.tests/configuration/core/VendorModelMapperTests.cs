
namespace Guard.Tests.Configuration;

/// <summary>
/// VendorModelMapper 单元测试 — 验证 BuildProviders 在 models 缺少 capabilities 字段时的健壮性
/// <para>复现 CI Daily Command Test #32 失败: settings.json 中 models 未声明 capabilities 字段时，
/// JSON 反序列化将 Capabilities 设为 null，BuildProviders 访问 m.Capabilities.FastMode 抛 NRE。</para>
/// </summary>
public class VendorModelMapperTests {
    #region 场景1: models 缺少 capabilities 字段（CI 配置场景）

    [Fact]
    public void Given_models无capabilities字段_When_BuildProviders_Then_不抛异常且DefaultFastModelId回退首个模型() {
        var json = """
            {
              "vendor": {
                "sensenova": {
                  "provider": "sensenova",
                  "protocol": "openai-compatible",
                  "model": "sensenova-6.8-flash-lite",
                  "endpoint": "https://token.sensenova.cn/v1",
                  "apiKeyEnvVar": "SENSENOVA_API_KEY",
                  "models": [
                    {"id":"sensenova-6.8-flash-lite","canonicalId":"sensenova-6.8-flash-lite","displayName":"SenseNova 6.8 Flash-Lite","contextWindow":262144}
                  ]
                }
              },
              "current": { "profile": "sensenova" }
            }
            """;

        var settings = RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        var act = () => VendorModelMapper.BuildProviders(settings);

        var providers = act.Should().NotThrow("models 缺少 capabilities 字段时应降级处理而非 NRE").Subject;
        providers["sensenova"].DefaultFastModelId.Should().Be("sensenova-6.8-flash-lite",
            "无 capabilities 时应回退到 models 列表首个模型作为 FastModel");
    }

    [Fact]
    public void Given_多个models无capabilities_When_BuildProviders_Then_所有vendor均不抛异常() {
        var json = """
            {
              "vendor": {
                "sensenova": {
                  "provider": "sensenova",
                  "model": "sensenova-6.8-flash-lite",
                  "models": [{"id":"sensenova-6.8-flash-lite","displayName":"SenseNova","contextWindow":262144}]
                },
                "openai": {
                  "provider": "openai",
                  "model": "gpt-5.6-sol",
                  "models": [{"id":"gpt-5.6-sol","displayName":"GPT","contextWindow":200000}]
                },
                "deepseek": {
                  "provider": "deepseek",
                  "model": "deepseek-v4-flash",
                  "models": [{"id":"deepseek-v4-flash","displayName":"DeepSeek","contextWindow":128000}]
                }
              },
              "current": { "profile": "sensenova" }
            }
            """;

        var settings = RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        var providers = VendorModelMapper.BuildProviders(settings);

        providers.Should().HaveCount(3);
        providers["sensenova"].DefaultFastModelId.Should().Be("sensenova-6.8-flash-lite");
        providers["openai"].DefaultFastModelId.Should().Be("gpt-5.6-sol");
        providers["deepseek"].DefaultFastModelId.Should().Be("deepseek-v4-flash");
    }

    #endregion

    #region 场景2: models 有 capabilities 字段（正常路径回归）

    [Fact]
    public void Given_models有capabilities且FastMode为true_When_BuildProviders_Then_DefaultFastModelId选FastMode模型() {
        var json = """
            {
              "vendor": {
                "openai": {
                  "provider": "openai",
                  "model": "gpt-5.6-sol",
                  "models": [
                    {"id":"gpt-5.6-sol","displayName":"GPT","contextWindow":200000,"capabilities":{"fastMode":false}},
                    {"id":"gpt-5.6-mini","displayName":"GPT Mini","contextWindow":200000,"capabilities":{"fastMode":true}}
                  ]
                }
              },
              "current": { "profile": "openai" }
            }
            """;

        var settings = RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        var providers = VendorModelMapper.BuildProviders(settings);

        providers["openai"].DefaultFastModelId.Should().Be("gpt-5.6-mini",
            "应优先选择 FastMode=true 的模型");
    }

    #endregion

    #region 场景3: 边界情况

    [Fact]
    public void Given_nullSettings_When_BuildProviders_Then_返回空字典() {
        var providers = VendorModelMapper.BuildProviders(null);
        providers.Should().BeEmpty();
    }

    [Fact]
    public void Given_vendor无models_When_BuildProviders_Then_DefaultFastModelId为空() {
        var settings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase) {
                ["openai"] = new ProfileSettings { Provider = "openai", Model = "gpt-4o" },
            },
            Current = new CurrentSettings { Profile = "openai" },
        };

        var providers = VendorModelMapper.BuildProviders(settings);
        providers["openai"].DefaultFastModelId.Should().BeEmpty("无 models 时不设置 FastModel");
    }

    #endregion
}
