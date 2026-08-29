namespace Core.Agents.Doctor;


/// <summary>
/// 源码工程引擎 — Doctor 模式的源码工程能力核心
/// 解决 "拿着 exe 无法自举" 的根本问题
/// </summary>
public sealed class SourceCodeEngine : ISourceCodeEngine
{
    private readonly IFileSystem _fs;
    private readonly IGitCommandRunner _gitRunner;

    private static readonly (int Layer, string SlnxName)[] BuildLayers =
    [
        (1, "Generators.slnx"),
        (2, "Foundation.slnx"),
        (3, "Infrastructure.slnx"),
        (4, "Core.slnx"),
        (5, "Services.slnx"),
        (6, "Composition.slnx"),
        (7, "App.slnx")
    ];

    public SourceCodeEngine(IFileSystem fs, IGitCommandRunner gitRunner)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
    }

    public async Task<SourceCodeLocation> LocateSourceRepositoryAsync(
        string? hintPath = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var envSourceDir = Environment.GetEnvironmentVariable("JCC_SOURCE_DIR");
        if (!string.IsNullOrEmpty(envSourceDir) && _fs.DirectoryExists(envSourceDir))
            return await BuildLocationAsync(envSourceDir, ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(hintPath))
        {
            var gitRoot = SearchUpForGitRoot(hintPath);
            if (gitRoot is not null)
                return await BuildLocationAsync(gitRoot, ct).ConfigureAwait(false);
        }

        var exeDir = AppContext.BaseDirectory;
        var exeGitRoot = SearchUpForGitRoot(exeDir);
        if (exeGitRoot is not null)
            return await BuildLocationAsync(exeGitRoot, ct).ConfigureAwait(false);

        var cwdGitRoot = SearchUpForGitRoot(_fs.GetCurrentDirectory());
        if (cwdGitRoot is not null)
            return await BuildLocationAsync(cwdGitRoot, ct).ConfigureAwait(false);

        return new SourceCodeLocation
        {
            GitRoot = "",
            IsAvailable = false,
            FailureReason = "无法定位源码仓库。请设置 JCC_SOURCE_DIR 环境变量或在源码目录中运行"
        };
    }

    public async Task<FullBuildResult> BuildFullProjectAsync(
        string worktreePath,
        string configuration = "Debug",
        CancellationToken ct = default)
    {
        var layerResults = new List<SlnxBuildResult>();
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var (layer, slnxName) in BuildLayers)
        {
            ct.ThrowIfCancellationRequested();

            var slnxPath = Path.Combine(worktreePath, slnxName);
            if (!_fs.FileExists(slnxPath))
            {
                layerResults.Add(new SlnxBuildResult
                {
                    Layer = layer,
                    SlnxName = slnxName,
                    Success = false,
                    ExitCode = -1,
                    Output = $"slnx 不存在: {slnxPath}"
                });
                break;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = worktreePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("build");
                startInfo.ArgumentList.Add(slnxPath);
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(configuration);
                if (configuration == "Release")
                {
                    startInfo.ArgumentList.Add("--no-incremental");
                }

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process is null)
                {
                    sw.Stop();
                    layerResults.Add(new SlnxBuildResult
                    {
                        Layer = layer, SlnxName = slnxName, Success = false,
                        ExitCode = -1, Output = "无法启动 dotnet 进程", Duration = sw.Elapsed
                    });
                    break;
                }

                var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                sw.Stop();
                var success = process.ExitCode == 0;
                layerResults.Add(new SlnxBuildResult
                {
                    Layer = layer,
                    SlnxName = slnxName,
                    Success = success,
                    ExitCode = process.ExitCode,
                    Output = success ? output : $"{output}\n{error}",
                    Duration = sw.Elapsed
                });

                if (!success) break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                sw.Stop();
                layerResults.Add(new SlnxBuildResult
                {
                    Layer = layer, SlnxName = slnxName, Success = false,
                    ExitCode = -1, Output = ex.Message, Duration = sw.Elapsed
                });
                break;
            }
        }

        totalSw.Stop();

        var artifactExePath = layerResults.Count > 0 && layerResults.All(r => r.Success)
            ? await GetArtifactExePathAsync(worktreePath, configuration, ct).ConfigureAwait(false)
            : null;

        return new FullBuildResult
        {
            Success = layerResults.Count > 0 && layerResults.All(r => r.Success),
            LayerResults = layerResults,
            ArtifactExePath = artifactExePath,
            TotalDuration = totalSw.Elapsed
        };
    }

    public Task<string> GetArtifactExePathAsync(
        string worktreePath,
        string configuration = "Debug",
        CancellationToken ct = default)
    {
        var exePath = Path.Combine(worktreePath, "artifacts", "bin", "JoinCode", configuration, "net10.0", "jcc.exe");
        return Task.FromResult(exePath);
    }

    public async Task<ExeSwapResult> SwapExeAsync(
        string currentExePath,
        string newExePath,
        string patientId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_fs.FileExists(newExePath))
        {
            return new ExeSwapResult
            {
                Success = false,
                Description = $"新 exe 不存在: {newExePath}"
            };
        }

        if (!_fs.FileExists(currentExePath))
        {
            return new ExeSwapResult
            {
                Success = false,
                Description = $"当前 exe 不存在: {currentExePath}"
            };
        }

        try
        {
            var backupPath = currentExePath + ".bak";
            _fs.CopyFile(currentExePath, backupPath, overwrite: true);

            var oldPath = currentExePath + ".old";
            if (_fs.FileExists(oldPath))
                _fs.DeleteFile(oldPath);

            _fs.MoveFile(currentExePath, oldPath);
            _fs.CopyFile(newExePath, currentExePath);

            return new ExeSwapResult
            {
                Success = true,
                OldExePath = currentExePath,
                NewExePath = newExePath,
                Description = "exe 已替换，下次启动病人进程将使用新版本"
            };
        }
        catch (Exception ex)
        {
            var backupPath = currentExePath + ".bak";
            if (_fs.FileExists(backupPath))
            {
                try { _fs.CopyFile(backupPath, currentExePath, overwrite: true); }
                catch (Exception rollbackEx) { DoctorDiag.WriteError($"[Doctor] 回滚失败: {rollbackEx.Message}"); }
            }

            return new ExeSwapResult
            {
                Success = false,
                Description = $"替换失败: {ex.Message}，已回滚"
            };
        }
    }

    public async Task<SourceCodeLocation> EnsureSourceAvailableAsync(
        string? repoUrl = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var envSourceDir = Environment.GetEnvironmentVariable("JCC_SOURCE_DIR");
        if (!string.IsNullOrEmpty(envSourceDir) && _fs.DirectoryExists(Path.Combine(envSourceDir, ".git")))
        {
            DoctorDiag.Write($"[SourceCodeEngine] 策略1: JCC_SOURCE_DIR={envSourceDir}");
            return await BuildLocationAsync(envSourceDir, ct).ConfigureAwait(false);
        }

        var exeGitRoot = SearchUpForGitRoot(AppContext.BaseDirectory);
        if (exeGitRoot is not null)
        {
            DoctorDiag.Write($"[SourceCodeEngine] 策略2: exe目录搜索.git={exeGitRoot}");
            return await BuildLocationAsync(exeGitRoot, ct).ConfigureAwait(false);
        }

        var cwdGitRoot = SearchUpForGitRoot(_fs.GetCurrentDirectory());
        if (cwdGitRoot is not null)
        {
            DoctorDiag.Write($"[SourceCodeEngine] 策略2: cwd搜索.git={cwdGitRoot}");
            return await BuildLocationAsync(cwdGitRoot, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(repoUrl))
        {
            DoctorDiag.Write($"[SourceCodeEngine] 策略3: git clone {repoUrl}");
            var cloneResult = await GitCloneAsync(repoUrl, ct).ConfigureAwait(false);
            if (cloneResult.IsAvailable)
                return cloneResult;
        }

        return new SourceCodeLocation
        {
            GitRoot = "",
            IsAvailable = false,
            FailureReason = "无法确保源码可用。请设置 JCC_SOURCE_DIR 环境变量、在源码目录中运行、或提供 repoUrl"
        };
    }

    private async Task<SourceCodeLocation> GitCloneAsync(string repoUrl, CancellationToken ct)
    {
        var homeDir = Environment.GetEnvironmentVariable("USERPROFILE")
            ?? Environment.GetEnvironmentVariable("HOME")
            ?? AppContext.BaseDirectory;
        var cloneDir = Path.Combine(homeDir, ".jcc", "source");

        try
        {
            if (!_fs.DirectoryExists(cloneDir))
                _fs.CreateDirectory(cloneDir);

            var result = await _gitRunner.ExecuteAsync($"clone \"{repoUrl}\" \"{cloneDir}\"", null, ct).ConfigureAwait(false);

            if (!result.Success)
                return new SourceCodeLocation { GitRoot = "", IsAvailable = false, FailureReason = $"git clone 失败: {result.Error}" };

            return await BuildLocationAsync(cloneDir, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[SourceCodeEngine] git clone 异常: {ex.Message}");
            return new SourceCodeLocation { GitRoot = "", IsAvailable = false, FailureReason = ex.Message };
        }
    }

    private string? SearchUpForGitRoot(string startDir)
    {
        var dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (_fs.DirectoryExists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private async Task<SourceCodeLocation> BuildLocationAsync(string gitRoot, CancellationToken ct)
    {
        string? branch = null;
        string? commitHash = null;

        try
        {
            var branchResult = await _gitRunner.ExecuteAsync("rev-parse --abbrev-ref HEAD", gitRoot, ct).ConfigureAwait(false);
            branch = branchResult.Output.Trim();

            var hashResult = await _gitRunner.ExecuteAsync("rev-parse HEAD", gitRoot, ct).ConfigureAwait(false);
            commitHash = hashResult.Output.Trim();
        }
        catch (Exception ex) { DoctorDiag.WriteError($"[Doctor] 获取 git 信息失败: {ex.Message}"); }

        return new SourceCodeLocation
        {
            GitRoot = gitRoot,
            IsAvailable = true,
            CurrentBranch = branch,
            CurrentCommitHash = commitHash
        };
    }
}
