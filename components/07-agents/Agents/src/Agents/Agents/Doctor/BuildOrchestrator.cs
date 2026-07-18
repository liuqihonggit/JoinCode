namespace Core.Agents.Doctor;

public sealed class BuildOrchestrator
{
    private readonly IProcessService _processService;

    public BuildOrchestrator(IProcessService processService)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
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

            DoctorDiag.Write($"[Doctor] 开始编译: dotnet {arguments}");

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

            DoctorDiag.Write($"[Doctor] 编译完成: 成功={success}, 退出码={result.ExitCode}, 耗时={sw.ElapsedMilliseconds}ms");

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
            DoctorDiag.WriteError($"[Doctor] 编译异常: {projectPath}: {ex.Message}");
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
