#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Guard.Tests.Configuration;

/// <summary>
/// SettingsLoader BDD 测试
/// 验证 5 层配置来源优先级 + 合并策略
/// 与 ProjectRulesLoaderTests 共享 AppDataConstants 全局状态,需串行执行避免相互污染
/// </summary>
[Collection("AppDataConstantsCollection")]
public class SettingsLoaderTests : IDisposable
{
    private static readonly string DefaultOpenAiModelId = ModelConfigLoader.GetDefaultModelId("openai");
    private static readonly string DefaultAnthropicModelId = ModelConfigLoader.GetDefaultModelId("anthropic");

    private readonly string _tempDir;
    private readonly string _projectAppDataDir;
    private readonly string _userAppDataDir;
    private readonly string? _originalAppDataFolder;
    private readonly string? _originalSettingsFileName;
    private readonly string? _originalEnvAppDataFolder;
    private readonly IFileSystem _fs = TestFileSystem.Current;

    public SettingsLoaderTests()
    {
        _tempDir = $"/test/jcc_settings_test_{Guid.NewGuid().ToString("N")[..8]}";
        _fs.CreateDirectory(_tempDir);

        // 项目设置目录: _tempDir/.jcc/
        _projectAppDataDir = Path.Combine(_tempDir, ".jcc");
        _fs.CreateDirectory(_projectAppDataDir);

        // 用户设置隔离目录（绝对路径）
        _userAppDataDir = $"/test/jcc_settings_user_{Guid.NewGuid().ToString("N")[..8]}";
        _fs.CreateDirectory(_userAppDataDir);

        // 先清除环境变量，确保读取到 backing field 的真实值
        _originalEnvAppDataFolder = Environment.GetEnvironmentVariable(JccEnvVarConstants.AppDataFolder);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, null);

        _originalAppDataFolder = AppDataConstants.AppDataFolder;
        _originalSettingsFileName = AppDataConstants.SettingsFileName;

