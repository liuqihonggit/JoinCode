namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// HttpRequestSerializer 确定性测试 — 构造 HttpRequestMessage 序列化、字符串反序列化、internal 解析方法。
/// </summary>
public class HttpRequestSerializerTest {
    /// <summary>序列化请求行格式为 METHOD /path?query HTTP/1.1。</summary>
    [Fact]
    public async Task SerializeAsync_RequestLine_ContainsMethodAndPath() {
        using var req = new HttpRequestMessage(HttpMethod.Post, "http://example.com/path?x=1");

        var serialized = await HttpRequestSerializer.SerializeAsync(req);

        serialized.Should().StartWith("POST /path?x=1 HTTP/1.1\r\n");
    }

    /// <summary>请求头被序列化为 Key: Value 行。</summary>
    [Fact]
    public async Task SerializeAsync_Headers_EmittedAsLines() {
        using var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        req.Headers.Add("Authorization", "Bearer token");
        req.Headers.Add("X-Custom", "v1");

        var serialized = await HttpRequestSerializer.SerializeAsync(req);

        serialized.Should().Contain("Authorization: Bearer token\r\n");
        serialized.Should().Contain("X-Custom: v1\r\n");
    }

    /// <summary>带 StringContent 的请求序列化包含内容头与请求体。</summary>
    [Fact]
    public async Task SerializeAsync_WithContent_EmitsContentHeadersAndBody() {
        using var req = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit") {
            Content = new StringContent("{\"k\":1}", Encoding.UTF8, "application/json"),
        };

        var serialized = await HttpRequestSerializer.SerializeAsync(req);

