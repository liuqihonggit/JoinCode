
namespace Sync.Tests.Scheduling.Tasks;

public class RemoteAgentTaskExecutorTests
{
    private readonly FakeHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly RemoteAgentTaskExecutor _executor;

    public RemoteAgentTaskExecutorTests()
    {
        _handler = new FakeHttpMessageHandler();
        _httpClient = new HttpClient(_handler);
        _executor = new RemoteAgentTaskExecutor(_httpClient, NullLogger<RemoteAgentTaskExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteRemoteAsync_SuccessfulResponse_ShouldReturnSuccessResult()
    {
        var responseJson = """{"success":true,"output":"task completed"}""";
        _handler.SetResponse(System.Net.HttpStatusCode.OK, responseJson);

        var definition = new RemoteAgentTaskDefinition
        {
            TaskId = "task-001",
            Endpoint = "http://localhost:8080",
            TaskDescription = "Run analysis",
            MaxRetries = 0
        };

        var result = await _executor.ExecuteRemoteAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.TaskId.Should().Be("task-001");
        result.AgentId.Should().Be("remote");
        result.Output.Should().Be("task completed");
    }

    [Fact]
    public async Task ExecuteRemoteAsync_FailedResponse_ShouldReturnFailureResult()
    {
        var responseJson = """{"success":false,"error":"something went wrong"}""";
        _handler.SetResponse(System.Net.HttpStatusCode.OK, responseJson);

        var definition = new RemoteAgentTaskDefinition
        {
            TaskId = "task-002",
            Endpoint = "http://localhost:8080",
            TaskDescription = "Run analysis",
            MaxRetries = 0
        };

        var result = await _executor.ExecuteRemoteAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeFalse();
        result.TaskId.Should().Be("task-002");
        result.Error.Should().Be("something went wrong");
    }

    [Fact]
    public async Task ExecuteRemoteAsync_HttpError_ShouldReturnFailureResult()
    {
        _handler.SetResponse(System.Net.HttpStatusCode.InternalServerError, "");

        var definition = new RemoteAgentTaskDefinition
        {
            TaskId = "task-003",
            Endpoint = "http://localhost:8080",
            TaskDescription = "Run analysis",
            MaxRetries = 0
        };

        var result = await _executor.ExecuteRemoteAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeFalse();
        result.TaskId.Should().Be("task-003");
    }

    [Fact]
    public async Task ExecuteRemoteAsync_NullDefinition_ShouldThrowArgumentNullException()
    {
        var act = () => _executor.ExecuteRemoteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task IsRemoteAvailableAsync_HealthyEndpoint_ShouldReturnTrue()
    {
        _handler.SetResponse(System.Net.HttpStatusCode.OK, "healthy");

        var result = await _executor.IsRemoteAvailableAsync("http://localhost:8080").ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRemoteAvailableAsync_UnhealthyEndpoint_ShouldReturnFalse()
    {
        _handler.SetResponse(System.Net.HttpStatusCode.ServiceUnavailable, "");

        var result = await _executor.IsRemoteAvailableAsync("http://localhost:8080").ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRemoteAvailableAsync_NetworkError_ShouldReturnFalse()
    {
        _handler.SetException(new HttpRequestException("Connection refused"));

        var result = await _executor.IsRemoteAvailableAsync("http://localhost:8080").ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelRemoteAsync_ShouldExecuteWithoutError()
    {
        _handler.SetResponse(System.Net.HttpStatusCode.OK, "");

        var act = () => _executor.CancelRemoteAsync("task-001");

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CancelRemoteAsync_HttpError_ShouldNotThrow()
    {
        _handler.SetException(new HttpRequestException("Connection refused"));

        var act = () => _executor.CancelRemoteAsync("task-001");

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private System.Net.HttpStatusCode _statusCode = System.Net.HttpStatusCode.OK;
        private string _content = "";
        private Exception? _exception;

        public void SetResponse(System.Net.HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
            _exception = null;
        }

        public void SetException(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                return Task.FromException<HttpResponseMessage>(_exception);
            }

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
