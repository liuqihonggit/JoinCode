#pragma warning disable JCC3010, JCC3011, JCC3012
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Guard.Tests.Configuration;

/// <summary>
/// SettingsLoader BDD 测试
/// 验证 5 层配置来源优先级 + 合并策略
/// 与 ProjectRulesLoaderTests 共享 AppDataConstants 全局状态,需串行执行避免相互污染
/// </summary>
[Collection("AppDataConstantsCollection")]
public class SettingsLoaderTests : IDisposable {
    private static readonly IModelConfigLoader Loader = new ModelConfigLoader();
    private static readonly string DefaultOpenAiModelId = Loader.GetDefaultModelId("openai");
    private static readonly string DefaultAnthropicModelId = Loader.GetDefaultModelId("anthropic");

    private readonly string _tempDir;
    private readonly string _projectAppDataDir;
    private readonly string _userAppDataDir;
    private readonly string? _originalAppDataFolder;
    private readonly string? _originalSettingsFileName;
    private readonly EnvVarScope _envScope;
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private bool _disposed;

    public SettingsLoaderTests() {
        _tempDir = $"/test/jcc_settings_test_{Guid.NewGuid().ToString("N")[..8]}";
        _fs.CreateDirectory(_tempDir);

        // 项目设置目录: _tempDir/.jcc/
        _projectAppDataDir = Path.Combine(_tempDir, AppDataConstants.AppDataFolder);
        _fs.CreateDirectory(_projectAppDataDir);

        // 用户设置隔离目录（绝对路径）
        _userAppDataDir = $"/test/jcc_settings_user_{Guid.NewGuid().ToString("N")[..8]}";
        _fs.CreateDirectory(_userAppDataDir);

        // 先清除环境变量，确保读取到 backing field 的真实值
        _envScope = EnvVarScope.Set(JccEnvVarEnumConstants.AppDataFolder, null);

        _originalAppDataFolder = AppDataConstants.AppDataFolder;
        _originalSettingsFileName = AppDataConstants.SettingsFileName;

        // 用户设置: 通过环境变量覆盖为临时绝对路径，GetUserSettingsPath 的 IsPathRooted 分支直接使用
        _envScope.Add(JccEnvVarEnumConstants.AppDataFolder, _userAppDataDir);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment(); // 刷新 Paths 实例
        AppDataConstants.SettingsFileName = "settings.json";
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        // 还原全局 AppDataConstants,避免污染后续测试
        _envScope.DisposeSafe();
        AppDataConstants.Paths = AppDataPaths.FromEnvironment(); // 恢复 Paths 实例
    }

