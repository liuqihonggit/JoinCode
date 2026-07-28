
namespace Guard.Tests.Configuration;

/// <summary>
/// SettingsMapper BDD 测试
/// 验证 SettingsJson → WorkflowConfig 映射 + 环境变量覆盖 + 合并策略
/// </summary>
public class SettingsMapperTests
{
    private static readonly string OpenAiModelId = ModelConfigLoader.GetDefaultModelId("openai");
    private static readonly string DefaultAnthropicModelId = ModelConfigLoader.GetDefaultModelId("anthropic");

    private readonly SettingsMapper _mapper = new(new ProviderDefinitionRegistry());

    #region 场景1: SettingsJson 映射到 WorkflowConfig

    [Fact]
    public void Given_包含model的SettingsJson_When_ToWorkflowConfig_Then_ProviderModelId正确()
    {
        // Given
        var settings = new SettingsJson { Model = DefaultAnthropicModelId };

        // When
        var config = _mapper.ToWorkflowConfig(settings);

        // Then
        config.Provider.ModelId.Should().Be(DefaultAnthropicModelId);
    }

    [Fact]
    public void Given_FastMode为true的SettingsJson_When_ToWorkflowConfig_Then_FastMode为true()
    {
        // Given
        var settings = new SettingsJson { FastMode = true };

        // When
        var config = _mapper.ToWorkflowConfig(settings);

        // Then
        config.FastMode.Should().BeTrue();
    }

    [Fact]
    public void Given_FastMode为null的SettingsJson_When_ToWorkflowConfig_Then_FastMode默认为false()
    {
        // Given
        var settings = new SettingsJson { FastMode = null };

        // When
        var config = _mapper.ToWorkflowConfig(settings);

        // Then
        config.FastMode.Should().BeFalse();
    }

    [Fact]
    public void Given_Worktree配置的SettingsJson_When_ToWorkflowConfig_Then_Worktree字段正确()
    {
        // Given
        var settings = new SettingsJson
        {
            Worktree = new WorktreeSettings
            {
                SymlinkDirectories = ["node_modules", ".venv"],
                SparsePaths = ["src/"],
            },
        };

        // When
        var config = _mapper.ToWorkflowConfig(settings);

        // Then
        config.Worktree.SymlinkDirectories.Should().Equal("node_modules", ".venv");
        config.Worktree.SparsePaths.Should().Equal("src/");
    }

    #endregion

    #region 场景2: 环境变量覆盖

