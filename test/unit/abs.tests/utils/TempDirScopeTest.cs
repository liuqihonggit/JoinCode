// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Abs.Tests.Utils;
/// <summary>
/// TempDirScope 单元测试 — 验证临时目录创建与 DisposeAsync 自动清理（含幂等、目录已删不抛）
/// </summary>
public sealed class TempDirScopeTest {
    // === Create ===

    [Fact]
    public async Task Create_WithFileSystem_CreatesDirectory() {
        var fs = new InMemoryFileSystem();

        await using var scope = TempDirScope.Create(fs, "test_");

        fs.DirectoryExists(scope.Path).Should().BeTrue();
        scope.Path.Should().Contain("test_");
    }

    [Fact]
    public async Task Create_NullPrefix_UsesDefaultPrefix() {
        var fs = new InMemoryFileSystem();

        await using var scope = TempDirScope.Create(fs);

        fs.DirectoryExists(scope.Path).Should().BeTrue();
        scope.Path.Should().Contain("jcctmp_");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Create_NullFileSystem_ThrowsArgumentNullException() {
        var act = () => TempDirScope.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // === DisposeAsync ===

    [Fact]
    public async Task DisposeAsync_DeletesDirectory() {
        var fs = new InMemoryFileSystem();
        var scope = TempDirScope.Create(fs, "test_");
        var path = scope.Path;

        await scope.DisposeAsync();

        fs.DirectoryExists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent() {
        var fs = new InMemoryFileSystem();
        var scope = TempDirScope.Create(fs, "test_");

        await scope.DisposeAsync();
        var act = async () => await scope.DisposeAsync();

        await act.Should().NotThrowAsync();
    }
}