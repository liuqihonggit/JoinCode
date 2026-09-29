namespace Core.Tests.Configuration;

/// <summary>
/// ConfigLoader 内部纯函数确定性测试 — MaskKey 脱敏 / ParseJsonValueElement 类型推断。
/// <para>不依赖文件系统,仅验证 internal 静态方法的输入输出契约。</para>
/// </summary>
public sealed class ConfigLoaderInternalTests {

    #region MaskKey — 安全脱敏

    [Fact]
    public void MaskKey_Null_Should_Return_EmptyPlaceholder() {
        ConfigLoader.MaskKey(null!).Should().Be("<empty>");
    }

    [Fact]
    public void MaskKey_Empty_Should_Return_EmptyPlaceholder() {
        ConfigLoader.MaskKey(string.Empty).Should().Be("<empty>");
    }

    [Theory]
    [InlineData("a")]      // 1 字符
    [InlineData("ab")]     // 2 字符
    [InlineData("abc")]    // 3 字符
    [InlineData("abcd")]   // 4 字符 — key[..4] 会暴露整个 key,必须完全遮蔽
    public void MaskKey_ShortKey_Should_FullyMask(string key) {
        var masked = ConfigLoader.MaskKey(key);
        masked.Should().Be("***");
        // 安全断言:不暴露原始 key
        masked.Should().NotContain(key);
    }

    [Theory]
    [InlineData("abcde")]          // 5 字符
    [InlineData("abcdefgh")]       // 8 字符
    [InlineData("abcdefghij")]     // 10 字符
    [InlineData("abcdefghijkl")]   // 12 字符
    public void MaskKey_MediumKey_Should_Show_Prefix_Only(string key) {
        var masked = ConfigLoader.MaskKey(key);
        masked.Should().StartWith(key[..4]);
        masked.Should().EndWith("...");
        // 安全断言:不完整暴露原始 key
        masked.Should().NotBe(key);
    }

    [Fact]
    public void MaskKey_LongKey_Should_Show_Prefix8_And_Suffix4() {
        var key = "abcdefghijklmnop";  // 16 字符
        var masked = ConfigLoader.MaskKey(key);
        masked.Should().Be("abcdefgh...mnop");
        // 安全断言:不完整暴露原始 key
        masked.Should().NotBe(key);
    }

    [Fact]
    public void MaskKey_RealApiKey_Should_Not_Expose_Full_Key() {
        var key = "sk-proj-abc123def456ghi789jkl012mno345pqr678stu901vwx234yz";
        var masked = ConfigLoader.MaskKey(key);
        // 应以 sk-proj- 开头(前8位)
        masked.Should().StartWith("sk-proj-");
        // 应以 yz 结尾(后4位)
        masked.Should().EndWith("yz");
        // 中间应有 ...
        masked.Should().Contain("...");
        // 安全断言:不完整暴露原始 key
        masked.Should().NotBe(key);
        masked.Length.Should().BeLessThan(key.Length);
    }

    [Fact]
    public void MaskKey_Should_Be_Deterministic() {
        var key = "abcdefghijklmnop";
        ConfigLoader.MaskKey(key).Should().Be(ConfigLoader.MaskKey(key));
    }

    #endregion

    #region ParseJsonValueElement — 类型推断

    [Fact]
    public void ParseJsonValueElement_Null_Should_Return_NullKind() {
        var element = ConfigLoader.ParseJsonValueElement(null);
        element.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ParseJsonValueElement_EmptyString_Should_Return_EmptyString() {
        var element = ConfigLoader.ParseJsonValueElement(string.Empty);
        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("true", JsonValueKind.True)]
    [InlineData("True", JsonValueKind.True)]
    [InlineData("TRUE", JsonValueKind.True)]
    [InlineData(" false ", JsonValueKind.False)]
    [InlineData("False", JsonValueKind.False)]
    public void ParseJsonValueElement_Boolean_Should_Return_BoolKind(string input, JsonValueKind expected) {
        var element = ConfigLoader.ParseJsonValueElement(input);
        element.ValueKind.Should().Be(expected);
    }

    [Fact]
    public void ParseJsonValueElement_NullLiteral_Should_Return_NullKind() {
        var element = ConfigLoader.ParseJsonValueElement("null");
        element.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("42", JsonValueKind.Number)]
    [InlineData("-42", JsonValueKind.Number)]
    [InlineData("3.14", JsonValueKind.Number)]
    [InlineData("-3.14", JsonValueKind.Number)]
    [InlineData("1e10", JsonValueKind.Number)]
    [InlineData("0", JsonValueKind.Number)]
    [InlineData(" 123 ", JsonValueKind.Number)]
    public void ParseJsonValueElement_Number_Should_Return_NumberKind(string input, JsonValueKind expected) {
        var element = ConfigLoader.ParseJsonValueElement(input);
        element.ValueKind.Should().Be(expected);
    }

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("hello world", "hello world")]
    [InlineData("123abc", "123abc")]
    public void ParseJsonValueElement_String_Should_Return_StringKind(string input, string expected) {
        var element = ConfigLoader.ParseJsonValueElement(input);
        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be(expected);
    }

    [Fact]
    public void ParseJsonValueElement_StringWithSpecialChars_Should_Escape() {
        var element = ConfigLoader.ParseJsonValueElement("a\"b\\c\nd");
        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be("a\"b\\c\nd");
    }

    [Fact]
    public void ParseJsonValueElement_StringWithTab_Should_Escape() {
        var element = ConfigLoader.ParseJsonValueElement("a\tb");
        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be("a\tb");
    }

    [Fact]
    public void ParseJsonValueElement_StringWithBackslash_Should_Escape() {
        var element = ConfigLoader.ParseJsonValueElement(@"a\b\c");
        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be(@"a\b\c");
    }

    #endregion
}
