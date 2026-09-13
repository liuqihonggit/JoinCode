namespace Core.Agents.Doctor;

/// <summary>
/// 编译编排器 — 封装 dotnet build 命令执行，支持项目/解决方案编译
/// </summary>
public sealed class BuildOrchestrator
{
    private readonly IProcessService _processService;

    /// <summary>
    /// 构造编译编排器
    /// </summary>
    /// <param name="processService">进程服务抽象</param>
    public BuildOrchestrator(IProcessService processService)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    /// <summary>
    /// 编译指定项目 — 执行 dotnet build --no-incremental 全量编译
    /// </summary>
    /// <param name="projectPath">项目文件路径</param>
    /// <param name="configuration">编译配置（Debug/Release），默认 Debug</param>
    /// <param name="workingDirectory">工作目录（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>编译结果</returns>
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
                ArgumentList = new[] { "build", projectPath, "-c", configuration, "--no-incremental" },
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

    /// <summary>
    /// 编译指定解决方案 — 委托给 BuildProjectAsync，解决方案文件同样适用
    /// </summary>
    /// <param name="solutionPath">解决方案文件路径</param>
    /// <param name="configuration">编译配置（Debug/Release），默认 Debug</param>
    /// <param name="workingDirectory">工作目录（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>编译结果</returns>
    public async Task<BuildResult> BuildSolutionAsync(
        string solutionPath,
        string configuration = "Debug",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return await BuildProjectAsync(solutionPath, configuration, workingDirectory, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// 编译结果记录
/// </summary>
public sealed record BuildResult
{
    /// <summary>是否编译成功（退出码为 0）</summary>
    public required bool Success { get; init; }

    /// <summary>进程退出码</summary>
    public required int ExitCode { get; init; }

    /// <summary>项目路径</summary>
    public required string ProjectPath { get; init; }

    /// <summary>编译配置名称</summary>
    public required string Configuration { get; init; }

    /// <summary>标准输出内容</summary>
    public string? StandardOutput { get; init; }

    /// <summary>标准错误内容</summary>
    public string? StandardError { get; init; }

    /// <summary>编译耗时</summary>
    public TimeSpan Duration { get; init; }
}
