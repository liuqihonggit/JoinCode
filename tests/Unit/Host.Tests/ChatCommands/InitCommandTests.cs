namespace Host.Tests.ChatCommands;

using Testing.Common.Services;

/// <summary>
/// InitCommand 单元测试 — 覆盖 /init quick 路径
/// 验证目标:bug 修复 — 使用 AppDataConstants.AppDataFolder 而非硬编码 ".jcc"
/// 修复前:QuickInitAsync 硬编码 Path.Combine(cwd, ".jcc"),与 EnsureJccDirectory 使用 AppDataConstants.AppDataFolder (默认 "jcc") 路径不一致
/// 修复后:统一使用 AppDataConstants.AppDataFolder,确保目录创建与文件写入路径一致
/// </summary>
public sealed class InitCommandTests
{
    [Fact]
    public void Name_Should_Be_init()
    {
        var cmd = new InitCommand();
        cmd.Name.Should().Be("init");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new InitCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new InitCommand();
        cmd.Usage.Should().StartWith("/init");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new InitCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_WithQuick_Should_Create_JccDirectory_Using_AppDataConstants()
    {
        // Arrange — bug 复现:修复前硬编码 ".jcc",导致目录创建在 "jcc" 但文件写入 ".jcc"
        // 修复后应统一使用 AppDataConstants.AppDataFolder (默认 "jcc")
        var fs = new InMemoryFileSystem();
        var cwd = "/test/init-unit";
        fs.SetCurrentDirectory(cwd);

        var expectedJccDir = Path.Combine(cwd, AppDataConstants.AppDataFolder);
        var legacyJccDir = Path.Combine(cwd, ".jcc");

        var cmd = new InitCommand();
        var context = BuildContext("quick", fs);

        // Act
        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        // Assert — 目录创建使用 AppDataConstants.AppDataFolder
        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        fs.DirectoryExists(expectedJccDir).Should().BeTrue(
            $"应使用 AppDataConstants.AppDataFolder ('{AppDataConstants.AppDataFolder}') 创建目录,而非硬编码 '.jcc'");
        fs.DirectoryExists(legacyJccDir).Should().BeFalse(
            "不应创建硬编码的 '.jcc' 目录 (bug 修复前会同时存在两个目录)");
    }

    [Fact]
    public async Task Execute_WithQuick_Should_Write_Files_To_AppDataConstants_Path()
    {
        // Arrange — 验证文件写入路径与目录创建路径一致
        var fs = new InMemoryFileSystem();
        var cwd = "/test/init-files";
        fs.SetCurrentDirectory(cwd);

        var expectedJccDir = Path.Combine(cwd, AppDataConstants.AppDataFolder);
        var expectedRulesFile = Path.Combine(expectedJccDir, "project_rules.md");
        var expectedSettingsFile = Path.Combine(expectedJccDir, "settings.json");
        var legacyRulesFile = Path.Combine(cwd, ".jcc", "project_rules.md");

        var cmd = new InitCommand();
        var context = BuildContext("quick", fs);

        // Act
        await cmd.ExecuteAsync(context).ConfigureAwait(true);

        // Assert — 文件应写入 AppDataConstants.AppDataFolder 路径
        fs.FileExists(expectedRulesFile).Should().BeTrue(
            "project_rules.md 应写入 AppDataConstants.AppDataFolder 路径");
        fs.FileExists(expectedSettingsFile).Should().BeTrue(
            "settings.json 应写入 AppDataConstants.AppDataFolder 路径");
        fs.FileExists(legacyRulesFile).Should().BeFalse(
            "不应写入硬编码 '.jcc' 路径 (bug 修复前文件写入失败,因为目录创建在 'jcc' 而非 '.jcc')");

        // 验证文件内容非空
        var rulesContent = fs.ReadAllText(expectedRulesFile);
        rulesContent.Should().Contain("项目规则");
        var settingsContent = fs.ReadAllText(expectedSettingsFile);
        settingsContent.Should().Contain("openai");
    }

    [Fact]
    public async Task Execute_WithQuickAlias_Q_Should_Behave_Like_Quick()
    {
        // Arrange — "q" 是 "quick" 的别名
        var fs = new InMemoryFileSystem();
        var cwd = "/test/init-q";
        fs.SetCurrentDirectory(cwd);

        var expectedJccDir = Path.Combine(cwd, AppDataConstants.AppDataFolder);

        var cmd = new InitCommand();
        var context = BuildContext("q", fs);

        // Act
        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        // Assert
        result.ShouldContinue.Should().BeTrue();
        fs.DirectoryExists(expectedJccDir).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithQuick_Should_Not_Throw_When_ConfigService_Unavailable()
    {
        // Arrange — ServiceProvider 为 null 时,RegisterProjectConfigAsync 应安全跳过
        var fs = new InMemoryFileSystem();
        fs.SetCurrentDirectory("/test/init-no-config");

        var cmd = new InitCommand();
        var context = new ChatCommandContext
        {
            Arguments = "quick",
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = fs,
                // ServiceProvider 故意留 null
            },
        };

        // Act
        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        // Assert — 不应抛异常,应正常返回
        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithQuick_Should_Skip_Existing_Files()
    {
        // Arrange — 已存在的文件不应被覆盖
        var fs = new InMemoryFileSystem();
        var cwd = "/test/init-existing";
        fs.SetCurrentDirectory(cwd);

        var jccDir = Path.Combine(cwd, AppDataConstants.AppDataFolder);
        var rulesFile = Path.Combine(jccDir, "project_rules.md");
        var existingContent = "# 已有规则\n\n不应被覆盖\n";
        await fs.WriteAllTextAsync(rulesFile, existingContent, CancellationToken.None).ConfigureAwait(true);

        var cmd = new InitCommand();
        var context = BuildContext("quick", fs);

        // Act
        await cmd.ExecuteAsync(context).ConfigureAwait(true);

        // Assert — 文件内容应保持不变
        var content = fs.ReadAllText(rulesFile);
        content.Should().Be(existingContent, "已存在的 project_rules.md 不应被覆盖");
    }

    private static ChatCommandContext BuildContext(string arguments, IFileSystem fs)
    {
        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = fs,
            },
        };
    }
}
