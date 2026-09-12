namespace Core.Hooks.Lifecycle;


public interface ISubagentStopCheckpointInternal : ISubagentStopCheckpoint;

[Register(typeof(ISubagentStopCheckpointInternal), ServiceLifetime.Singleton)]
public sealed partial class SubagentStopCheckpoint : ServiceEntity, ISubagentStopCheckpointInternal
{
    private readonly IGitSecretScanner _secretScanner;
    private readonly IGitDiffProvider _diffProvider;
    private readonly IBuildQueueService _buildQueue;
    private readonly ILogger<SubagentStopCheckpoint>? _logger;

    public SubagentStopCheckpoint(
        IGitSecretScanner secretScanner,
        IGitDiffProvider diffProvider,
        IBuildQueueService buildQueue,
        ILogger<SubagentStopCheckpoint>? logger = null)
    {
        _secretScanner = secretScanner;
        _diffProvider = diffProvider;
        _buildQueue = buildQueue;
        _logger = logger;
    }

    public async Task<CheckpointResult> ExecuteAsync(CheckpointContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var violations = new List<CheckpointViolation>();
        var workingDir = context.WorktreePath ?? context.WorkingDirectory;

        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return CheckpointResult.Pass();
        }

        await ScanSecretsAsync(workingDir, violations, ct).ConfigureAwait(false);
        await VerifyBuildAsync(workingDir, violations, ct).ConfigureAwait(false);

        var errors = violations.Where(v => v.Severity == "error").ToList();
        var warnings = violations.Where(v => v.Severity != "error").ToList();

        return errors.Count == 0
            ? CheckpointResult.Pass(warnings)
            : CheckpointResult.Fail(errors);
    }

    private async Task ScanSecretsAsync(string workingDir, List<CheckpointViolation> violations, CancellationToken ct)
    {
        try
        {
            var stagedFiles = await _diffProvider.GetStagedFileNamesAsync(workingDir, ct).ConfigureAwait(false);
            var fileNameResult = await _secretScanner.ScanFileNamesAsync(stagedFiles, ct).ConfigureAwait(false);

            if (fileNameResult.IsBlocked)
            {
                foreach (var finding in fileNameResult.Findings)
                {
                    violations.Add(new CheckpointViolation
                    {
                        Rule = "no-secret-files",
                        Message = $"敏感文件检测: {finding.FilePath}",
                        Severity = "error"
                    });
                }
            }

            var diffOutput = await _diffProvider.GetStagedDiffAsync(workingDir, ct).ConfigureAwait(false);
            var contentResult = await _secretScanner.ScanContentAsync(diffOutput, ct).ConfigureAwait(false);

            if (contentResult.IsBlocked)
            {
                foreach (var finding in contentResult.Findings)
                {
                    violations.Add(new CheckpointViolation
                    {
                        Rule = "no-secrets-in-diff",
                        Message = $"密钥泄露检测: {finding.FilePath} 行{finding.LineNumber}",
                        Severity = "error"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Secret scan checkpoint failed, skipping");
            violations.Add(new CheckpointViolation
            {
                Rule = "secret-scan-error",
                Message = $"密钥扫描异常: {ex.Message}",
                Severity = "warning"
            });
        }
    }

    private async Task VerifyBuildAsync(string workingDir, List<CheckpointViolation> violations, CancellationToken ct)
    {
        try
        {
            var request = new BuildRequest
            {
                Command = "dotnet build --verbosity quiet --no-restore",
                WorkingDirectory = workingDir,
            };

            var buildId = await _buildQueue.SubmitAsync(request, ct).ConfigureAwait(false);
            var result = await _buildQueue.WaitAsync(buildId, ct).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                violations.Add(new CheckpointViolation
                {
                    Rule = "build-must-pass",
                    Message = $"编译失败: {(result.Output?.Length > 200 ? result.Output[..200] + "..." : result.Output)}",
                    Severity = "error"
                });
            }
        }
        catch (OperationCanceledException)
        {
            violations.Add(new CheckpointViolation
            {
                Rule = "build-timeout",
                Message = "编译超时",
                Severity = "warning"
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Build verification checkpoint failed, skipping");
            violations.Add(new CheckpointViolation
            {
                Rule = "build-error",
                Message = $"编译验证异常: {ex.Message}",
                Severity = "warning"
            });
        }
    }
}
