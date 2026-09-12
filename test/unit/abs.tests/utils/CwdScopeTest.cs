namespace Abs.Tests.Utils;

using Testing.Common.Services;

/// <summary>
/// CwdScope 单元测试 — 验证工作目录切换与 Dispose 自动恢复（含幂等）
/// </summary>
public sealed class CwdScopeTest
{
    [Fact]
    public void Enter_SwitchesToNewDirectory()
    {
        var fs = new InMemoryFileSystem();
        var original = fs.GetCurrentDirectory();

        using (CwdScope.Enter(fs, "/new/cwd"))
        {
            fs.GetCurrentDirectory().Should().Be("/new/cwd");
        }

        fs.GetCurrentDirectory().Should().Be(original);
    }

    [Fact]
    public void Dispose_RestoresOriginalDirectory()
    {
        var fs = new InMemoryFileSystem();
        var original = fs.GetCurrentDirectory();

        var scope = CwdScope.Enter(fs, "/tmp/work");
        scope.Dispose();

        fs.GetCurrentDirectory().Should().Be(original);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var fs = new InMemoryFileSystem();
        var original = fs.GetCurrentDirectory();

        var scope = CwdScope.Enter(fs, "/tmp/work");
        scope.Dispose();
        var act = () => scope.Dispose();

        act.Should().NotThrow();
        fs.GetCurrentDirectory().Should().Be(original);
    }

    [Fact]
    public void Enter_NullFileSystem_ThrowsArgumentNullException()
    {
        var act = () => CwdScope.Enter(null!, "/tmp");

        act.Should().Throw<ArgumentNullException>();
    }
}
