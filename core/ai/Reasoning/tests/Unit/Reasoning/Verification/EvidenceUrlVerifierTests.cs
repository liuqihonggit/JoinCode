namespace JoinCode.Reasoning.Tests.Verification;

public sealed class EvidenceUrlVerifierTests
{
    [Fact]
    public async Task VerifyAsync_WhenSourceUrlIsNull_ReturnsValidResult()
    {
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance);
        var evidence = new EvidenceRecord
        {
            Content = "无URL证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.True(result.IsValid);
        Assert.True(result.IsAccessible);
        Assert.True(result.ContainsExpectedText);
        Assert.Equal(string.Empty, result.Url);
    }

    [Fact]
    public async Task VerifyAsync_WhenSourceUrlIsEmpty_ReturnsValidResult()
    {
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance);
        var evidence = new EvidenceRecord
        {
            Content = "空URL证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = string.Empty,
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.True(result.IsValid);
        Assert.True(result.IsAccessible);
        Assert.True(result.ContainsExpectedText);
    }

    [Fact]
    public async Task VerifyAsync_WhenResponseIsFailure_ReturnsInvalidResult()
    {
        var handler = new TestHttpMessageHandler("content", HttpStatusCode.NotFound);
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.False(result.IsValid);
        Assert.False(result.IsAccessible);
        Assert.False(result.ContainsExpectedText);
        Assert.Contains("NotFound", result.Error);
    }

    [Fact]
    public async Task VerifyAsync_WhenContentMatchesExtractedText_ReturnsValidResult()
    {
        var handler = new TestHttpMessageHandler("line1\nexpected text here\nline3", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
            ExtractedText = "expected text",
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.True(result.IsValid);
        Assert.True(result.IsAccessible);
        Assert.True(result.ContainsExpectedText);
        Assert.Equal(2, result.FoundAtLine);
        Assert.Equal("expected text here", result.ExtractedText);
        Assert.NotNull(result.VerificationTime);
    }

    [Fact]
    public async Task VerifyAsync_WhenExtractedTextNotFound_ReturnsInvalidResult()
    {
        var handler = new TestHttpMessageHandler("line1\nline2", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
            ExtractedText = "missing text",
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.False(result.IsValid);
        Assert.True(result.IsAccessible);
        Assert.False(result.ContainsExpectedText);
        Assert.Null(result.FoundAtLine);
    }

    [Fact]
    public async Task VerifyAsync_WhenNoExtractedText_ReturnsValidResult()
    {
        var handler = new TestHttpMessageHandler("any content", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.True(result.IsValid);
        Assert.True(result.IsAccessible);
        Assert.False(result.ContainsExpectedText);
        Assert.NotNull(result.VerificationTime);
    }

    [Fact]
    public async Task VerifyAsync_WhenRequestTimesOut_ReturnsTimeoutResult()
    {
        var handler = new TestHttpMessageHandler("content", HttpStatusCode.OK) { Delay = TimeSpan.FromSeconds(2) };
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.False(result.IsValid);
        Assert.False(result.IsAccessible);
        Assert.True(result.IsTimeout);
        Assert.Equal("连接超时", result.Error);
    }

    [Fact]
    public async Task VerifyAsync_WhenRequestThrows_ReturnsInvalidResultWithError()
    {
        var handler = new TestHttpMessageHandler("content", HttpStatusCode.OK) { ThrowException = new HttpRequestException("network error") };
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
        };

        var result = await verifier.VerifyAsync(evidence);

        Assert.False(result.IsValid);
        Assert.False(result.IsAccessible);
        Assert.Contains("network error", result.Error);
    }

    [Fact]
    public async Task VerifyAllAsync_SkipsAlreadyVerifiedAndEmptyUrls()
    {
        var handler = new TestHttpMessageHandler("content", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidences = new List<EvidenceRecord>
        {
            new()
            {
                Content = "空URL",
                Category = EvidenceCategory.Documentary,
                SubmittedBy = AgentRole.Prosecutor,
                SourceUrl = null,
            },
            new()
            {
                Content = "已验证",
                Category = EvidenceCategory.Documentary,
                SubmittedBy = AgentRole.Prosecutor,
                SourceUrl = "https://example.com/doc",
                IsUrlVerified = true,
            },
        };

        var results = await verifier.VerifyAllAsync(evidences);

        Assert.Empty(results);
    }

    [Fact]
    public async Task VerifyAllAsync_ProcessesUnverifiedUrls()
    {
        var handler = new TestHttpMessageHandler("content", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        var evidences = new List<EvidenceRecord>
        {
            new()
            {
                Content = "待验证",
                Category = EvidenceCategory.Documentary,
                SubmittedBy = AgentRole.Prosecutor,
                SourceUrl = "https://example.com/doc",
            },
        };

        var results = await verifier.VerifyAllAsync(evidences);

        Assert.Single(results);
        Assert.True(results[0].IsValid);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _content;
        private readonly HttpStatusCode _statusCode;

        public TimeSpan Delay { get; init; } = TimeSpan.Zero;
        public Exception? ThrowException { get; init; }

        public TestHttpMessageHandler(string content, HttpStatusCode statusCode)
        {
            _content = content;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ThrowException is not null)
            {
                throw ThrowException;
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content),
            };
        }
    }
}