    [Fact]
    public void Given_环境变量JCC_PROVIDER_When_ApplyEnvOverrides_Then_Provider被覆盖()
    {
        // Given
        var config = new WorkflowConfig();
        config.Provider.Provider = ProviderKind.DeepSeek.ToValue();
        Environment.SetEnvironmentVariable(JccEnvVar.Provider.ToValue(), "anthropic");
        try
        {
            // When
            _mapper.ApplyEnvOverrides(config);

            // Then
            config.Provider.Provider.Should().Be("anthropic");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Provider.ToValue(), null);
        }
    }

    [Fact]
    public void Given_环境变量JCC_API_KEY_When_ApplyEnvOverrides_Then_ApiKey不被覆盖()
    {
        // API Key 由 ConfigLoader.ResolveApiKeyAsync 统一解析，ApplyEnvOverrides 不再处理 API Key
        var config = new WorkflowConfig();
        config.Provider.ApiKey = "original-key";
        Environment.SetEnvironmentVariable(JccEnvVar.ApiKey.ToValue(), "env-api-key");
        try
        {
            // When
            _mapper.ApplyEnvOverrides(config);

            // Then: ApplyEnvOverrides 不再覆盖 API Key
            config.Provider.ApiKey.Should().Be("original-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.ApiKey.ToValue(), null);
        }
    }

    [Fact]
    public void Given_环境变量JCC_MODEL_ID_When_ApplyEnvOverrides_Then_ModelId被覆盖()
    {
        // Given
        var config = new WorkflowConfig();
        config.Provider.ModelId = OpenAiModelId;
        Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), "gpt-4o-mini");
        try
        {
            // When
            _mapper.ApplyEnvOverrides(config);

            // Then
            config.Provider.ModelId.Should().Be("gpt-4o-mini");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), null);
        }
    }

    [Fact]
    public void Given_无环境变量_When_ApplyEnvOverrides_Then_配置不变()
    {
        // Given
        var config = new WorkflowConfig();
        config.Provider.Provider = ProviderKind.DeepSeek.ToValue();
        var testModelId = "deepseek-v4-flash";
        config.Provider.ModelId = testModelId;
        // 清除可能存在的环境变量
        Environment.SetEnvironmentVariable(JccEnvVar.Provider.ToValue(), null);
        Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), null);
        Environment.SetEnvironmentVariable(JccEnvVar.ApiKey.ToValue(), null);
        Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), null);

        // When
        _mapper.ApplyEnvOverrides(config);

        // Then
        config.Provider.Provider.Should().Be(ProviderKind.DeepSeek.ToValue());
        config.Provider.ModelId.Should().Be(testModelId);
    }

    #endregion

    #region 场景3: Settings.env 注入

    [Fact]
    public void Given_SettingsEnv包含KEY_When_InjectEnvFromSettings_Then_环境变量被设置()
    {
        // Given
        var settings = new SettingsJson
        {
            Env = new Dictionary<string, string> { ["TEST_JCC_VAR"] = "test-value" },
        };
        Environment.SetEnvironmentVariable("TEST_JCC_VAR", null);
        try
        {
            // When
            SettingsMapper.InjectEnvFromSettings(settings);

            // Then
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
        // Given
        var settings = new SettingsJson
        {
            Env = new Dictionary<string, string> { ["TEST_JCC_EXISTING"] = "settings-value" },
        };
        Environment.SetEnvironmentVariable("TEST_JCC_EXISTING", "system-value");
        try
        {
            // When
            SettingsMapper.InjectEnvFromSettings(settings);

            // Then: 系统环境变量优先，不覆盖
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
    public void Given_两个SettingsJson_When_Merge_Then_高优先级覆盖简单值()
    {
        // Given
        var baseSettings = new SettingsJson { Model = OpenAiModelId, Language = "en-US" };
        var overrideSettings = new SettingsJson { Model = DefaultAnthropicModelId };

        // When
        var merged = SettingsMapper.Merge(baseSettings, overrideSettings);

        // Then
        merged.Model.Should().Be(DefaultAnthropicModelId); // 高优先级覆盖
        merged.Language.Should().Be("en-US"); // 低优先级保留
    }

    [Fact]
    public void Given_两个SettingsJson的Env_When_Merge_Then_字典合并高优先级覆盖()
    {
        // Given
        var baseSettings = new SettingsJson
        {
            Env = new Dictionary<string, string> { ["KEY1"] = "base1", ["KEY2"] = "base2" },
        };
        var overrideSettings = new SettingsJson
        {
            Env = new Dictionary<string, string> { ["KEY2"] = "override2", ["KEY3"] = "override3" },
        };

        // When
        var merged = SettingsMapper.Merge(baseSettings, overrideSettings);

        // Then
        merged.Env.Should().NotBeNull();
        merged.Env!["KEY1"].Should().Be("base1"); // 低优先级独有
        merged.Env["KEY2"].Should().Be("override2"); // 高优先级覆盖
        merged.Env["KEY3"].Should().Be("override3"); // 高优先级独有
    }

    [Fact]
    public void Given_两个SettingsJson的Permissions_When_Merge_Then_数组拼接去重()
    {
        // Given
        var baseSettings = new SettingsJson
        {
            Permissions = new PermissionsSettings { Allow = ["Bash(npm test)"], Deny = ["Bash(rm)"] },
        };
        var overrideSettings = new SettingsJson
        {
            Permissions = new PermissionsSettings { Allow = ["ReadFile"], DefaultMode = "autoAccept" },
        };

        // When
        var merged = SettingsMapper.Merge(baseSettings, overrideSettings);

        // Then
        merged.Permissions.Should().NotBeNull();
        merged.Permissions!.Allow.Should().Contain("Bash(npm test)");
        merged.Permissions.Allow.Should().Contain("ReadFile");
        merged.Permissions.Deny.Should().ContainSingle("Bash(rm)"); // 低优先级保留
        merged.Permissions.DefaultMode.Should().Be("autoAccept"); // 高优先级覆盖
    }

    [Fact]
    public void Given_base为null_When_Merge_Then_返回override()
    {
        // Given
        var overrideSettings = new SettingsJson { Model = OpenAiModelId };

        // When
        var merged = SettingsMapper.Merge(null, overrideSettings);

        // Then
        merged.Model.Should().Be(OpenAiModelId);
    }

    [Fact]
    public void Given_override为null_When_Merge_Then_返回base()
    {
        // Given
        var baseSettings = new SettingsJson { Model = OpenAiModelId };

        // When
        var merged = SettingsMapper.Merge(baseSettings, null);

        // Then
        merged.Model.Should().Be(OpenAiModelId);
    }

    [Fact]
    public void Given_两个null_When_Merge_Then_返回空SettingsJson()
    {
        // When
        var merged = SettingsMapper.Merge(null, null);

        // Then
        merged.Should().NotBeNull();
        merged.Model.Should().BeNull();
    }

    #endregion

    #region 场景5: 完整优先级链

    [Fact]
    public void Given_SettingsJson和环境变量_When_先映射再覆盖_Then_环境变量优先()
    {
        // Given
        var settings = new SettingsJson { Model = OpenAiModelId };
        Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), "gpt-4o-mini");
        try
        {
            // When: 先映射 SettingsJson，再应用环境变量覆盖
            var config = _mapper.ToWorkflowConfig(settings);
            _mapper.ApplyEnvOverrides(config);

            // Then: 环境变量优先
            config.Provider.ModelId.Should().Be("gpt-4o-mini");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), null);
        }
    }

    #endregion
}
