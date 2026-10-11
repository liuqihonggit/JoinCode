// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.Permission;

/// <summary>
/// PermissionChecker null 参数守卫测试 — 验证所有公开方法的 null/空/空白参数守卫
/// <para>ArgumentException.ThrowIfNullOrWhiteSpace 对 null 抛 ArgumentNullException,对空/空白抛 ArgumentException。</para>
/// </summary>
public sealed class PermissionCheckerNullGuardTests {

    #region 辅助构造

    private static PermissionChecker CreateChecker(
        MiddlewarePipeline<PermissionCheckContext>? pipeline = null,
        PermissionConfig? config = null,
        IFileSystem? fs = null) {
        return new PermissionChecker(
            pipeline ?? new MiddlewarePipeline<PermissionCheckContext>([]),
            Options.Create(config ?? PermissionConfig.CreateDefault()),
            fs ?? new InMemoryFileSystem());
    }

    #endregion

    #region CheckPermissionAsync — toolName 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckPermissionAsync_空toolName_抛ArgumentException(string? toolName) {
        await using var checker = CreateChecker();

        var act = () => checker.CheckPermissionAsync(toolName!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region AddToAutoApproved — toolName 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddToAutoApproved_空toolName_抛ArgumentException(string? toolName) {
        await using var checker = CreateChecker();

        var act = () => checker.AddToAutoApproved(toolName!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddToAutoApproved带规则_空toolName_抛ArgumentException(string? toolName) {
        await using var checker = CreateChecker();

        var act = () => checker.AddToAutoApproved(toolName!, "domain:example.com");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AddToAutoApproved带规则_null规则Content_不抛() {
        await using var checker = CreateChecker();

        var act = () => checker.AddToAutoApproved("WebFetch", null);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task AddToAutoApproved带规则_空规则Content_不抛() {
        await using var checker = CreateChecker();

        var act = () => checker.AddToAutoApproved("WebFetch", string.Empty);

        act.Should().NotThrow();
    }

    #endregion

    #region AddToAutoApprovedAndPersistAsync — toolName 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddToAutoApprovedAndPersistAsync_空toolName_抛ArgumentException(string? toolName) {
        await using var fs = new InMemoryFileSystem();
        await using var checker = CreateChecker(fs: fs);

        var act = () => checker.AddToAutoApprovedAndPersistAsync(toolName!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region AddToAutoRejected — toolName 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddToAutoRejected_空toolName_抛ArgumentException(string? toolName) {
        await using var checker = CreateChecker();

        var act = () => checker.AddToAutoRejected(toolName!);

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region RemoveFromAutoApproved — toolName 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveFromAutoApproved_空toolName_抛ArgumentException(string? toolName) {
        await using var checker = CreateChecker();

        var act = () => checker.RemoveFromAutoApproved(toolName!);

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region RemoveFromAutoRejected — toolName 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveFromAutoRejected_空toolName_抛ArgumentException(string? toolName) {
        await using var checker = CreateChecker();

        var act = () => checker.RemoveFromAutoRejected(toolName!);

        act.Should().Throw<ArgumentException>();
    }

    #endregion
}
