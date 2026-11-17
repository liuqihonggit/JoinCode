namespace Core.Tests.Configuration;

/// <summary>
/// 配置键唯一清单（TASK091）— <see cref="ConfigKeyRegistry"/> 取代了原来的四张表：
/// <c>ConfigKey</c> 枚举、<c>SupportedSettings</c>、<c>SettingSourceMap</c>、<c>SettingPathMap</c>。
/// <para>这里锁住「合并之后仍然自洽」的几条不变量 —— 它们正是四表分裂时各自失守的地方：
/// 同一个键的源与路径不能落在互相矛盾的位置，选项不能和枚举真实取值脱节，
/// 也不能再有「登记成可配置、但 settings.json 里没有这个字段」的幽灵键。</para>
/// </summary>
public sealed class ConfigKeyRegistryTests {
    [Fact]
    public void EveryEntry_KeyMatchesDictionaryKey() {
        foreach (var (key, setting) in ConfigKeyRegistry.All)
            setting.Key.Should().Be(key, "字典键与定义里的键名必须一致，否则按路径读写会错位");
    }

    [Fact]
    public void EveryEntry_HasTypeAndDescription() {
        foreach (var (key, setting) in ConfigKeyRegistry.All) {
            setting.Type.Should().BeOneOf("string", "boolean", $"{key} 的类型只能是 config 工具认识的两种");
            setting.Description.Should().NotBeNullOrWhiteSpace($"{key} 必须有面向用户的说明");
        }
    }

    [Fact]
    public void BooleanKeys_DeclareNoOptions() {
        foreach (var (key, setting) in ConfigKeyRegistry.All) {
            if (setting.Type != "boolean")
                continue;
            setting.Options.Should().BeNull($"{key} 是布尔键，选项由工具统一按 true/false 校验");
        }
    }

    [Fact]
    public void GlobalConfigKeys_DeclareNoNestedPath() {
        // global.json 是扁平键值文件：声明了嵌套路径就说明这个键其实住 settings.json
        foreach (var (key, setting) in ConfigKeyRegistry.All) {
            if (setting.Source != SettingSource.GlobalConfig)
                continue;
            setting.Path.Should().BeNull($"{key} 住在扁平结构里，不应声明嵌套路径");
        }
        // 反向也成立：嵌套路径只在 settings.json 里出现
        foreach (var (key, setting) in ConfigKeyRegistry.All) {
            if (setting.Path is null)
                continue;
            setting.Source.Should().Be(SettingSource.UserSettings, $"{key} 有嵌套路径，不可能在 global.json 里");
        }
    }

    [Fact]
    public void NestedPaths_StartFromFileRoot() {
        // 路径必须从文件根写全（TASK090 的教训：消费方不该再猜前缀）
        ConfigKeyRegistry.PathOf(ConfigKeys.Model).Should().Equal("vendor", "{profile}", "model");
        ConfigKeyRegistry.PathOf(ConfigKeys.PermissionsDefaultMode)
            .Should().Equal("current", "permissions", "defaultMode");
        ConfigKeyRegistry.PathOf(ConfigKeys.Theme).Should().BeNull("扁平键不声明路径");
    }

    [Fact]
    public void PermissionsDefaultMode_OptionsMatchPermissionModeEnum() {
        var expected = new[] {
            PermissionMode.Plan.ToValue(),
            PermissionMode.Auto.ToValue(),
            PermissionMode.Ask.ToValue(),
            PermissionMode.Bypass.ToValue(),
        };

        ConfigKeyRegistry.OptionsOf(ConfigKeys.PermissionsDefaultMode).Should().Equal(
            expected,
            "选项写的是权限模式枚举的存储键；枚举改名/改值时这里必须先红，而不是让用户写进去一个运行时认不出的值");
    }

    [Fact]
    public void PermissionsDefaultMode_DoesNotOfferUnattendedToTheModel() {
        ConfigKeyRegistry.OptionsOf(ConfigKeys.PermissionsDefaultMode)
            .Should().NotContain(PermissionMode.Unattended.ToValue(),
                "unattended 是无人值守长任务用的模式，只能由用户从命令行开启，不给模型可调用的 config 工具留入口");
    }

    [Fact]
    public void EnumOnlyKeys_AreNowVisibleAndWritable() {
        // profile/fastMode/effortLevel/outputStyle 曾经只在 ConfigKey 枚举里，
        // 于是 /config set 能写、config_set 报 Unknown setting —— 用户和模型各看见一半
        ConfigKeyRegistry.IsSupported(ConfigKeys.Profile).Should().BeTrue();
        ConfigKeyRegistry.IsSupported(ConfigKeys.FastMode).Should().BeTrue();
        ConfigKeyRegistry.IsSupported(ConfigKeys.EffortLevel).Should().BeTrue();
        ConfigKeyRegistry.IsSupported(ConfigKeys.OutputStyle).Should().BeTrue();
    }

    [Fact]
    public void PhantomApiKeyKey_IsGone() {
        // 枚举里登记过 apiKey，但 CurrentSettings 没有这个字段：写进去也读不回来
        ConfigKeyRegistry.IsSupported("apiKey").Should().BeFalse("没有落地字段的键不能出现在清单里");
    }

    [Fact]
    public void UnknownKey_FallsBackToUserSettingsAndFlatPath() {
        ConfigKeyRegistry.SourceOf("some.unknown.key").Should().Be(SettingSource.UserSettings);
        ConfigKeyRegistry.PathOf("some.unknown.key").Should().BeNull();
        ConfigKeyRegistry.Get("some.unknown.key").Should().BeNull();
        ConfigKeyRegistry.IsNested("some.unknown.key").Should().BeFalse();
    }

    [Fact]
    public void SourceAndPath_LookupsAgreeWithEntries() {
        foreach (var (key, setting) in ConfigKeyRegistry.All) {
            ConfigKeyRegistry.SourceOf(key).Should().Be(setting.Source);
            ConfigKeyRegistry.PathOf(key).Should().Equal(setting.Path);
            ConfigKeyRegistry.IsNested(key).Should().Be(setting.Path is not null);
        }
    }

    [Fact]
    public void OptionsOf_ReturnsStaticOptionsCopy() {
        ConfigKeyRegistry.OptionsOf(ConfigKeys.Theme).Should().Contain("dark").And.Contain("light");
        ConfigKeyRegistry.OptionsOf(ConfigKeys.DebugLog).Should().BeNull("布尔键没有可选值列表");
        ConfigKeyRegistry.OptionsOf("nope").Should().BeNull();
    }

    [Fact]
    public void ModelKey_ReadsAndWritesActiveVendorField() {
        var model = ConfigKeyRegistry.Get(ConfigKeys.Model);
        if (model is null) {
            Assert.Fail("model 必须在唯一清单里");
            return;
        }

        model.FormatOnRead.Should().NotBeNull("未选模型时 config_get model 要显示 default 而不是 null");
        model.ValidateOnWrite.Should().NotBeNull("模型 id 要拒绝空串与首尾空格");
        model.AppStateKey.Should().Be("mainLoopModel");
    }
}
