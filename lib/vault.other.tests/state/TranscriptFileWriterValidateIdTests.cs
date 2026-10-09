
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.State;

/// <summary>
/// TranscriptFileWriter.ValidateId 字符白名单确定性测试
/// 合法: 字母/数字/'-'/'_'; 非法字符抛 ArgumentException
/// </summary>
public sealed class TranscriptFileWriterValidateIdTests {
    private const string Param = "id";

    // === 合法 ID ===

    [Theory]
    [InlineData("abc123")]
    [InlineData("ABC")]
    [InlineData("a-b-c")]
    [InlineData("a_b_c")]
    [InlineData("a1-b2_c3")]
    [InlineData("session-001")]
    [InlineData("user_id-42")]
    [InlineData("-")]
    [InlineData("_")]
    [InlineData("-_")]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData("Z9")]
    public void ValidateId_LegalId_DoesNotThrow(string id) {
        var act = () => TranscriptFileWriter.ValidateId(id, Param);
        act.Should().NotThrow();
    }

    // === 空 ID ===

    [Fact]
    public void ValidateId_EmptyString_DoesNotThrow() {
        // 空字符串无字符,foreach 不执行,不抛异常
        var act = () => TranscriptFileWriter.ValidateId("", Param);
        act.Should().NotThrow();
    }

    // === 非法字符 ===

    [Theory]
    [InlineData("a.b", '.')]
    [InlineData("a/b", '/')]
    [InlineData("a\\b", '\\')]
    [InlineData("a b", ' ')]
    [InlineData("a@b", '@')]
    [InlineData("a#b", '#')]
    [InlineData("a$b", '$')]
    [InlineData("a:b", ':')]
    [InlineData("a;b", ';')]
    [InlineData("a,b", ',')]
    [InlineData("a+b", '+')]
    [InlineData("a=b", '=')]
    [InlineData("a(b", '(')]
    [InlineData("a[b", '[')]
    [InlineData("a{b", '{')]
    [InlineData("a<b", '<')]
    [InlineData("a|b", '|')]
    [InlineData("a~b", '~')]
    [InlineData("a`b", '`')]
    [InlineData("a^b", '^')]
    [InlineData("a&b", '&')]
    [InlineData("a*b", '*')]
    [InlineData("a%b", '%')]
    [InlineData("a!b", '!')]
    [InlineData("a?b", '?')]
    public void ValidateId_IllegalChar_ThrowsArgumentException(string id, char illegalChar) {
        var act = () => TranscriptFileWriter.ValidateId(id, Param);
        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{illegalChar}*")
            .WithParameterName(Param);
    }

    // === 非法字符在开头/结尾 ===

    [Fact]
    public void ValidateId_IllegalCharAtStart_Throws() {
        var act = () => TranscriptFileWriter.ValidateId(".abc", Param);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateId_IllegalCharAtEnd_Throws() {
        var act = () => TranscriptFileWriter.ValidateId("abc.", Param);
        act.Should().Throw<ArgumentException>();
    }

    // === 多个非法字符(抛第一个)===

    [Fact]
    public void ValidateId_MultipleIllegalChars_ThrowsOnFirst() {
        var act = () => TranscriptFileWriter.ValidateId("a.b/c", Param);
        var ex = act.Should().Throw<ArgumentException>().Which;
        ex.Message.Should().Contain(".");
    }

    // === paramName 传播 ===

    [Fact]
    public void ValidateId_ThrowsWithProvidedParamName() {
        var act = () => TranscriptFileWriter.ValidateId("a.b", "myParam");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("myParam");
    }

    // === null ID ===

    [Fact]
    public void ValidateId_NullId_ThrowsNullReferenceException() {
        // ValidateId 不显式检查 null,foreach null string 抛 NRE
        var act = () => TranscriptFileWriter.ValidateId(null!, Param);
        act.Should().Throw<NullReferenceException>();
    }

    // === Unicode 字母合法 ===

    [Theory]
    [InlineData("你好")]      // CJK 字母
    [InlineData("café")]      // 带重音拉丁字母
    [InlineData("αβγ")]       // 希腊字母
    public void ValidateId_UnicodeLetters_DoesNotThrow(string id) {
        // char.IsLetterOrDigit 对 Unicode 字母返回 true
        var act = () => TranscriptFileWriter.ValidateId(id, Param);
        act.Should().NotThrow();
    }

    // === 确定性 ===

    [Fact]
    public void ValidateId_Deterministic_SameInputSameBehavior() {
        // 合法 ID 多次调用都不抛
        for (var i = 0; i < 5; i++) {
            TranscriptFileWriter.ValidateId("legal-id_123", Param);
        }
        // 非法 ID 多次调用都抛
        for (var i = 0; i < 5; i++) {
            FluentActions.Invoking(() => TranscriptFileWriter.ValidateId("illegal.id", Param))
                .Should().Throw<ArgumentException>();
        }
    }
}
