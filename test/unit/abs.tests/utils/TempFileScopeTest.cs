namespace Abs.Tests.Utils;

using Testing.Common.Services;

/// <summary>
/// TempFileScope 单元测试 — 验证临时文件路径预留与 Dispose 自动删除（含幂等、文件不存在不抛）
/// </summary>
public sealed class TempFileScopeTest
{
    [Fact]
    public void Create_ReturnsPathWithPrefixAndExtension()
    {
        var fs = new InMemoryFileSystem();

        var scope = TempFileScope.Create(fs, "jcc_repl_", ".cs");

        scope.Path.Should().Contain("jcc_repl_");
        scope.Path.Should().EndWith(".cs");
    }

    [Fact]
    public void Create_NullFileSystem_ThrowsArgumentNullException()
    {
        var act = () => TempFileScope.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Dispose_FileExists_DeletesFile()
    {
        var fs = new InMemoryFileSystem();
        var scope = TempFileScope.Create(fs, "test_", ".tmp");
        fs.WriteAllText(scope.Path, "content");

        scope.Dispose();

        fs.FileExists(scope.Path).Should().BeFalse();
    }

    [Fact]
    public void Dispose_FileDoesNotExist_DoesNotThrow()
    {
        var fs = new InMemoryFileSystem();
        var scope = TempFileScope.Create(fs, "test_", ".tmp");

        var act = () => scope.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var fs = new InMemoryFileSystem();
        var scope = TempFileScope.Create(fs, "test_", ".tmp");
        fs.WriteAllText(scope.Path, "content");

        scope.Dispose();
        var act = () => scope.Dispose();

        act.Should().NotThrow();
    }
}
