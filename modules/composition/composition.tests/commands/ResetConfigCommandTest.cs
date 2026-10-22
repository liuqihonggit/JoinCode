namespace Composition.Tests.Commands;

/// <summary>
/// /reset-config 命令单元测试 — 覆盖 all(全部重置+文件不存在提示)/空参默认 all/
/// auth 清空/trust 清空/onboarding 删除/未知选项提示
/// <para>jccDir 与命令侧同公式(UserProfile + AppDataFolder),写入经 InMemoryFileSystem 0磁盘IO</para>
/// </summary>
[Collection("command-output")]
public sealed class ResetConfigCommandTest {
    private static readonly string JccDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppDataConstants.AppDataFolder);
    private static readonly string AuthFile = Path.Combine(JccDir, AppDataConstants.AuthFileName);
    private static readonly string TrustFile = Path.Combine(JccDir, AppDataConstants.TrustedFoldersFileName);
    private static readonly string OnboardingFile = Path.Combine(JccDir, "onboarding_complete.json");

    private static ChatCommandContext CreateContext(IFileSystem fs, string arguments) {
        var services = CommandTestHarness.CreateServices(fileSystem: fs);
        return CommandTestHarness.CreateContext(services, arguments: arguments);
    }

    [Fact]
    public async Task Execute_All_ResetsEveryConfigFile() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(AuthFile, "{\"openai\":\"sk-key\"}");
        await fs.WriteAllTextAsync(TrustFile, "{\"folders\":[\"dir-a\"]}");
        await fs.WriteAllTextAsync(OnboardingFile, "{\"completed\":true}");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "all")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("重置所有配置文件...");
        output.Should().Contain("✓ 所有配置已重置");
        output.Should().Contain("✓ auth.json 已清空");
        output.Should().Contain("✓ trusted_folders.json 已清空");
        output.Should().Contain("✓ onboarding_complete.json 已删除");
        (await fs.ReadAllTextAsync(AuthFile)).Should().Be("{}");
        (await fs.ReadAllTextAsync(TrustFile)).Should().Be("{\"folders\":[]}");
        fs.FileExists(OnboardingFile).Should().BeFalse("onboarding 完成标记应被删除");
        fs.FileExists(Path.Combine(JccDir, "settings.json")).Should().BeTrue(
            "all 重置应重建默认配置树 settings.json");
    }

    [Fact]
    public async Task Execute_EmptyArgs_DefaultsToAllScope() {
        var fs = TestFileSystem.Current;

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("重置所有配置文件...", "空参应默认 all 范围");
        output.Should().Contain("· auth.json 不存在", "auth 文件缺失时应提示而非报错");
    }

    [Fact]
    public async Task Execute_Auth_ClearsAuthFileContent() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(AuthFile, "{\"openai\":\"sk-key\",\"anthropic\":\"sk-2\"}");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "auth")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("✓ auth.json 已清空");
        (await fs.ReadAllTextAsync(AuthFile)).Should().Be("{}", "auth 重置应写入空对象");
    }

    [Fact]
    public async Task Execute_Auth_FileMissing_ShowsNotice() {
        var fs = TestFileSystem.Current;

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "auth")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("· auth.json 不存在");
    }

    [Fact]
    public async Task Execute_Trust_ClearsTrustedFoldersFile() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(TrustFile, "{\"folders\":[\"dir-a\",\"dir-b\"]}");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "trust")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("✓ trusted_folders.json 已清空");
        (await fs.ReadAllTextAsync(TrustFile)).Should().Be("{\"folders\":[]}");
    }

    [Fact]
    public async Task Execute_Onboarding_DeletesMarkerFile() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(OnboardingFile, "{\"completed\":true}");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "onboarding")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("✓ onboarding_complete.json 已删除");
        fs.FileExists(OnboardingFile).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Onboarding_FileMissing_ShowsNotice() {
        var fs = TestFileSystem.Current;

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "onboarding")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("· onboarding_complete.json 不存在");
    }

    [Fact]
    public async Task Execute_UnknownScope_ShowsAvailableOptions() {
        var fs = TestFileSystem.Current;

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "bogus")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知选项: bogus");
        output.Should().Contain("可用选项: all, auth, settings, trust, onboarding",
            "错误提示应引导用户使用正确选项");
    }

    [Fact]
    public async Task Execute_ScopeIsCaseInsensitive() {
        var fs = TestFileSystem.Current;

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ResetConfigCommand().ExecuteAsync(CreateContext(fs, "AUTH")));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("auth.json", "scope 参数应 ToLowerInvariant 归一匹配");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ResetConfigCommand();
        cmd.Name.Should().Be("reset-config");
        cmd.Usage.Should().StartWith("/reset-config");
    }
}