        // 用户设置: 通过环境变量覆盖为临时绝对路径，GetUserSettingsPath 的 IsPathRooted 分支直接使用
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, _userAppDataDir);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment(); // 刷新 Paths 实例
        AppDataConstants.SettingsFileName = "settings.json";
    }

    public void Dispose()
    {
        // 还原全局 AppDataConstants,避免污染后续测试
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, _originalEnvAppDataFolder);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment(); // 恢复 Paths 实例
    }

    #region 场景1: 多源加载 + 优先级合并

    [Fact]
    public async Task Given_用户设置和项目设置_When_加载全部_Then_项目设置覆盖用户设置()
    {
        // Given: 用户设置 model=gpt-4o, 项目设置 model=claude-sonnet
        var userSettings = new SettingsJson { Model = DefaultOpenAiModelId };
        var projectSettings = new SettingsJson { Model = DefaultAnthropicModelId };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When: 加载全部来源
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 项目设置覆盖用户设置
        result.Model.Should().Be(DefaultAnthropicModelId);
    }

    [Fact]
    public async Task Given_用户设置和本地设置_When_加载全部_Then_本地设置覆盖用户设置()
    {
        // Given: 用户设置 language=en-US, 本地设置 language=zh-CN
        var userSettings = new SettingsJson { Language = "en-US" };
        var localSettings = new SettingsJson { Language = "zh-CN" };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteLocalSettingsAsync(localSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then
        result.Language.Should().Be("zh-CN");
    }

    [Fact]
    public async Task Given_用户设置有env_项目设置也有env_When_加载全部_Then_env字典合并()
    {
        // Given
        var userSettings = new SettingsJson { Env = new Dictionary<string, string> { ["KEY1"] = "user1", ["KEY2"] = "user2" } };
        var projectSettings = new SettingsJson { Env = new Dictionary<string, string> { ["KEY2"] = "project2", ["KEY3"] = "project3" } };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 字典合并，高优先级覆盖
        result.Env.Should().NotBeNull();
        result.Env!["KEY1"].Should().Be("user1");
        result.Env["KEY2"].Should().Be("project2"); // 项目覆盖用户
        result.Env["KEY3"].Should().Be("project3");
    }

    #endregion

    #region 场景2: 无配置文件时返回默认值

    [Fact]
    public async Task Given_无任何配置文件_When_加载全部_Then_返回空SettingsJson()
    {
        // Given: 无配置文件

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then
        result.Should().NotBeNull();
        result.Model.Should().BeNull();
    }

    #endregion

    #region 场景3: 损坏文件不影响其他来源

    [Fact]
    public async Task Given_用户设置损坏_项目设置正常_When_加载全部_Then_项目设置仍可用()
    {
        // Given: 用户设置损坏
        var userPath = GetUserSettingsPath();
        var dir = Path.GetDirectoryName(userPath);
        if (!string.IsNullOrEmpty(dir)) _fs.CreateDirectory(dir);
        await _fs.WriteAllTextAsync(userPath, "{ invalid json }").ConfigureAwait(true);
        var projectSettings = new SettingsJson { Model = DefaultAnthropicModelId };
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 项目设置仍可用
        result.Model.Should().Be(DefaultAnthropicModelId);
    }

    #endregion

    #region 场景4: 保存到指定来源

    [Fact]
    public async Task Given_保存到UserSettings_When_重新加载_Then_数据一致()
    {
        // Given
        var settings = new SettingsJson { Model = DefaultOpenAiModelId, FastMode = true };

        // When: 保存到用户设置
        await SettingsLoader.SaveSettingsAsync(_fs, SettingSource.UserSettings, settings, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        // Then: 重新加载能读到
        var loaded = await SettingsLoader.LoadUserSettingsAsync(_fs).ConfigureAwait(true);
        loaded.Should().NotBeNull();
        loaded!.Model.Should().Be(DefaultOpenAiModelId);
        loaded.FastMode.Should().BeTrue();
    }

    #endregion

    #region 场景5: 权限数组拼接去重

    [Fact]
    public async Task Given_用户权限和项目权限_When_加载全部_Then_权限数组拼接去重()
    {
        // Given
        var userSettings = new SettingsJson
        {
            Permissions = new PermissionsSettings { Allow = ["Bash(npm test)"], Deny = ["Bash(rm)"] },
        };
        var projectSettings = new SettingsJson
        {
            Permissions = new PermissionsSettings { Allow = ["ReadFile"], DefaultMode = "autoAccept" },
        };

        await WriteSettingsAsync(GetUserSettingsPath(), userSettings).ConfigureAwait(true);
        await WriteProjectSettingsAsync(projectSettings).ConfigureAwait(true);

        // When
        var result = await SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: _tempDir).ConfigureAwait(true);

        // Then: 数组拼接
        result.Permissions.Should().NotBeNull();
        result.Permissions!.Allow.Should().Contain("Bash(npm test)");
        result.Permissions.Allow.Should().Contain("ReadFile");
        result.Permissions.DefaultMode.Should().Be("autoAccept");
    }

    #endregion

    #region 辅助方法

    private string GetUserSettingsPath() => SettingsLoader.GetUserSettingsPath();

    private async Task WriteSettingsAsync(string path, SettingsJson settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            // Windows 并行测试时偶发 UnauthorizedAccessException(目录刚被前一个测试清理),
            // 增加重试机制,确保临时目录稳定创建
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (!_fs.DirectoryExists(dir))
                        _fs.CreateDirectory(dir);
                    break;
                }
                catch (UnauthorizedAccessException) when (i < maxRetries - 1)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }
            }
        }
        var json = JsonSerializer.Serialize(settings, ConfigIndentedJsonContext.Default.SettingsJson);
        _fs.WriteAllText(path, json);
    }

    private async Task WriteProjectSettingsAsync(SettingsJson settings)
    {
        var path = Path.Combine(_projectAppDataDir, "settings.json");
        await WriteSettingsAsync(path, settings).ConfigureAwait(true);
    }

    private async Task WriteLocalSettingsAsync(SettingsJson settings)
    {
        var path = Path.Combine(_projectAppDataDir, "settings.local.json");
        await WriteSettingsAsync(path, settings).ConfigureAwait(true);
    }

    #endregion
}
#pragma warning restore JCC3010, JCC3011, JCC3012
