namespace Hands.Tests.Network;

public sealed class RemoteTriggerServiceTests
{
    private readonly FakeHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly RemoteTriggerService _service;

    public RemoteTriggerServiceTests()
    {
        _handler = new FakeHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:9999") };
        _service = new RemoteTriggerService(_httpClient);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutEndpoint_ReturnsUnauthorized()
    {
        var result = await _service.ExecuteAsync(TriggerAction.List).ConfigureAwait(true);

        result.Status.Should().Be(401);
        result.Json.Should().Contain("未配置 JCC API 端点");
    }

    [Fact]
    public async Task ExecuteAsync_List_SendsGetRequest()
    {
        _handler.SetResponse(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"triggers":[]}""")
        });
        Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), "http://localhost:9999");
        try
        {
            var result = await _service.ExecuteAsync(TriggerAction.List).ConfigureAwait(true);

            result.Status.Should().Be(200);
            result.Json.Should().Be("""{"triggers":[]}""");
            _handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
            _handler.LastRequest.RequestUri.Should().Be(new Uri("http://localhost:9999/v1/code/triggers"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Create_SendsPostRequest()
    {
        _handler.SetResponse(request => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"123"}""")
        });
        Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), "http://localhost:9999/");
        try
        {
            var result = await _service.ExecuteAsync(TriggerAction.Create, body: """{"name":"x"}""").ConfigureAwait(true);

            result.Status.Should().Be(201);
            _handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
            _handler.LastRequest.RequestUri.Should().Be(new Uri("http://localhost:9999/v1/code/triggers"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithTrailingSlash_TrimsSlash()
    {
        _handler.SetResponse(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{}""")
        });
        Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), "http://localhost:9999/");
        try
        {
            await _service.ExecuteAsync(TriggerAction.List).ConfigureAwait(true);

            _handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Be("http://localhost:9999/v1/code/triggers");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithApiKey_AddsAuthorizationHeader()
    {
        _handler.SetResponse(request => new HttpResponseMessage(HttpStatusCode.OK));
        Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), "http://localhost:9999");
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(c => c.GetAsync("api.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync("sk-test");
        var service = new RemoteTriggerService(_httpClient, configMock.Object);
        try
        {
            await service.ExecuteAsync(TriggerAction.List).ConfigureAwait(true);

            _handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
            _handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
            _handler.LastRequest.Headers.Authorization.Parameter.Should().Be("sk-test");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_HttpError_Returns500WithMessage()
    {
        _handler.SetResponse(request => throw new HttpRequestException("connection refused"));
        Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), "http://localhost:9999");
        try
        {
            var result = await _service.ExecuteAsync(TriggerAction.List).ConfigureAwait(true);

            result.Status.Should().Be(500);
            result.Json.Should().Contain("connection refused");
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Endpoint.ToValue(), null);
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;

        public HttpRequestMessage? LastRequest { get; private set; }

        public void SetResponse(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _responseFactory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = _responseFactory?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            return Task.FromResult(response);
        }
    }
}
