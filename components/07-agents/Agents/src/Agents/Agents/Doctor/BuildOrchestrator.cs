namespace Core.Agents.Doctor;

public sealed class BuildOrchestrator
{
    private readonly IProcessService _processService;
    private readonly ILogger? _logger;

    public BuildOrchestrator(IProcessService processService, ILogger? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    public async Task<BuildResult> BuildProjectAsync(
        string projectPath,
        string configuration = "Debug",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var arguments = $"build \"{projectPath}\" -c {configuration} --no-incremental";

            _logger?.LogInformation("[Doctor] 开始编译: dotnet {Args}", arguments);

            var options = new ProcessOptions
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                TimeoutMs = 120_000
            };

            var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            var success = result.ExitCode == 0;

            _logger?.LogInformation("[Doctor] 编译完成: 成功={Success}, 退出码={ExitCode}, 耗时={Duration}ms",
                success, result.ExitCode, sw.ElapsedMilliseconds);

            return new BuildResult
            {
                Success = success,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                ProjectPath = projectPath,
                Configuration = configuration,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger?.LogError(ex, "[Doctor] 编译异常: {ProjectPath}", projectPath);
            return new BuildResult
            {
                Success = false,
                ExitCode = -1,
                ProjectPath = projectPath,
                Configuration = configuration,
                StandardError = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    public async Task<BuildResult> BuildSolutionAsync(
        string solutionPath,
        string configuration = "Debug",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return await BuildProjectAsync(solutionPath, configuration, workingDirectory, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record BuildResult
{
    public required bool Success { get; init; }
    public required int ExitCode { get; init; }
    public required string ProjectPath { get; init; }
    public required string Configuration { get; init; }
    public string? StandardOutput { get; init; }
    public string? StandardError { get; init; }
    public TimeSpan Duration { get; init; }
}
