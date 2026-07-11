
// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9001, JCC9002
namespace MockServer.E2E.Tests.Fixtures;

/// <summary>
/// JoinCode 进程启动选项封装类
/// </summary>
public sealed record CliProcessOptions
{
    public required string ExecutablePath { get; init; }
    public string Arguments { get; init; } = "";
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new();
    public string? WorkingDirectory { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool RedirectStandardInput { get; init; } = true;
    public bool RedirectStandardOutput { get; init; } = true;
    public bool RedirectStandardError { get; init; } = true;
}

/// <summary>
/// 管道 JoinCode 进程工厂
/// 用于创建带管道参数的 JoinCode 进程
/// </summary>
public sealed class PipeJoinCodeProcessFactory
{
    private readonly ILogger<PipeJoinCodeProcessFactory> _logger;

    public PipeJoinCodeProcessFactory(ILogger<PipeJoinCodeProcessFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 创建带管道参数的 JoinCode 进程
    /// </summary>
    /// <param name="pipeName">管道名称</param>
    /// <param name="apiKey">OpenAI API Key</param>
    /// <param name="executablePath">JoinCode 可执行文件路径</param>
    /// <param name="additionalArgs">额外参数</param>
    /// <returns>进程启动信息和进程实例</returns>
    public Task<Process> CreateAsync(
        string pipeName,
        string apiKey,
        string executablePath,
        string? additionalArgs = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(executablePath);

        // 确保管道名称使用标准格式
        var normalizedPipeName = NormalizePipeName(pipeName);

        var options = new CliProcessOptions
        {
            ExecutablePath = executablePath,
            Arguments = BuildArguments(normalizedPipeName, additionalArgs),
            EnvironmentVariables = BuildEnvironmentVariables(normalizedPipeName, apiKey),
            WorkingDirectory = Path.GetDirectoryName(executablePath)
        };

        return CreateAsync(options, ct);
    }

    /// <summary>
    /// 规范化管道名称，确保使用标准格式
    /// </summary>
    private static string NormalizePipeName(string pipeName)
    {
        // 如果已经是标准格式，直接返回
        if (pipeName.StartsWith("JoinCode_MockServer_", StringComparison.OrdinalIgnoreCase))
        {
            return pipeName;
        }

        // 否则添加标准前缀
        return $"JoinCode_MockServer_{pipeName}";
    }

    /// <summary>
    /// 根据选项创建 JoinCode 进程
    /// </summary>
    public Task<Process> CreateAsync(CliProcessOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ExecutablePath);

        _logger.LogInformation(
            "[{Factory}] 创建 JoinCode 进程: {Path} {Args}",
            nameof(PipeJoinCodeProcessFactory),
            options.ExecutablePath,
            options.Arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            Arguments = options.Arguments,
            RedirectStandardInput = options.RedirectStandardInput,
            RedirectStandardOutput = options.RedirectStandardOutput,
            RedirectStandardError = options.RedirectStandardError,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = options.WorkingDirectory ?? Directory.GetCurrentDirectory()
        };

        foreach (var (key, value) in options.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[key] = value;
            _logger.LogDebug("[{Factory}] 环境变量: {Key}={Value}", nameof(PipeJoinCodeProcessFactory), key, value);
        }

        var process = new Process { StartInfo = startInfo };

        _logger.LogInformation("[{Factory}] JoinCode 进程创建完成", nameof(PipeJoinCodeProcessFactory));

        return Task.FromResult(process);
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
    /// 优先从 .env 文件加载，如果未设置则使用传入的 apiKey
    /// </summary>
    private static Dictionary<string, string> BuildEnvironmentVariables(string pipeName, string apiKey)
    {
        // 尝试从 .env 文件或环境变量获取 API Key
        var effectiveApiKey = EnvFileLoader.Get("OPENAI_API_KEY") ?? apiKey;
        var baseUrl = EnvFileLoader.Get("OPENAI_BASE_URL") ?? "http://localhost:8080/v1";

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(pipeName).ToUpperInvariant()] = pipeName,
            [ProviderEnvVarConstants.OpenAiApiKey] = effectiveApiKey,
            ["OPENAI_BASE_URL"] = baseUrl
        };
    }
}
