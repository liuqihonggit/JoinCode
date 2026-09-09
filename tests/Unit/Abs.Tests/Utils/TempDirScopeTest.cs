namespace Abs.Tests.Utils;

using Testing.Common.Services;

/// <summary>
/// TempDirScope 单元测试 — 验证临时目录创建与 DisposeAsync 自动清理（含幂等、目录已删不抛）
/// </summary>
public sealed class TempDirScopeTest
{
    // === Create ===

    [Fact]
    public void Create_WithFileSystem_CreatesDirectory()
    {
        var fs = new InMemoryFileSystem();

        var scope = TempDirScope.Create(fs, "test_");

        fs.DirectoryExists(scope.Path).Should().BeTrue();
        scope.Path.Should().Contain("test_");
    }

    [Fact]
    public void Create_NullPrefix_UsesDefaultPrefix()
    {
        var fs = new InMemoryFileSystem();

        var scope = TempDirScope.Create(fs);

        fs.DirectoryExists(scope.Path).Should().BeTrue();
        scope.Path.Should().Contain("jcctmp_");
    }

    [Fact]
    public void Create_NullFileSystem_ThrowsArgumentNullException()
    {
        var act = () => TempDirScope.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // === DisposeAsync ===

    [Fact]
    public async Task DisposeAsync_DeletesDirectory()
    {
        var fs = new InMemoryFileSystem();
        var scope = TempDirScope.Create(fs, "test_");
        var path = scope.Path;

        await scope.DisposeAsync();

        fs.DirectoryExists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var fs = new InMemoryFileSystem();
        var scope = TempDirScope.Create(fs, "test_");

        await scope.DisposeAsync();
        var act = async () => await scope.DisposeAsync();

        await act.Should().NotThrowAsync();
    }
}
