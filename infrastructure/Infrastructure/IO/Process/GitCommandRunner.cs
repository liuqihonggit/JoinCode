namespace IO.ProcessService;

/// <summary>
/// Git 命令统一执行器 — 委托给 IProcessService，消除各处重复代码
/// </summary>
[Register]
public sealed partial class GitCommandRunner : ServiceEntity, IGitCommandRunner
{

    public GitCommandRunner(IProcessService processService, ILogger<GitCommandRunner>? logger = null)
    {
        _processService = processService;
        _logger = logger;
    }
    private readonly IProcessService _processService;
    private readonly ILogger<GitCommandRunner>? _logger;

    public async Task<GitCommandResult> ExecuteAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        try
        {
            Console.Error.WriteLine($"[DIAG-GIT] ExecuteAsync start: git {arguments}, cwd={workingDirectory}, ct.CanCancel={ct.CanBeCanceled}");
            Console.Error.Flush();
            var options = new ProcessOptions
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                EnvironmentVariables = CreateGitEnvironment()
            };

            var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);

            Console.Error.WriteLine($"[DIAG-GIT] ExecuteAsync end: git {arguments}, exitCode={result.ExitCode}, stdoutLen={result.StandardOutput.Length}, time={result.ExecutionTime.TotalMilliseconds:F0}ms");
            Console.Error.Flush();

            return new GitCommandResult
            {
                Success = result.Success,
                Output = result.StandardOutput,
                Error = result.StandardError,
                ExitCode = result.ExitCode
            };
        }
        catch (OperationCanceledException ex)
        {
            Console.Error.WriteLine($"[DIAG-GIT] ExecuteAsync CANCELED: git {arguments}, {ex.GetType().Name}");
            Console.Error.Flush();
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DIAG-GIT] ExecuteAsync EXCEPTION: git {arguments}, {ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();
            _logger?.LogError(ex, "执行 Git 命令失败: git {Arguments}", arguments);
            return new GitCommandResult
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    public async Task<MergeConflictResult> DetectMergeConflictAsync(
        string branch1,
        string branch2,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var result = await ExecuteAsync($"merge-tree --write-tree --name-only {branch1} {branch2}", workingDirectory, ct).ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            return new MergeConflictResult { HasConflict = false, MergedTreeOid = result.Output.Trim() };
        }

        if (result.ExitCode == 1)
        {
            var lines = result.Output.Split('\n');
            var treeOid = lines.Length > 0 ? lines[0].Trim() : string.Empty;
            var conflictFiles = new List<string>();
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) break;
                conflictFiles.Add(line);
            }
            return new MergeConflictResult { HasConflict = true, MergedTreeOid = treeOid, ConflictFiles = conflictFiles };
        }

        return new MergeConflictResult { HasConflict = false, Error = result.Error };
    }

    public async Task<StaleConflictMarkerResult> DetectStaleConflictMarkersAsync(
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var options = new ProcessOptions
        {
            FileName = "git",
            ArgumentList = ["grep", "-l", "-E", "^<<<<<<< |^=======$|^>>>>>>> "],
            WorkingDirectory = workingDirectory,
            SkipArgumentValidation = true,
            EnvironmentVariables = CreateGitEnvironment()
        };

        var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            var files = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static f => f.Trim())
                .Where(static f => f.Length > 0)
                .ToList();
            return new StaleConflictMarkerResult { HasStaleMarkers = true, Files = files };
        }

        if (result.ExitCode == 1)
        {
            return new StaleConflictMarkerResult { HasStaleMarkers = false };
        }

        return new StaleConflictMarkerResult { HasStaleMarkers = false, Error = result.StandardError };
    }

    /// <summary>
    /// 创建 Git 命令专用环境变量 — 避免交互式提示卡死
    /// </summary>
    private static Dictionary<string, string> CreateGitEnvironment() => new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_ASKPASS"] = "",
        ["GIT_PAGER"] = "cat",
        ["PAGER"] = "cat",
        ["GIT_EDITOR"] = "true",
        ["EDITOR"] = "true",
        ["VISUAL"] = "true"
    };
}
