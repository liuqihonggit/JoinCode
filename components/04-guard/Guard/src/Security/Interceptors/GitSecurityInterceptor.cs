
namespace Core.Security.Interceptors;

[Register]
public sealed partial class GitSecurityInterceptor : IGitSecurityInterceptor
{
    private readonly IGitDiffProvider _diffProvider;
    private readonly IGitSecretScanner _scanner;
    [Inject] private readonly ILogger<GitSecurityInterceptor> _logger;

    private static readonly HashSet<string> ScannedTools =
    [
        "git_commit",
        "git_add"
    ];

    public int Priority => 100;

    public GitSecurityInterceptor(
        IGitDiffProvider diffProvider,
        IGitSecretScanner scanner,
        ILogger<GitSecurityInterceptor> logger)
    {
        _diffProvider = diffProvider ?? throw new ArgumentNullException(nameof(diffProvider));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static bool ShouldScanTool(string toolName)
    {
        return ScannedTools.Contains(toolName);
    }

    public async Task<ScanResult> ScanBeforeCommitAsync(string workingDirectory, CancellationToken ct = default)
    {
        _logger.LogDebug("开始安全扫描: WorkingDir={WorkingDir}", workingDirectory);

        var stagedFiles = await _diffProvider.GetStagedFileNamesAsync(workingDirectory, ct).ConfigureAwait(false);

        if (stagedFiles.Count == 0)
        {
            _logger.LogDebug("暂存区为空，跳过安全扫描");
            return ScanResult.Safe;
        }

        var fileNameResult = await _scanner.ScanFileNamesAsync(stagedFiles, ct).ConfigureAwait(false);
        if (fileNameResult.IsBlocked)
        {
            _logger.LogWarning("文件名安全扫描拦截: {Count} 个敏感文件", fileNameResult.Findings.Count);
            return fileNameResult;
        }

        var diffOutput = await _diffProvider.GetStagedDiffAsync(workingDirectory, ct).ConfigureAwait(false);
        var contentResult = await _scanner.ScanContentAsync(diffOutput, ct).ConfigureAwait(false);
        if (contentResult.IsBlocked)
        {
            _logger.LogWarning("内容安全扫描拦截: {Count} 个密钥泄露", contentResult.Findings.Count);
            return contentResult;
        }

        _logger.LogDebug("安全扫描通过: {FileCount} 个文件", stagedFiles.Count);
        return ScanResult.Safe;
    }
}
