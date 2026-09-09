namespace Abs.Tests.Utils;

/// <summary>
/// EnvVarScope 单元测试 — 验证环境变量临时设置与 Dispose 自动恢复（含逆序、幂等、删除语义）
/// </summary>
public sealed class EnvVarScopeTest
{
    private static string UniqueName() => "JCC_TEST_ENV_" + Guid.NewGuid().ToString("N");

    // === Set 单变量 ===

    [Fact]
    public void Set_SingleVariable_RestoresOnDispose()
    {
        var name = UniqueName();
        Environment.SetEnvironmentVariable(name, "original");

        using (EnvVarScope.Set(name, "temp"))
        {
            Environment.GetEnvironmentVariable(name).Should().Be("temp");
        }

        Environment.GetEnvironmentVariable(name).Should().Be("original");
        Environment.SetEnvironmentVariable(name, null);
    }

    [Fact]
    public void Set_VariableDidNotExist_RestoresToNull()
    {
        var name = UniqueName();
        Environment.SetEnvironmentVariable(name, null);

        using (EnvVarScope.Set(name, "temp"))
        {
            Environment.GetEnvironmentVariable(name).Should().Be("temp");
        }

        Environment.GetEnvironmentVariable(name).Should().BeNull();
    }

    [Fact]
    public void Set_NullValue_DeletesVariable()
    {
        var name = UniqueName();
        Environment.SetEnvironmentVariable(name, "original");

        using (EnvVarScope.Set(name, null))
        {
            Environment.GetEnvironmentVariable(name).Should().BeNull();
        }

        Environment.GetEnvironmentVariable(name).Should().Be("original");
        Environment.SetEnvironmentVariable(name, null);
    }

    // === Add 链式多变量 ===

    [Fact]
    public void Add_MultipleVariables_RestoresAllOnDispose()
    {
        var name1 = UniqueName();
        var name2 = UniqueName();
        Environment.SetEnvironmentVariable(name1, "orig1");
        Environment.SetEnvironmentVariable(name2, "orig2");

        using (EnvVarScope.Set(name1, "temp1").Add(name2, "temp2"))
        {
            Environment.GetEnvironmentVariable(name1).Should().Be("temp1");
            Environment.GetEnvironmentVariable(name2).Should().Be("temp2");
        }

        Environment.GetEnvironmentVariable(name1).Should().Be("orig1");
        Environment.GetEnvironmentVariable(name2).Should().Be("orig2");
        Environment.SetEnvironmentVariable(name1, null);
        Environment.SetEnvironmentVariable(name2, null);
    }

    // === Dispose 语义 ===

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var name = UniqueName();
        Environment.SetEnvironmentVariable(name, "original");

        var scope = EnvVarScope.Set(name, "temp");
        scope.Dispose();
        var act = () => scope.Dispose();

        act.Should().NotThrow();
        Environment.GetEnvironmentVariable(name).Should().Be("original");
        Environment.SetEnvironmentVariable(name, null);
    }

    [Fact]
    public void Add_AfterDispose_ThrowsObjectDisposedException()
    {
        var scope = EnvVarScope.Set(UniqueName(), "temp");
        scope.Dispose();

        var act = () => scope.Add(UniqueName(), "x");

        act.Should().Throw<ObjectDisposedException>();
    }
}
