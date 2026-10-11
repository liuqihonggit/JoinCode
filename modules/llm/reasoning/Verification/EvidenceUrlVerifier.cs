namespace JoinCode.Reasoning.Verification;

/// <summary>
/// 证据URL验证器 — 验证证据超链接的可访问性和内容匹配
/// 打不开、超时、grep出错 → 直接视为证据错误
/// </summary>
public sealed class EvidenceUrlVerifier {
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>
    /// 验证超时（秒）
    /// </summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// 构造URL验证器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="httpClient">可选的HTTP客户端，未提供时按超时配置内部创建</param>
    public EvidenceUrlVerifier(ILogger<EvidenceUrlVerifier> logger, HttpClient? httpClient = null) {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
    }

    /// <summary>
    /// 验证单个证据的URL
    /// </summary>
    public async Task<UrlVerificationResult> VerifyAsync(EvidenceRecord evidence, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(evidence.SourceUrl)) {
            return new UrlVerificationResult {
                Url = string.Empty,
                IsValid = true,
                IsAccessible = true,
                ContainsExpectedText = true,
            };
        }

        var url = evidence.SourceUrl;
        var isValid = false;
        var isAccessible = false;
        var containsExpectedText = false;
        int? foundAtLine = null;
        string? extractedText = null;
        string? error = null;
        var isTimeout = false;
        DateTime? verificationTime = null;

        try {
            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                _logger.LogWarning("[验证] URL不可访问: {Url} HTTP {Status}", url, response.StatusCode);
                return new UrlVerificationResult {
                    Url = url,
                    Error = $"HTTP {response.StatusCode}",
                };
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            isAccessible = true;

            if (!string.IsNullOrEmpty(evidence.ExtractedText)) {
                var (contains, line, text) = FindExtractedText(content, evidence.ExtractedText);
                containsExpectedText = contains;
                foundAtLine = line;
                extractedText = text;
                if (line is not null) {
                    isValid = true;
                    verificationTime = DateTime.UtcNow;
                }
            } else {
                isValid = true;
                verificationTime = DateTime.UtcNow;
            }
        } catch (TaskCanceledException) {
            _logger.LogWarning("[验证] URL超时: {Url}", url);
            error = "连接超时";
            isTimeout = true;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "[验证] URL验证异常: {Url}", url);
            error = ex.Message;
        }

        return new UrlVerificationResult {
            Url = url,
            IsValid = isValid,
            IsAccessible = isAccessible,
            ContainsExpectedText = containsExpectedText,
            FoundAtLine = foundAtLine,
            ExtractedText = extractedText,
            Error = error,
            IsTimeout = isTimeout,
            VerificationTime = verificationTime,
        };
    }

    /// <summary>
    /// 批量验证证据URL — 验证失败自动降级信任度
    /// </summary>
    public async Task<IReadOnlyList<UrlVerificationResult>> VerifyAllAsync(
        IReadOnlyList<EvidenceRecord> evidences, CancellationToken ct = default) {
        var results = new List<UrlVerificationResult>();

        foreach (var evidence in evidences.Where(e => !string.IsNullOrEmpty(e.SourceUrl) && !e.IsUrlVerified)) {
            var result = await VerifyAsync(evidence, ct).ConfigureAwait(false);
            results.Add(result);

            if (result.IsValid) {
                _logger.LogInformation("[验证] 链接有效: {Url}", evidence.SourceUrl);
            } else {
                _logger.LogWarning("[验证] 链接无效: {Url} - {Error}", evidence.SourceUrl, result.Error);
            }
        }

        return results;
    }

    /// <summary>
    /// 在页面内容中查找期望文本 — 返回是否包含、首次出现行号(从1开始)与该行去除首尾空白后的文本。
    /// 纯计算,不涉及 IO 与时间。extractedText 为 null 或空时返回 (false, null, null)。
    /// </summary>
    /// <param name="content">页面全文</param>
    /// <param name="extractedText">期望定位的文本片段</param>
    /// <returns>(整体包含, 首次命中行号, 命中行去除空白文本); 未命中行时行号与文本为 null</returns>
    internal static (bool ContainsExpectedText, int? FoundAtLine, string? ExtractedLineText) FindExtractedText(string content, string? extractedText) {
        if (string.IsNullOrEmpty(extractedText)) return (false, null, null);

        var contains = content.Contains(extractedText, StringComparison.Ordinal);
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].Contains(extractedText, StringComparison.Ordinal)) {
                return (contains, i + 1, lines[i].Trim());
            }
        }
        return (contains, null, null);
    }
}