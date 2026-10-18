namespace Hands.Tests.Handlers;

/// <summary>
/// ApiLoggingHandler.ReadContentAsync charset 解码测试（改动 P4）。
/// <para>ReadContentAsync 是 private static 方法，用反射测试：
/// 从 content.Headers.ContentType?.CharSet 解析编码，不支持回退 UTF-8。</para>
/// <para>注：.NET 10 默认仅支持 UTF/ASCII/Latin1，GBK 等需 CodePagesEncodingProvider。
/// 测试用条件断言：编码可用时验证对应解码，不可用时验证回退 UTF-8。</para>
/// </summary>
public sealed class ApiLoggingHandlerTests {
    private static readonly MethodInfo _readContentMethod =
        typeof(ApiLoggingHandler).GetMethod("ReadContentAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("未找到 ApiLoggingHandler.ReadContentAsync 私有方法");

    /// <summary>
    /// 调用 private static async ReadContentAsync(HttpContent?, int) 方法。
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "反射 Invoke 返回 object? 需强制转换为 Task<string?>,边界可空抑制")]
    private static async Task<string?> InvokeReadContentAsync(HttpContent? content, int maxLength) {
        var task = (Task<string?>)_readContentMethod.Invoke(null, [content, maxLength])!;
        return await task.ConfigureAwait(true);
    }

    /// <summary>
    /// 判断指定 charset 在当前运行时是否可用。
    /// </summary>
    private static bool IsEncodingAvailable(string charset) {
        try { Encoding.GetEncoding(charset); return true; } catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// content 为 null 时应返回 null。
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_null内容_应返回null() {
        var result = await InvokeReadContentAsync(null, 1000).ConfigureAwait(true);
        result.Should().BeNull();
    }

    /// <summary>
    /// 带 utf-8 charset 的 HttpContent 应正确解码中文内容。
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_utf8charset_应正确解码中文() {
        const string text = "中文日志内容测试";
        var bytes = Encoding.UTF8.GetBytes(text);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };

        var result = await InvokeReadContentAsync(content, 1000).ConfigureAwait(true);

        result.Should().Be(text);
    }

    /// <summary>
    /// 带 gbk charset 的 HttpContent：若运行时支持 GBK 则正确解码 GBK 中文，否则回退 UTF-8 解码。
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_gbkCharset_支持时正确解码_否则回退Utf8() {
        const string text = "GBK编码的中文内容";
        if (IsEncodingAvailable("gbk")) {
            var gbk = Encoding.GetEncoding("gbk");
            var bytes = gbk.GetBytes(text);
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "gbk" };
            var result = await InvokeReadContentAsync(content, 1000).ConfigureAwait(true);
            result.Should().Be(text);
        } else {
            // GBK 不可用时回退 UTF-8：用 UTF-8 编码的内容验证回退解码正确
            var bytes = Encoding.UTF8.GetBytes(text);
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "gbk" };
            var result = await InvokeReadContentAsync(content, 1000).ConfigureAwait(true);
            result.Should().Be(text);
        }
    }

    /// <summary>
    /// 带不支持的 charset 时应回退 UTF-8 解码。
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_不支持的charset_应回退Utf8() {
        const string text = "fallback content";
        var bytes = Encoding.UTF8.GetBytes(text);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "nonexistent-xyz" };

        var result = await InvokeReadContentAsync(content, 1000).ConfigureAwait(true);

        result.Should().Be(text);
    }

    /// <summary>
    /// 无 charset 的 HttpContent 应默认用 UTF-8 解码。
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_无charset_应默认Utf8解码() {
        const string text = "no charset content 中文";
        var bytes = Encoding.UTF8.GetBytes(text);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        var result = await InvokeReadContentAsync(content, 1000).ConfigureAwait(true);

        result.Should().Be(text);
    }

    /// <summary>
    /// 内容长度超过 maxLength 时应截断并附加 "... (truncated)"。
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "已断言 NotNull 后的 null-forgiving 抑制,边界可空")]
    [Fact]
    public async Task ReadContentAsync_超过最大长度_应截断并附加标记() {
        var text = new string('A', 200);
        var bytes = Encoding.UTF8.GetBytes(text);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };

        var result = await InvokeReadContentAsync(content, 50).ConfigureAwait(true);

        result.Should().NotBeNull();
        result!.Should().StartWith(new string('A', 50));
        result.Should().EndWith("... (truncated)");
    }

    /// <summary>
    /// 带 iso-8859-1 charset 的 HttpContent：若运行时支持则正确解码，否则回退 UTF-8。
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_iso88591Charset_支持时正确解码_否则回退Utf8() {
        if (IsEncodingAvailable("iso-8859-1")) {
            // ISO-8859-1 中 0xE9 = é
            var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0xE9 };
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "iso-8859-1" };
            var result = await InvokeReadContentAsync(content, 1000).ConfigureAwait(true);
            result.Should().Be("Hello é");
        } else {
            // ISO-8859-1 不可用时回退 UTF-8：用 UTF-8 编码的内容验证回退解码正确
            const string text = "iso fallback test";
            var bytes = Encoding.UTF8.GetBytes(text);
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "iso-8859-1" };
            var result = await InvokeReadContentAsync(content, 1000).ConfigureAwait(true);
            result.Should().Be(text);
        }
    }
}
