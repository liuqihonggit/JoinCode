
namespace MockServer.E2E.Tests.Fixtures;

/// <summary>
/// 管道 Mock Server Fixture
/// 实现 IAsyncLifetime 接口用于 xUnit 测试生命周期管理
/// </summary>
public sealed class PipeMockServerFixture : IAsyncLifetime
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PipeMockServerFixture> _logger;
    private PipeOpenAIMockServer? _mockServer;
    private RequestRecorder? _requestRecorder;

    /// <summary>
    /// Mock Server 实例
    /// </summary>
    public PipeOpenAIMockServer MockServer => _mockServer
        ?? throw new InvalidOperationException("Mock Server not initialized. Call InitializeAsync first.");

    /// <summary>
    /// 请求记录器
    /// </summary>
    public RequestRecorder RequestRecorder => _requestRecorder
        ?? throw new InvalidOperationException("Request Recorder not initialized. Call InitializeAsync first.");

    /// <summary>
    /// 是否已初始化
    /// </summary>
    public bool IsInitialized => _mockServer != null && _requestRecorder != null;

    public PipeMockServerFixture()
    {
        // 加载 .env 文件中的环境变量
        EnvFileLoader.LoadFromDirectory(new IO.FileSystem.PhysicalFileSystem());

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<PipeMockServerFixture>();
    }

    /// <summary>
    /// 初始化 Fixture - 启动 Mock Server
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("[{Fixture}] 初始化 Fixture", nameof(PipeMockServerFixture));

        _requestRecorder = new RequestRecorder();

        // 使用标准管道名称格式
        var pipeName = $"JoinCode_MockServer_{Guid.NewGuid():N}";
        var options = new MockServerOptions(
            pipeName: pipeName,
            apiKey: TestConfiguration.GetRealApiKey(),
            model: MockServerOptions.DefaultModel);

        var serverLogger = _loggerFactory.CreateLogger<PipeOpenAIMockServer>();
        _mockServer = new PipeOpenAIMockServer(options, serverLogger, _requestRecorder);

        await _mockServer.StartAsync().ConfigureAwait(true);

        _logger.LogInformation(
            "[{Fixture}] Fixture 初始化完成, PipeName: {PipeName}",
            nameof(PipeMockServerFixture),
            GetPipeName());
    }

    /// <summary>
    /// 释放 Fixture - 停止 Mock Server
    /// </summary>
    public async Task DisposeAsync()
    {
        _logger.LogInformation("[{Fixture}] 释放 Fixture", nameof(PipeMockServerFixture));

        if (_mockServer != null)
        {
            await _mockServer.StopAsync().ConfigureAwait(true);
            await _mockServer.DisposeAsync().ConfigureAwait(true);
            _mockServer = null;
        }

        _requestRecorder?.Clear();
        _requestRecorder = null;

        _loggerFactory.Dispose();

        _logger.LogInformation("[{Fixture}] Fixture 已释放", nameof(PipeMockServerFixture));
    }

    /// <summary>
    /// 获取管道名称（用于客户端连接）
    /// </summary>
    public string GetPipeName()
    {
        if (_mockServer == null)
        {
            throw new InvalidOperationException("Mock Server not started. Call InitializeAsync first.");
        }

        return _mockServer.GetPipeName();
    }

    /// <summary>
    /// 获取 API Key
    /// </summary>
    public string GetApiKey() => TestConfiguration.GetRealApiKey();

    /// <summary>
    /// 获取 Model 名称
    /// </summary>
    public string GetModel() => MockServerOptions.DefaultModel;

    /// <summary>
    /// 创建 Logger
    /// </summary>
    public ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();
}
