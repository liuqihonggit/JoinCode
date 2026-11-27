namespace Hub.Tests;

/// <summary>Hub 启动配置规范化测试。</summary>
public sealed class ProgramTests {
    /// <summary>空对象配置缺少 vendor 时必须回退到嵌入的默认供应商，而不是空引用崩溃。</summary>
    [Fact]
    public void ResolveSettings_EmptyObject_LoadsDefaultVendors() {
        var settings = JsonSerializer.Deserialize("{}", ConfigJsonContext.Default.SettingsJson);

        var resolved = JoinCode.Hub.Program.ResolveSettings(settings);

        resolved.Vendor.Should().NotBeNull();
        resolved.Vendor.Should().ContainKey("sensenova");
        resolved.Current.Should().NotBeNull();
        resolved.Current?.Profile.Should().Be("sensenova");
    }

    /// <summary>显式空 vendor 配置也必须加载默认供应商。</summary>
    [Fact]
    public void ResolveSettings_EmptyVendor_LoadsDefaultVendors() {
        var settings = JsonSerializer.Deserialize("""{"vendor":{}}""", ConfigJsonContext.Default.SettingsJson);

        var resolved = JoinCode.Hub.Program.ResolveSettings(settings);

        resolved.Vendor.Should().ContainKey("openai");
    }

    /// <summary>用户已配置供应商时必须原样保留，不能被默认配置覆盖。</summary>
    [Fact]
    public void ResolveSettings_ConfiguredVendor_PreservesUserSettings() {
        var settings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["custom"] = new ProfileSettings {
                    Provider = "custom",
                    Endpoint = "http://localhost:9999/v1",
                    Model = "custom-model"
                }
            },
            Current = new CurrentSettings { Profile = "custom" }
        };

        var resolved = JoinCode.Hub.Program.ResolveSettings(settings);

        resolved.Should().BeSameAs(settings);
        resolved.Vendor.Should().ContainSingle();
        resolved.Current?.Profile.Should().Be("custom");
    }
}
