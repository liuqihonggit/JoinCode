
namespace Core.Security.Interceptors;

/// <summary>
/// Git 安全拦截器 — 在 git_commit / git_add 工具执行前扫描暂存区,拦截敏感文件与密钥泄露
/// </summary>
[Register(typeof(IGitSecurityInterceptor), ServiceLifetime.Singleton)]
public sealed partial class GitSecurityInterceptor : ServiceEntity, IGitSecurityInterceptor {
    private readonly IGitDiffProvider _diffProvider;
    private readonly IGitSecretScanner _scanner;
    private readonly ILogger<GitSecurityInterceptor> _logger;

    private static readonly HashSet<string> ScannedTools =
    [
        "git_commit",
        "git_add"
    ];

    /// <inheritdoc />
    public int Priority => 100;

    /// <summary>
    /// 构造函数 — 注入差异提供者、密钥扫描器与日志记录器
    /// </summary>
    /// <param name="diffProvider">Git 差异提供者</param>
    /// <param name="scanner">Git 密钥扫描器</param>
    /// <param name="logger">日志记录器</param>
    public GitSecurityInterceptor(
        IGitDiffProvider diffProvider,
        IGitSecretScanner scanner,
        ILogger<GitSecurityInterceptor> logger) {
        _diffProvider = diffProvider ?? throw new ArgumentNullException(nameof(diffProvider));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 判断指定工具是否需要触发安全扫描
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <returns>需要扫描返回 true,否则返回 false</returns>
    public static bool ShouldScanTool(string toolName) {
        return ScannedTools.Contains(toolName);
    }

    /// <inheritdoc />
    public async Task<ScanResult> ScanBeforeCommitAsync(string workingDirectory, CancellationToken ct = default) {
        _logger.LogDebug("开始安全扫描: WorkingDir={WorkingDir}", workingDirectory);

        var stagedFiles = await _diffProvider.GetStagedFileNamesAsync(workingDirectory, ct).ConfigureAwait(false);

        if (stagedFiles.Count == 0) {
            _logger.LogDebug("暂存区为空，跳过安全扫描");
            return ScanResult.Safe;
        }

        var fileNameResult = await _scanner.ScanFileNamesAsync(stagedFiles, ct).ConfigureAwait(false);
        if (fileNameResult.IsBlocked) {
            _logger.LogWarning("文件名安全扫描拦截: {Count} 个敏感文件", fileNameResult.Findings.Count);
            return fileNameResult;
        }

        var diffOutput = await _diffProvider.GetStagedDiffAsync(workingDirectory, ct).ConfigureAwait(false);
        var contentResult = await _scanner.ScanContentAsync(diffOutput, ct).ConfigureAwait(false);
        if (contentResult.IsBlocked) {
            _logger.LogWarning("内容安全扫描拦截: {Count} 个密钥泄露", contentResult.Findings.Count);
            return contentResult;
        }

        _logger.LogDebug("安全扫描通过: {FileCount} 个文件", stagedFiles.Count);
        return ScanResult.Safe;
    }
}