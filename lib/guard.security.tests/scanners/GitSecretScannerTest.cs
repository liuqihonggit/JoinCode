namespace Guard.Security.Tests;

/// <summary>
/// GitSecretScanner 守卫测试 — 验证 null 参数抛 ArgumentNullException
/// <para>确定性测试:给定 null 输入 → 断言抛 ArgumentNullException,不依赖时序/IO。</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class GitSecretScannerTest {

    [Fact]
    public async Task ScanFileNamesAsync_NullStagedFiles_ThrowsArgumentNullException() {
        await using var scanner = new GitSecretScanner(NullLogger<GitSecretScanner>.Instance);
        var act = async () => await scanner.ScanFileNamesAsync(null!);
        var thrown = await act.Should().ThrowAsync<ArgumentNullException>();
        thrown.Which.ParamName.Should().Be("stagedFiles");
    }
}
