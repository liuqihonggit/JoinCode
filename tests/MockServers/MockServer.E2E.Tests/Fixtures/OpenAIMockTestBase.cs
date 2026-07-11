
namespace MockServer.E2E.Tests.Fixtures;

/// <summary>
/// JoinCode.Tests 测试基类
/// 提供测试生命周期管理和常用断言方法
/// </summary>
[Collection(nameof(PipeTestCollection))]
public abstract class OpenAIMockTestBase : IAsyncLifetime
{
    private readonly PipeMockServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private StdioProcessManager? _cliProcess;
    private PipeJoinCodeProcessFactory? _processFactory;

    /// <summary>
    /// Mock Server 实例
    /// </summary>
    protected PipeOpenAIMockServer MockServer => _fixture.MockServer;

    /// <summary>
    /// 请求记录器
    /// </summary>
    protected RequestRecorder RequestRecorder => _fixture.RequestRecorder;

    /// <summary>
    /// 测试输出助手
    /// </summary>
    protected ITestOutputHelper Output => _output;

    /// <summary>
    /// Logger 工厂
    /// </summary>
    protected ILoggerFactory TestLoggerFactory => _loggerFactory;

    /// <summary>
    /// JoinCode 进程是否正在运行
    /// </summary>
    protected bool IsCliRunning => _cliProcess?.IsRunning ?? false;

    protected OpenAIMockTestBase(PipeMockServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        _output = output ?? throw new ArgumentNullException(nameof(output));

        _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// 测试初始化 - 清理请求记录
    /// </summary>
    public virtual Task InitializeAsync()
    {
        _output.WriteLine($"[{nameof(OpenAIMockTestBase)}] 测试初始化 - 清理请求记录");
        RequestRecorder.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 测试释放 - 停止 JoinCode 进程
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        _output.WriteLine($"[{nameof(OpenAIMockTestBase)}] 测试释放 - 停止 JoinCode 进程");

        if (_cliProcess != null)
        {
            await _cliProcess.StopAsync().ConfigureAwait(true);
            await _cliProcess.DisposeAsync().ConfigureAwait(true);
            _cliProcess = null;
        }

        _loggerFactory.Dispose();
    }

    /// <summary>
    /// 启动 JoinCode 进程
    /// </summary>
    /// <param name="executablePath">JoinCode 可执行文件路径</param>
    /// <param name="additionalArgs">额外参数</param>
    /// <param name="ct">取消令牌</param>
    protected async Task StartJoinCodeAsync(
        string executablePath,
        string? additionalArgs = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);

        _output.WriteLine($"[{nameof(OpenAIMockTestBase)}] 启动 JoinCode 进程: {executablePath}");

        // 确保之前的进程已停止
        if (_cliProcess != null)
        {
            await _cliProcess.StopAsync().ConfigureAwait(true);
            await _cliProcess.DisposeAsync().ConfigureAwait(true);
        }

        _processFactory ??= new PipeJoinCodeProcessFactory(
            _loggerFactory.CreateLogger<PipeJoinCodeProcessFactory>());

        var pipeName = _fixture.GetPipeName();
        var apiKey = _fixture.GetApiKey();

        var process = await _processFactory.CreateAsync(
            pipeName,
            apiKey,
            executablePath,
            additionalArgs,
            ct).ConfigureAwait(true);

        _cliProcess = new StdioProcessManager(
            _loggerFactory.CreateLogger<StdioProcessManager>());

        var config = new StdioProcessConfig
        {
            ExecutablePath = executablePath,
            Arguments = BuildArguments(pipeName, additionalArgs),
            EnvironmentVariables = BuildEnvironmentVariables(pipeName, apiKey)
        };

        await _cliProcess.StartAsync(config, ct).ConfigureAwait(true);

        _output.WriteLine($"[{nameof(OpenAIMockTestBase)}] JoinCode 进程已启动");
    }

