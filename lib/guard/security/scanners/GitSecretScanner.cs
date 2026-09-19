
namespace Core.Security.Scanners;

/// <summary>
/// Git 密钥扫描器 — 检测暂存文件名与差异内容中的敏感信息与密钥泄露
/// </summary>
[Register(typeof(IGitSecretScanner), ServiceLifetime.Singleton)]
public sealed partial class GitSecretScanner : ServiceEntity, IGitSecretScanner {

    /// <summary>
    /// 构造函数 — 注入日志记录器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public GitSecretScanner(ILogger<GitSecretScanner> logger) {
        _logger = logger;
    }
    private readonly ILogger<GitSecretScanner> _logger;

    /// <inheritdoc />
    public Task<ScanResult> ScanFileNamesAsync(IReadOnlyList<string> stagedFiles, CancellationToken ct = default) {
        var findings = new List<SecretFinding>();

        foreach (var file in stagedFiles) {
            if (!SecurityPatterns.IsSensitiveFileName(file))
                continue;

            var type = DetermineSecretType(file);
            findings.Add(new SecretFinding {
                FilePath = file,
                MatchedPattern = file,
                MatchedContent = file,
                Type = type
            });

            _logger.LogWarning("检测到敏感文件: {FilePath} ({Type})", file, type);
        }

        if (findings.Count == 0)
            return Task.FromResult(ScanResult.Safe);

        return Task.FromResult(ScanResult.Blocked(findings));
    }

    /// <inheritdoc />
    public Task<ScanResult> ScanContentAsync(string diffOutput, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(diffOutput))
            return Task.FromResult(ScanResult.Safe);

        var allFindings = new List<SecretFinding>();
        var currentFile = string.Empty;
        var hunkLineNumber = 0;
        var lineIndex = 0;

        foreach (var line in diffOutput.Split('\n')) {
            lineIndex++;

            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) {
                currentFile = ExtractFileNameFromDiffLine(line);
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                hunkLineNumber = ParseStartLineNumber(line);
                continue;
            }

            if (!line.StartsWith('+') || line.StartsWith("+++"))
                continue;

            hunkLineNumber++;

            foreach (var regex in SecurityPatterns.CompiledSecretRegexes) {
                var match = regex.Match(line);
                if (match.Success) {
                    allFindings.Add(new SecretFinding {
                        FilePath = currentFile,
                        LineNumber = hunkLineNumber,
                        MatchedPattern = regex.ToString(),
                        MatchedContent = match.Value,
                        Type = SecretType.ApiKey
                    });

                    _logger.LogWarning("检测到密钥: {FilePath}:{LineNumber} 模式={Pattern}",
                        currentFile, hunkLineNumber, regex.ToString());
                }
            }
        }

        if (allFindings.Count == 0)
            return Task.FromResult(ScanResult.Safe);

        return Task.FromResult(ScanResult.Blocked(allFindings));
    }

    private static SecretType DetermineSecretType(string fileName) {
        if (SecurityPatterns.IsPrivateKeyFile(fileName))
            return SecretType.PrivateKey;

        if (SecurityPatterns.IsCredentialFile(fileName))
            return SecretType.Credential;

        return SecretType.SensitiveFile;
    }

    private static string ExtractFileNameFromDiffLine(string diffLine) {
        var span = diffLine.AsSpan();
        var bIndex = span.IndexOf(" b/");
        if (bIndex < 0)
            return diffLine;

        var afterB = span.Slice(bIndex + 3);
        return afterB.TrimEnd().ToString();
    }

    private static int ParseStartLineNumber(string hunkHeader) {
        var span = hunkHeader.AsSpan();
        var plusIndex = span.IndexOf('+');
        if (plusIndex < 0)
            return 0;

        var afterPlus = span.Slice(plusIndex + 1);
        var commaIndex = afterPlus.IndexOf(',');
        var numberSpan = commaIndex > 0 ? afterPlus.Slice(0, commaIndex) : afterPlus;

        return int.TryParse(numberSpan, out var num) ? num - 1 : 0;
    }
}