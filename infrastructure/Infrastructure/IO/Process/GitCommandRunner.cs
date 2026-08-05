namespace IO.ProcessService;

/// <summary>
/// Git 命令统一执行器 — 委托给 IProcessService，消除各处重复代码
/// </summary>
[Register]
public sealed partial class GitCommandRunner : IGitCommandRunner
{
    [Inject] private readonly IProcessService _processService;
    [Inject] private readonly ILogger<GitCommandRunner>? _logger;

    public async Task<GitCommandResult> ExecuteAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        try
        {
            var options = new ProcessOptions
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["GIT_ASKPASS"] = "",
                    ["GIT_PAGER"] = "cat",
                    ["PAGER"] = "cat"
                }
            };

            var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);

            return new GitCommandResult
            {
                Success = result.Success,
                Output = result.StandardOutput,
                Error = result.StandardError,
                ExitCode = result.ExitCode
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
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
        var result = await ExecuteAsync(
            "grep -l -E \"^<<<<<<< |^=======$|^>>>>>>> \"",
            workingDirectory, ct).ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            var files = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static f => f.Trim())
                .Where(static f => f.Length > 0)
                .ToList();
            return new StaleConflictMarkerResult { HasStaleMarkers = true, Files = files };
        }

        if (result.ExitCode == 1)
        {
            return new StaleConflictMarkerResult { HasStaleMarkers = false };
        }

        return new StaleConflictMarkerResult { HasStaleMarkers = false, Error = result.Error };
    }
}
