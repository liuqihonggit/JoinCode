// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Mcp.Tests;

public sealed class ArgumentSubstitutorTests {
    private readonly ArgumentSubstitutor _sut = new();

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Substitute_NullContent_ReturnsNull() {
        _sut.Substitute(null!, "args").Should().BeNull();
    }

    [Fact]
    public void Substitute_EmptyContent_ReturnsEmpty() {
        _sut.Substitute("", "args").Should().BeEmpty();
    }

    [Fact]
    public void Substitute_DollarArguments_ReplacesAll() {
        _sut.Substitute("prefix $ARGUMENTS suffix", "hello world").Should().Be("prefix hello world suffix");
    }

    [Fact]
    public void Substitute_IndexedArgument_ReplacesSpecific() {
        _sut.Substitute("$ARGUMENTS[0] and $ARGUMENTS[1]", "first second").Should().Be("first and second");
    }

    [Fact]
    public void Substitute_ShorthandArgument_ReplacesSpecific() {
        _sut.Substitute("$0 and $1", "first second").Should().Be("first and second");
    }

    [Fact]
    public void Substitute_NamedArgument_ReplacesByName() {
        _sut.Substitute("$name is $age", "Alice 30", ["name", "age"]).Should().Be("Alice is 30");
    }

    [Fact]
    public void Substitute_SkillDirectory_ReplacesPlaceholder() {
        _sut.Substitute("dir: ${CLAUDE_SKILL_DIR}", null, skillDirectory: @"C:\skills\my-skill")
            .Should().Be("dir: C:/skills/my-skill");
    }

    [Fact]
    public void Substitute_SessionId_ReplacesPlaceholder() {
        _sut.Substitute("session: ${CLAUDE_SESSION_ID}", null, sessionId: "sess-123")
            .Should().Be("session: sess-123");
    }

    [Fact]
    public void Substitute_NoPlaceholder_AppendsArgs() {
        var result = _sut.Substitute("content", "extra args");
        result.Should().Contain("ARGUMENTS: extra args");
    }

    [Fact]
    public void Substitute_NoPlaceholder_AppendDisabled() {
        var result = _sut.Substitute("content", "extra args", appendIfNoPlaceholder: false);
        result.Should().Be("content");
    }
}