    #region 场景1: 多源加载 + 优先级合并

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_用户设置和项目设置_When_加载全部_Then_项目设置覆盖用户设置() {
        // Given: 用户设置 profile 指向 openai, 项目设置 profile 指向 anthropic
        var userSettings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["openai"] = new ProfileSettings { Model = DefaultOpenAiModelId },
            },
            Current = new CurrentSettings { Profile = "openai" },
        };
        var projectSettings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["anthropic"] = new ProfileSettings { Model = DefaultAnthropicModelId },
            },
            Current = new CurrentSettings { Profile = "anthropic" },
        };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When: 加载全部来源
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 项目设置覆盖用户设置 — profile 指向 anthropic
        result.Current.Should().NotBeNull();
        result.Current!.Profile.Should().Be("anthropic");
        var activeProfile = result.GetActiveProfile();
        activeProfile.Should().NotBeNull();
        activeProfile!.Model.Should().Be(DefaultAnthropicModelId);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_用户设置和本地设置_When_加载全部_Then_本地设置覆盖用户设置() {
        // Given: 用户设置 language=en-US, 本地设置 language=zh-CN
        var userSettings = new SettingsJson { Current = new CurrentSettings { Language = "en-US" } };
        var localSettings = new SettingsJson { Current = new CurrentSettings { Language = "zh-CN" } };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteLocalSettingsAsync(localSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then
        result.Current.Should().NotBeNull();
        result.Current!.Language.Should().Be("zh-CN");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_用户设置有env_项目设置也有env_When_加载全部_Then_env字典合并() {
        // Given
        var userSettings = new SettingsJson { Current = new CurrentSettings { Env = new Dictionary<string, string> { ["KEY1"] = "user1", ["KEY2"] = "user2" } } };
        var projectSettings = new SettingsJson { Current = new CurrentSettings { Env = new Dictionary<string, string> { ["KEY2"] = "project2", ["KEY3"] = "project3" } } };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 字典合并，高优先级覆盖
        result.Current.Should().NotBeNull();
        result.Current!.Env.Should().NotBeNull();
        result.Current.Env!["KEY1"].Should().Be("user1");
        result.Current.Env["KEY2"].Should().Be("project2"); // 项目覆盖用户
        result.Current.Env["KEY3"].Should().Be("project3");
    }

    #endregion

    #region 场景2: 无配置文件时返回默认值

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_无任何配置文件_When_加载全部_Then_返回默认骨架() {
        // Given: 无配置文件

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then
        result.Should().NotBeNull();
        result.Vendor.Should().NotBeNull();
        result.Vendor!.Should().ContainKey("sensenova");
        result.Vendor!.Should().ContainKey("openai");
        result.Vendor!.Should().ContainKey("anthropic");
        result.Vendor!.Should().ContainKey("deepseek");
        result.Vendor!.Should().ContainKey("agnes");
        result.AutoFetchModels.Should().BeTrue();
    }

    #endregion

    #region 场景3: 损坏文件不影响其他来源

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_用户设置损坏_项目设置正常_When_加载全部_Then_项目设置仍可用() {
        // Given: 用户设置损坏
        var userPath = GetUserSettingsPath();
        var dir = Path.GetDirectoryName(userPath);
        if (!string.IsNullOrEmpty(dir)) _fs.CreateDirectory(dir);
        await _fs.WriteAllTextAsync(userPath, "{ invalid json }").ConfigureAwait(true);
        var projectSettings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["anthropic"] = new ProfileSettings { Model = DefaultAnthropicModelId },
            },
            Current = new CurrentSettings { Profile = "anthropic" },
        };
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 项目设置仍可用
        result.Current.Should().NotBeNull();
        result.Current!.Profile.Should().Be("anthropic");
        var activeProfile = result.GetActiveProfile();
        activeProfile.Should().NotBeNull();
        activeProfile!.Model.Should().Be(DefaultAnthropicModelId);
    }

    #endregion

    #region 场景4: 保存到指定来源

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_保存到UserSettings_When_重新加载_Then_数据一致() {
        // Given
        var settings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["openai"] = new ProfileSettings { Model = DefaultOpenAiModelId },
            },
            Current = new CurrentSettings { Profile = "openai", FastMode = true },
        };

        // When: 保存到用户设置
        await SettingsLoader.SaveSettingsAsync(_fs, SettingSource.UserSettings, settings, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        // Then: 重新加载能读到
        var loaded = await SettingsLoader.LoadUserSettingsAsync(_fs).ConfigureAwait(true);
        loaded.Should().NotBeNull();
        loaded!.Current.Should().NotBeNull();
        loaded.Current!.Profile.Should().Be("openai");
        loaded.Current.FastMode.Should().BeTrue();
        var activeProfile = loaded.GetActiveProfile();
        activeProfile.Should().NotBeNull();
        activeProfile!.Model.Should().Be(DefaultOpenAiModelId);
    }

    #endregion

    #region 场景5: 权限数组拼接去重

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_用户权限和项目权限_When_加载全部_Then_权限数组拼接去重() {
        // Given
        var userSettings = new SettingsJson {
            Current = new CurrentSettings {
                Permissions = new PermissionsSettings { Allow = ["Bash(npm test)"], Deny = ["Bash(rm)"] },
            },
        };
        var projectSettings = new SettingsJson {
            Current = new CurrentSettings {
                Permissions = new PermissionsSettings { Allow = ["ReadFile"], DefaultMode = "autoAccept" },
            },
        };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 数组拼接
        result.Current.Should().NotBeNull();
        result.Current!.Permissions.Should().NotBeNull();
        result.Current.Permissions!.Allow.Should().Contain("Bash(npm test)");
        result.Current.Permissions.Allow.Should().Contain("ReadFile");
        result.Current.Permissions.DefaultMode.Should().Be("autoAccept");
    }

    #endregion

    #region 场景6: 空文件自动创建默认骨架

    /// <summary>
    /// 用户 settings.json 存在但为空(0字节) → 自动写入默认骨架并返回
    /// 骨架含所有5个供应商的预设入口点,models 数组留空由 AutoFetchModels 填充
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Given_用户设置文件为空_When_加载用户设置_Then_返回默认骨架并写入磁盘() {
        // Given: 用户设置文件存在但为空(0字节)
        var userPath = GetUserSettingsPath();
        var dir = Path.GetDirectoryName(userPath);
        if (!string.IsNullOrEmpty(dir)) _fs.CreateDirectory(dir);
        await _fs.WriteAllTextAsync(userPath, "").ConfigureAwait(true);
        _fs.GetFileLength(userPath).Should().Be(0);

        // When
        var result = await SettingsLoader.LoadUserSettingsAsync(_fs).ConfigureAwait(true);

        // Then
        result.Should().NotBeNull();
        result!.Vendor.Should().NotBeNull();
        result.Vendor!.Should().ContainKey("sensenova");
        result.Vendor!.Should().ContainKey("openai");
        result.Vendor!.Should().ContainKey("anthropic");
        result.Vendor!.Should().ContainKey("deepseek");
        result.Vendor!.Should().ContainKey("agnes");
        result.AutoFetchModels.Should().BeTrue();

        // 验证骨架已写入磁盘
        var written = await _fs.ReadAllTextAsync(userPath, CancellationToken.None).ConfigureAwait(true);
        written.Should().NotBeNullOrWhiteSpace();
        var parsed = JsonSerializer.Deserialize<SettingsJson>(written, ConfigJsonContext.Default.SettingsJson);
        parsed.Should().NotBeNull();
        parsed!.Vendor.Should().NotBeNull();
        parsed.Vendor!.Should().ContainKey("sensenova");
    }

    #endregion

    #region 场景7: 默认骨架含 Actor 配置

    /// <summary>
    /// BuildDefaultSettingsJson 生成的骨架应包含 current.actor 节点,
    /// 且能反序列化为 ActorSettings,值与默认预设一致(ADR 0074)
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Given_默认骨架_When_解析_Then_包含Actor配置且值正确() {
        // Given
        var json = SettingsLoader.BuildDefaultSettingsJson();

        // When
        var parsed = JsonSerializer.Deserialize<SettingsJson>(json, ConfigJsonContext.Default.SettingsJson);

        // Then
        parsed.Should().NotBeNull();
        parsed!.Current.Should().NotBeNull();
        parsed.Current!.Actor.Should().NotBeNull();

        var actor = parsed.Current.Actor!;
        actor.BuildQueue.Mode.Should().Be("serial");
        actor.BuildQueue.WorkerCount.Should().Be(2);

        actor.Backpressure.CodingAgentTask.Capacity.Should().Be(2000);
        actor.Backpressure.CodingAgentTask.SendTimeoutSeconds.Should().Be(30);
        actor.Backpressure.LlmGateway.Capacity.Should().Be(200);
        actor.Backpressure.LlmGateway.SendTimeoutSeconds.Should().Be(60);
        actor.Backpressure.Router.Capacity.Should().Be(1000);
        actor.Backpressure.Router.SendTimeoutSeconds.Should().Be(10);
        actor.Backpressure.Build.Capacity.Should().Be(100);
        actor.Backpressure.Build.SendTimeoutSeconds.Should().Be(60);

        // 验证合法性通过
        Action validate = () => actor.Validate();
        validate.Should().NotThrow();
    }

    #endregion

    #region 场景8: 默认骨架含 SearchProviders 配置

    /// <summary>
    /// BuildDefaultSettingsJson 生成的骨架应包含 searchProviders 节点,
    /// 含 6 个搜索供应商(tavily/serper/brave/searxng/anthropic/subagent),优先级与 enabled 正确(TASK054)
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Given_默认骨架_When_解析_Then_包含SearchProviders且6供应商配置正确() {
        var json = SettingsLoader.BuildDefaultSettingsJson();
        var parsed = JsonSerializer.Deserialize<SettingsJson>(json, ConfigJsonContext.Default.SettingsJson);

        parsed.Should().NotBeNull();
        parsed!.SearchProviders.Should().NotBeNull();
        parsed.SearchProviders.Count.Should().Be(6);

        parsed.SearchProviders.Should().ContainKey("tavily");
        parsed.SearchProviders["tavily"].ApiKeyEnvVar.Should().Be("TAVILY_API_KEY");
        parsed.SearchProviders["tavily"].Priority.Should().Be(100);
        parsed.SearchProviders["tavily"].Enabled.Should().BeTrue();

        parsed.SearchProviders.Should().ContainKey("serper");
        parsed.SearchProviders["serper"].ApiKeyEnvVar.Should().Be("SERPER_API_KEY");
        parsed.SearchProviders["serper"].Priority.Should().Be(200);

        parsed.SearchProviders.Should().ContainKey("brave");
        parsed.SearchProviders["brave"].ApiKeyEnvVar.Should().Be("BRAVE_API_KEY");
        parsed.SearchProviders["brave"].Priority.Should().Be(300);

        parsed.SearchProviders.Should().ContainKey("searxng");
        parsed.SearchProviders["searxng"].EndpointEnvVar.Should().Be("SEARXNG_URL");
        parsed.SearchProviders["searxng"].Priority.Should().Be(400);

        parsed.SearchProviders.Should().ContainKey("anthropic");
        parsed.SearchProviders["anthropic"].Priority.Should().Be(500);

        parsed.SearchProviders.Should().ContainKey("subagent");
        parsed.SearchProviders["subagent"].Priority.Should().Be(999);
    }

    /// <summary>
    /// SettingsJson.Merge 应正确合并 searchProviders 字典(高优先级覆盖同键)
    /// </summary>
    [Fact]
    public void Given_两层settings_When_合并_Then_searchProviders高优先级覆盖同键() {
        var baseSettings = new SettingsJson {
            SearchProviders = new Dictionary<string, SearchProviderProfileSettings> {
                ["tavily"] = new() { ApiKeyEnvVar = "BASE_KEY", Priority = 100, Enabled = true },
                ["serper"] = new() { ApiKeyEnvVar = "SERPER_API_KEY", Priority = 200, Enabled = true },
            }
        };
        var overrideSettings = new SettingsJson {
            SearchProviders = new Dictionary<string, SearchProviderProfileSettings> {
                ["tavily"] = new() { ApiKeyEnvVar = "OVERRIDE_KEY", Priority = 150, Enabled = false },
                ["brave"] = new() { ApiKeyEnvVar = "BRAVE_API_KEY", Priority = 300, Enabled = true },
            }
        };

        var merged = SettingsJson.Merge(baseSettings, overrideSettings);

        merged.SearchProviders.Count.Should().Be(3);
        merged.SearchProviders["tavily"].ApiKeyEnvVar.Should().Be("OVERRIDE_KEY");
        merged.SearchProviders["tavily"].Priority.Should().Be(150);
        merged.SearchProviders["tavily"].Enabled.Should().BeFalse();
        merged.SearchProviders["serper"].Priority.Should().Be(200);
        merged.SearchProviders["brave"].Priority.Should().Be(300);
    }

    #endregion

    #region 辅助方法

    private string GetUserSettingsPath() => SettingsLoader.GetUserSettingsPath();

    private async Task WriteSettingsAsync(string path, SettingsJson settings) {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) {
            // Windows 并行测试时偶发 UnauthorizedAccessException(目录刚被前一个测试清理),
            // 增加重试机制,确保临时目录稳定创建
            const int maxRetries = 3;
            for (var i = 0; i < maxRetries; i++) {
                try {
                    if (!_fs.DirectoryExists(dir))
                        _fs.CreateDirectory(dir);
                    break;
                } catch (UnauthorizedAccessException) when (i < maxRetries - 1) {
                    await Task.Delay(50).ConfigureAwait(true);
                }
            }
        }
        var json = JsonSerializer.Serialize(settings, ConfigIndentedJsonContext.Default.SettingsJson);
        await _fs.WriteAllText(path, json);
    }

    private async Task WriteProjectSettingsAsync(SettingsJson settings) {
        var path = Path.Combine(_projectAppDataDir, "settings.json");
        await WriteSettingsAsync(path, settings).ConfigureAwait(true);
    }

    private async Task WriteLocalSettingsAsync(SettingsJson settings) {
        var path = Path.Combine(_projectAppDataDir, "settings.local.json");
        await WriteSettingsAsync(path, settings).ConfigureAwait(true);
    }

    #endregion

    #region P6: providers_llm/ 子文件夹物理拆分

    [Fact]
    public async Task P6_LoadVendorSubConfigs_从子文件夹加载供应商配置和模型() {
        var vendorsDir = Path.Combine(_userAppDataDir, "providers_llm", "openai");
        _fs.CreateDirectory(vendorsDir);

        var configJson = """{"provider":"openai","protocol":"openai-compatible","model":"gpt-4o","endpoint":"https://api.openai.com/v1","apiKeyEnvVar":"OPENAI_API_KEY","modelsEndpoint":"models"}""";
        await _fs.WriteAllText(Path.Combine(vendorsDir, "config.json"), configJson);

        var modelsJson = """{"models":[{"id":"gpt-4o","displayName":"GPT-4o","contextWindow":128000}]}""";
        await _fs.WriteAllText(Path.Combine(vendorsDir, "models.json"), modelsJson);

        var result = await SettingsLoader.LoadVendorSubConfigsAsync(_fs, _userAppDataDir, CancellationToken.None);

        result.Should().ContainKey("openai");
        var openai = result["openai"];
        openai.Provider.Should().Be("openai");
        openai.Endpoint.Should().Be("https://api.openai.com/v1");
        openai.Models.Should().HaveCount(1);
        openai.Models[0].Id.Should().Be("gpt-4o");
        openai.Models[0].ContextWindow.Should().Be(128000);
    }

    [Fact]
    public async Task P6_LoadVendorSubConfigs_无vendors目录_返回空字典() {
        var result = await SettingsLoader.LoadVendorSubConfigsAsync(_fs, _userAppDataDir, CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task P6_LoadVendorSubConfigs_只有config无models_Models为空() {
        var vendorsDir = Path.Combine(_userAppDataDir, "providers_llm", "anthropic");
        _fs.CreateDirectory(vendorsDir);

        var configJson = """{"provider":"anthropic","protocol":"anthropic","model":"claude-sonnet-4-5"}""";
        await _fs.WriteAllText(Path.Combine(vendorsDir, "config.json"), configJson);

        var result = await SettingsLoader.LoadVendorSubConfigsAsync(_fs, _userAppDataDir, CancellationToken.None);

        result.Should().ContainKey("anthropic");
        result["anthropic"].Models.Should().BeEmpty();
    }

    [Fact]
    public async Task P6b_LoadUserSettingsAsync_子文件覆盖settingsJson内联() {
        // Given: settings.json 含 vendor.openai 内联 + providers_llm/openai/config.json 子文件(不同 endpoint)
        var userSettings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["openai"] = new ProfileSettings { Provider = "openai", Endpoint = "https://inline-endpoint" },
            },
        };
        await WriteSettingsAsync(GetUserSettingsPath(), userSettings);

        var vendorsDir = Path.Combine(_userAppDataDir, "providers_llm", "openai");
        _fs.CreateDirectory(vendorsDir);
        var subConfigJson = """{"provider":"openai","endpoint":"https://subfile-endpoint","apiKeyEnvVar":"OPENAI_API_KEY"}""";
        await _fs.WriteAllText(Path.Combine(vendorsDir, "config.json"), subConfigJson);

        // When: 加载用户设置
        var result = await SettingsLoader.LoadUserSettingsAsync(_fs, CancellationToken.None);

        // Then: 子文件 endpoint 覆盖内联
        result.Should().NotBeNull();
        result?.Vendor.Should().ContainKey("openai");
        result?.Vendor["openai"].Endpoint.Should().Be("https://subfile-endpoint");
    }

    #endregion

    #region P7: current/ 子文件物理拆分

    [Fact]
    public async Task P7_LoadCurrentSubConfigs_多子文件合并成CurrentSettings() {
        var currentDir = Path.Combine(_userAppDataDir, "current");
        _fs.CreateDirectory(currentDir);

        var uiJson = """{"theme":"dark","editorMode":"vim","language":"zh"}""";
        await _fs.WriteAllText(Path.Combine(currentDir, "ui.json"), uiJson);

        var inferenceJson = """{"effortLevel":"high","fastMode":true}""";
        await _fs.WriteAllText(Path.Combine(currentDir, "inference.json"), inferenceJson);

        var result = await SettingsLoader.LoadCurrentSubConfigsAsync(_fs, _userAppDataDir, CancellationToken.None);

        result.Should().NotBeNull();
        result?.Theme.Should().Be("dark");
        result?.EditorMode.Should().Be("vim");
        result?.Language.Should().Be("zh");
        result?.EffortLevel.Should().Be("high");
        result?.FastMode.Should().BeTrue();
    }

    [Fact]
    public async Task P7_LoadCurrentSubConfigs_无current目录_返回null() {
        var result = await SettingsLoader.LoadCurrentSubConfigsAsync(_fs, _userAppDataDir, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task P7b_LoadUserSettingsAsync_current子文件覆盖settingsJson内联() {
        var userSettings = new SettingsJson {
            Current = new CurrentSettings { Theme = "light", Profile = "openai" },
        };
        await WriteSettingsAsync(GetUserSettingsPath(), userSettings);

        var currentDir = Path.Combine(_userAppDataDir, "current");
        _fs.CreateDirectory(currentDir);
        var uiJson = """{"theme":"dark"}""";
        await _fs.WriteAllText(Path.Combine(currentDir, "ui.json"), uiJson);

        var result = await SettingsLoader.LoadUserSettingsAsync(_fs, CancellationToken.None);

        result.Should().NotBeNull();
        result?.Current?.Theme.Should().Be("dark");
        result?.Current?.Profile.Should().Be("openai");
    }

    #endregion

    #region P8: BuildDefaultConfigTreeAsync 树状骨架生成

    [Fact]
    public async Task P8_BuildDefaultConfigTreeAsync_生成current目录10子文件() {
        var baseDir = $"/test/p8_current_{Guid.NewGuid():N}";
        _fs.CreateDirectory(baseDir);

        await SettingsLoader.BuildDefaultConfigTreeAsync(_fs, baseDir, CancellationToken.None);

        var currentDir = Path.Combine(baseDir, "current");
        _fs.DirectoryExists(currentDir).Should().BeTrue("应生成 current/ 目录");
        var expectedFiles = new[] {
            "ui.json", "inference.json", "behavior.json", "security.json",
            "mcp.json", "plugins.json", "subagent.json", "tools.json",
            "worktree.json", "runtime.json"
        };
        foreach (var fileName in expectedFiles) {
            _fs.FileExists(Path.Combine(currentDir, fileName)).Should().BeTrue(
                $"current/{fileName} 应存在");
        }
    }

    [Fact]
    public async Task P8_BuildDefaultConfigTreeAsync_生成vendors目录7供应商() {
        var baseDir = $"/test/p8_vendors_{Guid.NewGuid():N}";
        _fs.CreateDirectory(baseDir);

        await SettingsLoader.BuildDefaultConfigTreeAsync(_fs, baseDir, CancellationToken.None);

        var vendorsDir = Path.Combine(baseDir, "providers_llm");
        _fs.DirectoryExists(vendorsDir).Should().BeTrue("应生成 providers_llm/ 目录");
        var expectedVendors = new[] {
            "sensenova", "openai", "anthropic", "deepseek",
            "deepseek-anthropic", "agnes", "zhipu"
        };
        foreach (var name in expectedVendors) {
            var vendorDir = Path.Combine(vendorsDir, name);
            _fs.DirectoryExists(vendorDir).Should().BeTrue($"providers_llm/{name}/ 应存在");
            _fs.FileExists(Path.Combine(vendorDir, "config.json")).Should().BeTrue($"providers_llm/{name}/config.json 应存在");
            _fs.FileExists(Path.Combine(vendorDir, "models.json")).Should().BeTrue($"providers_llm/{name}/models.json 应存在");
        }
    }

    [Fact]
    public async Task P8_BuildDefaultConfigTreeAsync_生成searchProviders目录6供应商() {
        var baseDir = $"/test/p8_search_{Guid.NewGuid():N}";
        _fs.CreateDirectory(baseDir);

        await SettingsLoader.BuildDefaultConfigTreeAsync(_fs, baseDir, CancellationToken.None);

        var searchDir = Path.Combine(baseDir, "providers_search");
        _fs.DirectoryExists(searchDir).Should().BeTrue("应生成 providers_search/ 目录");
        var expectedProviders = new[] { "tavily", "serper", "brave", "searxng", "anthropic", "subagent" };
        foreach (var name in expectedProviders) {
            var providerDir = Path.Combine(searchDir, name);
            _fs.DirectoryExists(providerDir).Should().BeTrue($"providers_search/{name}/ 应存在");
            _fs.FileExists(Path.Combine(providerDir, "config.json")).Should().BeTrue($"providers_search/{name}/config.json 应存在");
            _fs.FileExists(Path.Combine(providerDir, "models.json")).Should().BeTrue($"providers_search/{name}/models.json 应存在");
        }
    }

    [Fact]
    public async Task P8_BuildDefaultConfigTreeAsync_settingsJson极简不含Vendor内联() {
        var baseDir = $"/test/p8_minimal_{Guid.NewGuid():N}";
        _fs.CreateDirectory(baseDir);

        await SettingsLoader.BuildDefaultConfigTreeAsync(_fs, baseDir, CancellationToken.None);

        var settingsPath = Path.Combine(baseDir, "settings.json");
        _fs.FileExists(settingsPath).Should().BeTrue("settings.json 应存在");
        var settingsContent = await _fs.ReadAllText(settingsPath);

        settingsContent.Should().Contain("autoFetchModels", "极简 settings.json 应含 autoFetchModels 全局开关");
        settingsContent.Should().Contain("profile", "极简 settings.json 应含 current.profile 指针");
        settingsContent.Should().NotContain("\"vendor\":", "极简 settings.json 不应内联 vendor 节点（已拆到 providers_llm/ 子文件夹）");
        settingsContent.Should().NotContain("\"searchProviders\":", "极简 settings.json 不应内联 searchProviders 节点（已拆到 providers_search/ 子文件夹）");
    }

    [Fact]
    public async Task P8_BuildDefaultConfigTreeAsync_vendors子文件含完整Profile() {
        var baseDir = $"/test/p8_vendor_content_{Guid.NewGuid():N}";
        _fs.CreateDirectory(baseDir);

        await SettingsLoader.BuildDefaultConfigTreeAsync(_fs, baseDir, CancellationToken.None);

        var openaiConfigPath = Path.Combine(baseDir, "providers_llm", "openai", "config.json");
        var configContent = await _fs.ReadAllText(openaiConfigPath);
        configContent.Should().Contain("https://api.openai.com/v1", "openai config.json 应含 endpoint");
        configContent.Should().Contain("openai-compatible", "openai config.json 应含 protocol");
        configContent.Should().Contain("apiKeyEnvVar", "openai config.json 应含 apiKeyEnvVar 字段");
        configContent.Should().NotContain("\"models\":", "config.json 不应含 models 字段（已拆到 models.json）");
    }

    [Fact]
    public async Task P8_BuildDefaultConfigTreeAsync_加载侧合并后等价于单文件骨架() {
        var baseDir = $"/test/p8_roundtrip_{Guid.NewGuid():N}";
        _fs.CreateDirectory(baseDir);

        await SettingsLoader.BuildDefaultConfigTreeAsync(_fs, baseDir, CancellationToken.None);

        var treeSettings = await SettingsLoader.LoadUserSettingsFromDirAsync(_fs, baseDir, CancellationToken.None);
        treeSettings.Should().NotBeNull();
        var settings = treeSettings ?? throw new InvalidOperationException("unreachable");
        settings.Vendor.Should().ContainKey("openai");
        settings.Vendor["openai"].Endpoint.Should().Be("https://api.openai.com/v1");
        settings.Vendor["openai"].Protocol.Should().Be("openai-compatible");
        settings.Vendor.Should().ContainKey("deepseek-anthropic");
        settings.SearchProviders.Should().ContainKey("tavily");
        settings.SearchProviders["tavily"].Priority.Should().Be(100);
        settings.Current?.Profile.Should().NotBeNullOrEmpty();
        settings.Current?.Actor.Should().NotBeNull("subagent.json 应含 actor 配置");
        settings.Current?.SubAgentLiveness.Should().NotBeNull("subagent.json 应含 subAgentLiveness 配置");
    }

    #endregion
}
#pragma warning restore JCC3010, JCC3011, JCC3012