        serialized.Should().Contain("Content-Type: application/json");
        serialized.Should().EndWith("{\"k\":1}");
    }

    /// <summary>无 RequestUri 时路径默认为 /。</summary>
    [Fact]
    public async Task SerializeAsync_NoUri_PathDefaultsToSlash() {
        using var req = new HttpRequestMessage { Method = HttpMethod.Get };

        var serialized = await HttpRequestSerializer.SerializeAsync(req);

        serialized.Should().StartWith("GET / HTTP/1.1\r\n");
    }

    /// <summary>Deserialize 解析状态行、响应头与响应体。</summary>
    /// <remarks>
    /// 实现先用 new StringContent(body) 创建内容（默认 Content-Type=text/plain; charset=utf-8），
    /// 再 TryAddWithoutValidation 追加内容头 — Content-Type 为多值头，追加而非覆盖，
    /// 最终 Content-Type 含 [text/plain; charset=utf-8, application/json]。
    /// </remarks>
    [Fact]
    public void Deserialize_FullResponse_ParsesStatusHeadersBody() {
        var text = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nX-Trace: abc\r\n\r\n{\"ok\":true}";

        var resp = HttpRequestSerializer.Deserialize(text);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.ReasonPhrase.Should().Be("OK");
        resp.Headers.GetValues("X-Trace").Should().Equal("abc");
        resp.Content.Should().NotBeNull();
        // StringContent 默认 text/plain; charset=utf-8，TryAddWithoutValidation 追加 application/json
        resp.Content!.Headers.ContentType!.MediaType.Should().Be("text/plain");
        resp.Content.Headers.TryGetValues("Content-Type", out var ctValues).Should().BeTrue();
        ctValues.Should().Contain("application/json");
    }

    /// <summary>状态行无 ReasonPhrase 时仅解析状态码，ReasonPhrase 取 StatusCode 默认值。</summary>
    /// <remarks>HttpResponseMessage.StatusCode setter 会自动填充默认原因短语（如 204 → "No Content"）。</remarks>
    [Fact]
    public void Deserialize_StatusLineWithoutReason_ParsesStatusCodeOnly() {
        var text = "HTTP/1.1 204\r\n\r\n";

        var resp = HttpRequestSerializer.Deserialize(text);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        // StatusCode setter 自动填充 204 的默认原因短语
        resp.ReasonPhrase.Should().Be("No Content");
    }

    /// <summary>空状态行抛 InvalidOperationException。</summary>
    [Fact]
    public void Deserialize_EmptyStatusLine_Throws() {
        var act = () => HttpRequestSerializer.Deserialize("\r\n");

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>ParseStatusLine 解析状态码与原因短语。</summary>
    [Fact]
    public void ParseStatusLine_ValidLine_SetsStatusCodeAndReason() {
        var resp = new HttpResponseMessage();

        HttpRequestSerializer.ParseStatusLine("HTTP/1.1 404 Not Found", resp);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resp.ReasonPhrase.Should().Be("Not Found");
    }

    /// <summary>ParseStatusLine 部分不足两段时抛异常。</summary>
    [Fact]
    public void ParseStatusLine_TooFewParts_Throws() {
        var resp = new HttpResponseMessage();

        var act = () => HttpRequestSerializer.ParseStatusLine("HTTP/1.1", resp);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>ParseStatusLine 非数字状态码抛异常。</summary>
    [Fact]
    public void ParseStatusLine_NonNumericStatusCode_Throws() {
        var resp = new HttpResponseMessage();

        var act = () => HttpRequestSerializer.ParseStatusLine("HTTP/1.1 ABC Reason", resp);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>ParseHeaderLine 内容头进入 contentHeaders 字典。</summary>
    [Fact]
    public void ParseHeaderLine_ContentHeader_AddedToContentHeaders() {
        var resp = new HttpResponseMessage();
        var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        HttpRequestSerializer.ParseHeaderLine("Content-Type: application/json", resp, contentHeaders);

        contentHeaders.Should().ContainKey("Content-Type");
        contentHeaders["Content-Type"].Should().Be("application/json");
    }

    /// <summary>ParseHeaderLine 非内容头进入 response.Headers。</summary>
    [Fact]
    public void ParseHeaderLine_NonContentHeader_AddedToResponseHeaders() {
        var resp = new HttpResponseMessage();
        var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        HttpRequestSerializer.ParseHeaderLine("Authorization: Bearer x", resp, contentHeaders);

        contentHeaders.Should().BeEmpty();
        resp.Headers.GetValues("Authorization").Should().Equal("Bearer x");
    }

    /// <summary>ParseHeaderLine 无分隔符时被忽略（不抛异常）。</summary>
    [Fact]
    public void ParseHeaderLine_NoSeparator_Ignored() {
        var resp = new HttpResponseMessage();
        var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var act = () => HttpRequestSerializer.ParseHeaderLine("malformed-line", resp, contentHeaders);

        act.Should().NotThrow();
        contentHeaders.Should().BeEmpty();
    }

    /// <summary>IsContentHeader 对已知内容头返回 true。</summary>
    [Fact]
    public void IsContentHeader_KnownContentHeaders_ReturnsTrue() {
        HttpRequestSerializer.IsContentHeader("Content-Type").Should().BeTrue();
        HttpRequestSerializer.IsContentHeader("Content-Length").Should().BeTrue();
        HttpRequestSerializer.IsContentHeader("Expires").Should().BeTrue();
    }

    /// <summary>IsContentHeader 对非内容头返回 false。</summary>
    [Fact]
    public void IsContentHeader_NonContentHeader_ReturnsFalse() {
        HttpRequestSerializer.IsContentHeader("Authorization").Should().BeFalse();
        HttpRequestSerializer.IsContentHeader("X-Custom").Should().BeFalse();
    }

    /// <summary>IsContentHeader 大小写不敏感。</summary>
    [Fact]
    public void IsContentHeader_CaseInsensitive_ReturnsTrue() {
        HttpRequestSerializer.IsContentHeader("content-type").Should().BeTrue();
        HttpRequestSerializer.IsContentHeader("CONTENT-TYPE").Should().BeTrue();
    }
}
