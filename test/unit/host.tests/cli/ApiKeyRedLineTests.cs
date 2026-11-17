namespace Host.Tests.Cli;

/// <summary>
/// 密钥红线判定契约 — 拦「真的密钥」，放「长得像密钥的名字」。
/// <para>红线在程序入口处直接阻断，所以误伤的代价是「某个工具彻底不可用」，
/// 而漏检的代价是「密钥进了进程列表」。两类错误都要避免，但**参数名不能作为唯一依据</b>：
/// <c>--key</c> / <c>--token</c> / <c>--secret</c> 是常见业务参数名（配置项、认证方式、密文），
/// 按名字一刀切会让它们永远调不通。</para>
/// <para>真机证据见 docs/implemented/task/TASK088-密钥红线按值判定.md。</para>
/// </summary>
public sealed class ApiKeyRedLineTests {
    [Theory]
    [InlineData("--api-key", "sk-abc")]
    [InlineData("--apikey", "whatever")]
    [InlineData("--access-key", "AKIA1234")]
    [InlineData("--client-secret", "x")]
    [InlineData("--password", "x")]
    public void Rejects_ExplicitSecretOptionNames(string option, string value) {
        Check("mcp_call", "tool", option, value).Should().NotBeNull();
    }

    [Theory]
    [InlineData("--key", "theme")]
    [InlineData("--token", "bearer")]
    [InlineData("--secret", "my-config-value")]
    [InlineData("--auth", "oauth2")]
    public void Allows_GenericSecretNamesWithOrdinaryValues(string option, string value) {
        // 参数名像密钥、但值是普通业务值 —— 放行，否则这些工具永远调不通
        Check("tool_call", "read", option, value).Should().BeNull();
    }

    [Theory]
    [InlineData("--token", "sk-abcdefghijklmnopqrstuvwxyz")]
    [InlineData("--key", "ghp_abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("--some_option", "AKIAIOSFODNN7EXAMPLE")]
    [InlineData("--another", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9")]
    public void Rejects_SecretShapedValuesRegardlessOfOptionName(string option, string value) {
        Check("mcp_call", "tool", option, value).Should().NotBeNull();
    }

    [Fact]
    public void Rejects_OpaqueSecretUnderGenericName() {
        // 自建服务的不透明 token 没有厂商前缀，靠「够长 + 只含 token 字符集」识别
        Check("mcp_call", "tool", "--token", "aB3dE5fG7hI9jK1lM2nO4pQ6rS8tU0").Should().NotBeNull();
    }

    [Fact]
    public void Rejects_EqualsFormWithSecretValue() {
        // `--api-key=sk-xxx` 这种写法此前漏检：旧实现只看「下一个 token」的值
        Check("mcp_call", "tool", "--api-key=sk-abcdefghijklmnop").Should().NotBeNull();
    }

    [Fact]
    public void Allows_EqualsFormWithOrdinaryValue() {
        Check("tool_call", "read", "--key=theme").Should().BeNull();
    }

    [Fact]
    public void Allows_McpServeToken() {
        // ADR 0138：mcp serve --token 是本地服务口令，不是供应商 API Key
        Check("mcp_serve", "--token", "local-portal-token").Should().BeNull();
    }

    [Theory]
    [InlineData("--file_path", "/tmp/a.txt")]
    [InlineData("--path", "relative/dir/some-file.cs")]
    [InlineData("--pattern", "**/*.cs")]
    public void Allows_OrdinaryParameters(string option, string value) {
        Check("tool_call", "read", option, value).Should().BeNull();
    }

    [Fact]
    public void Rejects_ExplicitSecretOptionWithoutValue() {
        // 名字本身就带密钥语义 —— 就算没跟值也要拦
        Check("mcp_call", "tool", "--api-key").Should().NotBeNull();
    }

    private static string? Check(params string[] args) => ApiKeyRedLine.CheckArgsForSecrets(args);
}
