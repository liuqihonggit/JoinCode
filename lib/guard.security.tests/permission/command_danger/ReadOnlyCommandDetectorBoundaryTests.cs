namespace Core.Tests.Permission;

/// <summary>
/// ReadOnlyCommandDetector 位掩码边界测试 — 通过 CheckReadOnlyConstraints 间接测试 IsShellMetacharacter
/// <para>Shell 元字符位掩码: 低 64 位(char 0-63)含 \n \r ! # $ &amp; ; &lt; &gt;; 高 64 位(char 64-127)含 ` \ { | }</para>
/// <para>边界: char 63(低掩码末位)、char 64(高掩码首位)、char 127(高掩码末位)、char 128+(超出范围)</para>
/// </summary>
public sealed class ReadOnlyCommandDetectorBoundaryTests {
    private readonly ReadOnlyCommandDetector _sut = new();

    #region 元字符 — 应阻止 Allow

    /// <summary>低掩码元字符: \n \r ! # $ & ; < > — 命令含元字符不应返回 Allow</summary>
    [Theory]
    [InlineData('!')]
    [InlineData('#')]
    [InlineData('&')]
    [InlineData(';')]
    [InlineData('<')]
    [InlineData('>')]
    public void CheckReadOnlyConstraints_低掩码元字符_不返回Allow(char meta) {
        var command = $"cat x{meta}y";

        var result = _sut.CheckReadOnlyConstraints(command);

        result.Behavior.Should().NotBe(PermissionBehavior.Allow);
    }

    /// <summary>高掩码元字符: ` \ { | } — 命令含元字符不应返回 Allow</summary>
    [Theory]
    [InlineData('`')]
    [InlineData('\\')]
    [InlineData('{')]
    [InlineData('|')]
    [InlineData('}')]
    public void CheckReadOnlyConstraints_高掩码元字符_不返回Allow(char meta) {
        var command = $"cat x{meta}y";

        var result = _sut.CheckReadOnlyConstraints(command);

        result.Behavior.Should().NotBe(PermissionBehavior.Allow);
    }

    /// <summary>$ 元字符 — 触发变量扩展检测,不返回 Allow</summary>
    [Fact]
    public void CheckReadOnlyConstraints_美元符_不返回Allow() {
        var result = _sut.CheckReadOnlyConstraints("cat x$y");

        result.Behavior.Should().NotBe(PermissionBehavior.Allow);
    }

    /// <summary>换行符元字符 — 不返回 Allow</summary>
    [Fact]
    public void CheckReadOnlyConstraints_换行符_不返回Allow() {
        var result = _sut.CheckReadOnlyConstraints("cat x\ny");

        result.Behavior.Should().NotBe(PermissionBehavior.Allow);
    }

    /// <summary>回车符元字符 — 不返回 Allow</summary>
    [Fact]
    public void CheckReadOnlyConstraints_回车符_不返回Allow() {
        var result = _sut.CheckReadOnlyConstraints("cat x\ry");

        result.Behavior.Should().NotBe(PermissionBehavior.Allow);
    }

    #endregion

    #region 非元字符边界 — 应返回 Allow

    /// <summary>非元字符: @ . _ - a — 不触发元字符检测,简单命令返回 Allow</summary>
    [Theory]
    [InlineData('@')]
    [InlineData('.')]
    [InlineData('_')]
    [InlineData('-')]
    [InlineData('a')]
    public void CheckReadOnlyConstraints_非元字符_返回Allow(char c) {
        var command = $"cat x{c}y";

        var result = _sut.CheckReadOnlyConstraints(command);

        result.Behavior.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>边界 char 64 (@) — 高掩码首位,非元字符,返回 Allow</summary>
    [Fact]
    public void CheckReadOnlyConstraints_边界char64_返回Allow() {
        var result = _sut.CheckReadOnlyConstraints("cat x@y");

        result.Behavior.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>边界 char 127 (DEL) — 高掩码末位,非元字符,返回 Allow</summary>
    [Fact]
    public void CheckReadOnlyConstraints_边界char127_返回Allow() {
        var command = "cat x" + (char)127 + "y";

        var result = _sut.CheckReadOnlyConstraints(command);

        result.Behavior.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>边界 char 128 — 超出位掩码范围,非元字符,返回 Allow</summary>
    [Fact]
    public void CheckReadOnlyConstraints_边界char128_返回Allow() {
        var command = "cat x" + (char)128 + "y";

        var result = _sut.CheckReadOnlyConstraints(command);

        result.Behavior.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>边界 char 0 — 低掩码首位,非元字符,返回 Allow</summary>
    [Fact]
    public void CheckReadOnlyConstraints_边界char0_非元字符() {
        var command = "cat x" + (char)1 + "y";

        var result = _sut.CheckReadOnlyConstraints(command);

        result.Behavior.Should().Be(PermissionBehavior.Allow);
    }

    #endregion

    #region 简单只读命令 — 无元字符返回 Allow

    [Fact]
    public void CheckReadOnlyConstraints_简单只读命令_返回Allow() {
        var result = _sut.CheckReadOnlyConstraints("cat file.txt");

        result.Behavior.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public void CheckReadOnlyConstraints_空命令_返回Passthrough() {
        var result = _sut.CheckReadOnlyConstraints("");

        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void CheckReadOnlyConstraints_空白命令_返回Passthrough() {
        var result = _sut.CheckReadOnlyConstraints("   ");

        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    #endregion
}
