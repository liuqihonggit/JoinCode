
namespace Guard.Tests.Configuration;

/// <summary>
/// SettingsMapper BDD 测试 — 新结构 vendor+current
/// </summary>
public class SettingsMapperTests
{
    private static readonly IModelConfigLoader Loader = new ModelConfigLoader();
    private static readonly string OpenAiModelId = Loader.GetDefaultModelId("openai");
    private static readonly string DefaultAnthropicModelId = Loader.GetDefaultModelId("anthropic");

    private readonly SettingsMapper _mapper = new(new TestProviderDefinitionRegistry());

    #region 场景1: SettingsJson 映射到 WorkflowConfig

    [Fact]
    public void Given_vendor含anthropic预设_When_ToWorkflowConfig_Then_ProviderModelId正确()
    {
        var settings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic"] = new ProfileSettings { Provider = "anthropic", Model = DefaultAnthropicModelId },
            },
            Current = new CurrentSettings { Profile = "anthropic" },
        };

        var config = _mapper.ToWorkflowConfig(settings);
        config.Provider.ModelId.Should().Be(DefaultAnthropicModelId);
    }

    [Fact]
    public void Given_FastMode为true_When_ToWorkflowConfig_Then_FastMode为true()
    {
        var settings = new SettingsJson
        {
            Current = new CurrentSettings { FastMode = true },
        };

        var config = _mapper.ToWorkflowConfig(settings);
        config.FastMode.Should().BeTrue();
    }

    [Fact]
    public void Given_FastMode为null_When_ToWorkflowConfig_Then_FastMode默认为false()
    {
        var settings = new SettingsJson { Current = new CurrentSettings { FastMode = null } };

        var config = _mapper.ToWorkflowConfig(settings);
        config.FastMode.Should().BeFalse();
    }

    [Fact]
    public void Given_Worktree配置_When_ToWorkflowConfig_Then_Worktree字段正确()
    {
        var settings = new SettingsJson
        {
            Current = new CurrentSettings
            {
                Worktree = new WorktreeSettings
                {
                    SymlinkDirectories = ["node_modules", ".venv"],
                    SparsePaths = ["src/"],
                },
            },
        };

        var config = _mapper.ToWorkflowConfig(settings);
        config.Worktree.SymlinkDirectories.Should().Equal("node_modules", ".venv");
        config.Worktree.SparsePaths.Should().Equal("src/");
    }

    #endregion

    #region 场景2: 环境变量覆盖

    [Fact]
    public void Given_环境变量JCC_VENDOR_When_ApplyEnvOverrides_Then_Provider被覆盖()
    {
        var config = new WorkflowConfig();
        config.Provider.Vendor = VendorKind.DeepSeek.ToValue();
        Environment.SetEnvironmentVariable(JccEnvVar.Vendor.ToValue(), "anthropic");
        try
        {
            _mapper.ApplyEnvOverrides(config);
            config.Provider.Vendor.Should().Be("anthropic");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Vendor.ToValue(), null);
        }
    }

    [Fact]
    public void Given_环境变量JCC_VENDOR为anthropic_When_ApplyEnvOverrides_Then_Protocol同步为anthropic()
    {
        var config = new WorkflowConfig();
        config.Provider.Vendor = VendorKind.DeepSeek.ToValue();
        config.Provider.Protocol = ProtocolKind.OpenAiCompatible.ToValue();
        Environment.SetEnvironmentVariable(JccEnvVar.Vendor.ToValue(), "anthropic");
        try
        {
            _mapper.ApplyEnvOverrides(config);
            config.Provider.Vendor.Should().Be("anthropic");
            config.Provider.Protocol.Should().Be(ProtocolKind.Anthropic.ToValue());
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Vendor.ToValue(), null);
        }
    }

    [Fact]
    public void Given_环境变量JCC_PROTOCOL_When_ApplyEnvOverrides_Then_Protocol被覆盖()
    {
        var config = new WorkflowConfig();
        config.Provider.Protocol = ProtocolKind.OpenAiCompatible.ToValue();
        Environment.SetEnvironmentVariable(JccEnvVar.Protocol.ToValue(), "anthropic");
        try
        {
            _mapper.ApplyEnvOverrides(config);
            config.Provider.Protocol.Should().Be("anthropic");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Protocol.ToValue(), null);
        }
    }

    [Fact]
    public void Given_vendor含anthropic_When_ToWorkflowConfig_Then_Protocol同步为anthropic()
    {
        var settings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic"] = new ProfileSettings { Provider = "anthropic" },
            },
            Current = new CurrentSettings { Profile = "anthropic" },
        };

        var config = _mapper.ToWorkflowConfig(settings);
        config.Provider.Vendor.Should().Be("anthropic");
        config.Provider.Protocol.Should().Be(ProtocolKind.Anthropic.ToValue());
    }

    [Fact]
    public void Given_环境变量JCC_MODEL_ID_When_EnvOverrideApplier_Then_VendorProfileModel被覆盖()
    {
        var settings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new ProfileSettings { Provider = "openai", Model = OpenAiModelId },
            },
            Current = new CurrentSettings { Profile = "openai" },
        };
        Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), "gpt-4o-mini");
        try
        {
            var result = EnvOverrideApplier.Apply(settings);
            result.Vendor!["openai"].Model.Should().Be("gpt-4o-mini");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), null);
        }
    }

    [Fact]
    public void Given_无环境变量_When_ApplyEnvOverrides_Then_配置不变()
    {
        var config = new WorkflowConfig();
        config.Provider.Vendor = VendorKind.DeepSeek.ToValue();
        var testModelId = "deepseek-v4-flash";
        config.Provider.ModelId = testModelId;
        Environment.SetEnvironmentVariable(JccEnvVar.Vendor.ToValue(), null);
        Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), null);
        Environment.SetEnvironmentVariable(JccEnvVar.ApiKey.ToValue(), null);
        Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), null);

        _mapper.ApplyEnvOverrides(config);
        config.Provider.Vendor.Should().Be(VendorKind.DeepSeek.ToValue());
        config.Provider.ModelId.Should().Be(testModelId);
    }

    #endregion

    #region 场景3: Settings.env 注入

    [Fact]
    public void Given_CurrentEnv包含KEY_When_InjectEnvFromSettings_Then_环境变量被设置()
    {
        var settings = new SettingsJson
        {
            Current = new CurrentSettings
            {
                Env = new Dictionary<string, string> { ["TEST_JCC_VAR"] = "test-value" },
            },
        };
        Environment.SetEnvironmentVariable("TEST_JCC_VAR", null);
        try
        {
            SettingsMapper.InjectEnvFromSettings(settings);
            Environment.GetEnvironmentVariable("TEST_JCC_VAR").Should().Be("test-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_JCC_VAR", null);
        }
    }

    [Fact]
    public void Given_系统环境变量已存在_When_InjectEnvFromSettings_Then_不覆盖()
    {
        var settings = new SettingsJson
        {
            Current = new CurrentSettings
            {
                Env = new Dictionary<string, string> { ["TEST_JCC_EXISTING"] = "settings-value" },
            },
        };
        Environment.SetEnvironmentVariable("TEST_JCC_EXISTING", "system-value");
        try
        {
            SettingsMapper.InjectEnvFromSettings(settings);
            Environment.GetEnvironmentVariable("TEST_JCC_EXISTING").Should().Be("system-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_JCC_EXISTING", null);
        }
    }

    #endregion

    #region 场景4: SettingsJson 合并策略

    [Fact]
    public void Given_两个SettingsJson_When_Merge_Then_vendor字典合并current递归合并()
    {
        var baseSettings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new ProfileSettings { Provider = "openai", Model = OpenAiModelId },
            },
            Current = new CurrentSettings { Language = "en-US" },
        };
        var overrideSettings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic"] = new ProfileSettings { Provider = "anthropic", Model = DefaultAnthropicModelId },
            },
            Current = new CurrentSettings { Profile = "anthropic" },
        };

        var merged = SettingsMapper.Merge(baseSettings, overrideSettings);
        merged.Vendor.Should().ContainKey("openai");
        merged.Vendor.Should().ContainKey("anthropic");
        merged.Current!.Profile.Should().Be("anthropic");
        merged.Current.Language.Should().Be("en-US");
    }

    [Fact]
    public void Given_base为null_When_Merge_Then_返回override()
    {
        var overrideSettings = new SettingsJson
        {
            Current = new CurrentSettings { Profile = "openai" },
        };

        var merged = SettingsMapper.Merge(null, overrideSettings);
        merged.Current!.Profile.Should().Be("openai");
    }

    [Fact]
    public void Given_override为null_When_Merge_Then_返回base()
    {
        var baseSettings = new SettingsJson
        {
            Current = new CurrentSettings { Profile = "openai" },
        };

        var merged = SettingsMapper.Merge(baseSettings, null);
        merged.Current!.Profile.Should().Be("openai");
    }

    [Fact]
    public void Given_两个null_When_Merge_Then_返回空SettingsJson()
    {
        var merged = SettingsMapper.Merge(null, null);
        merged.Should().NotBeNull();
        merged.Current.Should().BeNull();
    }

    #endregion

    #region 场景5: SandboxSettings 映射

    [Fact]
    public void Given_SandboxRestrictNetwork_When_ToWorkflowConfig_Then_AllowNetworkAccess取反()
    {
        var settings = new SettingsJson
        {
            Current = new CurrentSettings
            {
                Sandbox = new SandboxSettings { RestrictNetwork = true },
            },
        };

        var config = _mapper.ToWorkflowConfig(settings);
        config.CodeExecution.AllowNetworkAccess.Should().BeFalse();
    }

    [Fact]
    public void Given_SandboxMemoryLimitMb_When_ToWorkflowConfig_Then_MaxMemoryMB映射()
    {
        var settings = new SettingsJson
        {
            Current = new CurrentSettings
            {
                Sandbox = new SandboxSettings { MemoryLimitMb = 512 },
            },
        };

        var config = _mapper.ToWorkflowConfig(settings);
        config.CodeExecution.MaxMemoryMB.Should().Be(512);
    }

    #endregion

    /// <summary>
    /// 测试专用 Provider 注册表 — 不依赖全局 settings.json，注册所有测试需要的 Provider
    /// </summary>
    private sealed class TestProviderDefinitionRegistry : IProviderDefinitionRegistry
    {
        private readonly Dictionary<string, IProviderDefinition> _definitions;

        public TestProviderDefinitionRegistry()
        {
            var loader = new ModelConfigLoader();
            _definitions = new Dictionary<string, IProviderDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new OpenAiCompatibleProviderDefinition(loader, "openai", "OPENAI_API_KEY"),
                ["anthropic"] = new AnthropicCompatibleProviderDefinition(loader, "anthropic", "ANTHROPIC_API_KEY"),
                ["deepseek"] = new OpenAiCompatibleProviderDefinition(loader, "deepseek", "DEEPSEEK_API_KEY"),
                ["azure"] = new AzureProviderDefinition(loader),
            };
        }

        public IProviderDefinition? TryGet(string providerName) => _definitions.GetValueOrDefault(providerName);
        public IReadOnlyCollection<string> RegisteredProviders => _definitions.Keys;
    }
}