    /// <summary>
    /// 向 JoinCode 发送输入
    /// </summary>
    /// <param name="input">输入内容</param>
    /// <param name="ct">取消令牌</param>
    protected async Task SendInputAsync(string input, CancellationToken ct = default)
    {
        if (_cliProcess == null)
        {
            throw new InvalidOperationException("JoinCode 进程未启动。请先调用 StartJoinCodeAsync。");
        }

        _output.WriteLine($"[{nameof(OpenAIMockTestBase)}] 发送输入: {input}");
        await _cliProcess.SendAsync(input, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// 等待响应
    /// </summary>
    /// <param name="predicate">响应条件</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>匹配的输出内容</returns>
    protected async Task<string> WaitForResponseAsync(
        Func<string, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (_cliProcess == null)
        {
            throw new InvalidOperationException("JoinCode 进程未启动。请先调用 StartJoinCodeAsync。");
        }

        timeout ??= TimeSpan.FromSeconds(30);
        _output.WriteLine($"[{nameof(OpenAIMockTestBase)}] 等待响应 (超时: {timeout.Value.TotalSeconds}s)");

        var response = await _cliProcess.WaitForOutputAsync(predicate, timeout, ct).ConfigureAwait(true);

        _output.WriteLine($"[{nameof(OpenAIMockTestBase)}] 收到响应: {response[..Math.Min(200, response.Length)]}...");

        return response;
    }

    /// <summary>
    /// 等待包含特定文本的响应
    /// </summary>
    /// <param name="text">期望文本</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="ct">取消令牌</param>
    protected async Task<string> WaitForResponseContainingAsync(
        string text,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        return await WaitForResponseAsync(
            output => output.Contains(text, StringComparison.OrdinalIgnoreCase),
            timeout,
            ct).ConfigureAwait(true);
    }

    /// <summary>
    /// 断言请求记录包含特定内容
    /// </summary>
    /// <param name="expectedContent">期望内容</param>
    /// <param name="message">断言消息</param>
    protected void AssertRequestContains(string expectedContent, string? message = null)
    {
        var requests = RequestRecorder.GetRequests();
        var found = requests.Any(r => r.Body.Contains(expectedContent, StringComparison.OrdinalIgnoreCase));

        if (!found)
        {
            var actualRequests = string.Join(", ", requests.Select(r => r.Body[..Math.Min(100, r.Body.Length)]));
            Assert.Fail(message ?? $"期望请求包含 '{expectedContent}'，但未找到。实际请求: {actualRequests}");
        }
    }

    /// <summary>
    /// 断言请求记录包含特定角色消息
    /// </summary>
    /// <param name="role">角色 (system/user/assistant)</param>
    /// <param name="content">期望内容</param>
    protected void AssertRequestContainsRoleMessage(string role, string content)
    {
        var requests = RequestRecorder.GetRequests();
        var found = requests.Any(r =>
            r.ParsedRequest?.Messages.Any(m =>
                m.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                m.Content.Contains(content, StringComparison.OrdinalIgnoreCase)) == true);

        if (!found)
        {
            Assert.Fail($"期望请求包含 {role} 角色的消息 '{content}'，但未找到。");
        }
    }

    /// <summary>
    /// 断言请求数量
    /// </summary>
    /// <param name="expectedCount">期望数量</param>
    protected void AssertRequestCount(int expectedCount)
    {
        var actualCount = RequestRecorder.Count;
        Assert.Equal(expectedCount, actualCount);
    }

    /// <summary>
    /// 获取当前 JoinCode 输出
    /// </summary>
    protected async Task<string> GetCurrentOutputAsync()
    {
        if (_cliProcess == null)
        {
            throw new InvalidOperationException("JoinCode 进程未启动。请先调用 StartJoinCodeAsync。");
        }

        return await _cliProcess.GetOutputAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 清空 JoinCode 输出缓冲区
    /// </summary>
    protected async Task ClearOutputAsync()
    {
        if (_cliProcess == null)
        {
            throw new InvalidOperationException("JoinCode 进程未启动。请先调用 StartJoinCodeAsync。");
        }

        await _cliProcess.ClearOutputAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 构建进程启动参数
    /// </summary>
    private static string BuildArguments(string pipeName, string? additionalArgs)
    {
        var args = $"--pipe \"{pipeName}\"";

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += $" {additionalArgs}";
        }

        return args;
    }

    /// <summary>
    /// 构建环境变量字典
    /// </summary>
    private static Dictionary<string, string> BuildEnvironmentVariables(string pipeName, string apiKey)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(pipeName).ToUpperInvariant()] = pipeName,
            [ProviderEnvVarConstants.OpenAiApiKey] = apiKey,
            ["OPENAI_BASE_URL"] = "http://localhost:8080/v1"
        };
    }
}
