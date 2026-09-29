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

    // === BuildRequestLine: 请求行构造 ===

    /// <summary>GET 方法 + 路径生成 "GET /path HTTP/1.1\r\n"。</summary>
    [Fact]
    public void BuildRequestLine_GetWithPath_ReturnsValidRequestLine() {
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Get, "/path")
            .Should().Be("GET /path HTTP/1.1\r\n");
    }

    /// <summary>POST 方法 + 路径查询生成 "POST /path?x=1 HTTP/1.1\r\n"。</summary>
    [Fact]
    public void BuildRequestLine_PostWithQuery_ReturnsValidRequestLine() {
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Post, "/path?x=1")
            .Should().Be("POST /path?x=1 HTTP/1.1\r\n");
    }

    /// <summary>pathAndQuery 为 null 时回退到 "/"。</summary>
    [Fact]
    public void BuildRequestLine_NullPath_DefaultsToSlash() {
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Get, null)
            .Should().Be("GET / HTTP/1.1\r\n");
    }

    /// <summary>pathAndQuery 为空字符串时回退到 "/"。</summary>
    [Fact]
    public void BuildRequestLine_EmptyPath_DefaultsToSlash() {
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Get, string.Empty)
            .Should().Be("GET / HTTP/1.1\r\n");
    }

    /// <summary>自定义 HttpMethod（如 PATCH）正确序列化。</summary>
    [Fact]
    public void BuildRequestLine_CustomMethod_SerializesMethodName() {
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Patch, "/resource")
            .Should().Be("PATCH /resource HTTP/1.1\r\n");
    }

    /// <summary>根路径 "/" 保留。</summary>
    [Fact]
    public void BuildRequestLine_RootPath_Preserved() {
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Get, "/")
            .Should().Be("GET / HTTP/1.1\r\n");
    }

    /// <summary>请求行始终以 \r\n 结尾。</summary>
    [Fact]
    public void BuildRequestLine_AlwaysEndsWithCrlf() {
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Get, "/x").Should().EndWith("\r\n");
        HttpRequestSerializer.BuildRequestLine(HttpMethod.Post, null).Should().EndWith("\r\n");
    }

    // === AppendHeaderLine: 单行头追加 ===

    /// <summary>追加 "Key: Value\r\n" 到 builder。</summary>
    [Fact]
    public void AppendHeaderLine_ValidKeyvalue_AppendsFormattedLine() {
        var builder = new StringBuilder();

        HttpRequestSerializer.AppendHeaderLine(builder, "Authorization", "Bearer token");

        builder.ToString().Should().Be("Authorization: Bearer token\r\n");
    }

    /// <summary>多次追加累积多行。</summary>
    [Fact]
    public void AppendHeaderLine_MultipleCalls_AccumulatesLines() {
        var builder = new StringBuilder();

        HttpRequestSerializer.AppendHeaderLine(builder, "X-A", "1");
        HttpRequestSerializer.AppendHeaderLine(builder, "X-B", "2");

        builder.ToString().Should().Be("X-A: 1\r\nX-B: 2\r\n");
    }

    /// <summary>空值头追加为 "Key: \r\n"。</summary>
    [Fact]
    public void AppendHeaderLine_EmptyValue_AppendsKeyWithEmptyValue() {
        var builder = new StringBuilder();

        HttpRequestSerializer.AppendHeaderLine(builder, "X-Empty", string.Empty);

        builder.ToString().Should().Be("X-Empty: \r\n");
    }

    /// <summary>含特殊字符的值原样追加（不转义）。</summary>
    [Fact]
    public void AppendHeaderLine_SpecialChars_PreservedAsIs() {
        var builder = new StringBuilder();

        HttpRequestSerializer.AppendHeaderLine(builder, "X-Path", "/a/b/c?d=e&f=g");

        builder.ToString().Should().Be("X-Path: /a/b/c?d=e&f=g\r\n");
    }

    // === AppendHeaders: 批量头追加 ===

    /// <summary>多值头每个值生成一行。</summary>
    [Fact]
    public void AppendHeaders_MultiValueHeader_EmitsOneLinePerValue() {
        var builder = new StringBuilder();
        using var req = new HttpRequestMessage();
        req.Headers.Add("X-Multi", new[] { "v1", "v2", "v3" });

        HttpRequestSerializer.AppendHeaders(builder, req.Headers);

        builder.ToString().Should().Be("X-Multi: v1\r\nX-Multi: v2\r\nX-Multi: v3\r\n");
    }

    /// <summary>多个不同头按 Headers 枚举顺序追加。</summary>
    [Fact]
    public void AppendHeaders_MultipleHeaders_AllEmitted() {
        var builder = new StringBuilder();
        using var req = new HttpRequestMessage();
        req.Headers.Add("Authorization", "Bearer t");
        req.Headers.Add("X-Trace", "abc");

        HttpRequestSerializer.AppendHeaders(builder, req.Headers);

        var result = builder.ToString();
        result.Should().Contain("Authorization: Bearer t\r\n");
        result.Should().Contain("X-Trace: abc\r\n");
    }

    /// <summary>空 Headers 集合追加空字符串。</summary>
    [Fact]
    public void AppendHeaders_EmptyHeaders_AppendsNothing() {
        var builder = new StringBuilder();
        using var req = new HttpRequestMessage();

        HttpRequestSerializer.AppendHeaders(builder, req.Headers);

        builder.ToString().Should().BeEmpty();
    }

    /// <summary>Content.Headers 也可通过 AppendHeaders 序列化（Content-Type 头）。</summary>
    /// <remarks>StringContent 的 Content-Length 头由 HttpClient 在发送时计算，构造时枚举 Headers 不包含它。</remarks>
    [Fact]
    public void AppendHeaders_ContentHeaders_EmitsContentHeaderLines() {
        var builder = new StringBuilder();
        using var req = new HttpRequestMessage {
            Content = new StringContent("body", Encoding.UTF8, "application/json"),
        };

        HttpRequestSerializer.AppendHeaders(builder, req.Content!.Headers);

        var result = builder.ToString();
        result.Should().Contain("Content-Type: application/json");
    }
}
