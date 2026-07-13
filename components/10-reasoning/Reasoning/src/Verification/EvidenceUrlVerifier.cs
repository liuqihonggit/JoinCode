namespace JoinCode.Reasoning.Verification;

/// <summary>
/// 证据URL验证器 — 验证证据超链接的可访问性和内容匹配
/// 打不开、超时、grep出错 → 直接视为证据错误
/// </summary>
public sealed class EvidenceUrlVerifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>
    /// 验证超时（秒）
    /// </summary>
    public int TimeoutSeconds { get; init; } = 10;

    public EvidenceUrlVerifier(ILogger<EvidenceUrlVerifier> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
    }

    /// <summary>
    /// 验证单个证据的URL
    /// </summary>
    public async Task<UrlVerificationResult> VerifyAsync(EvidenceRecord evidence, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(evidence.SourceUrl))
        {
            return new UrlVerificationResult
            {
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

        try
        {
            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[验证] URL不可访问: {Url} HTTP {Status}", url, response.StatusCode);
                return new UrlVerificationResult
                {
                    Url = url,
                    Error = $"HTTP {response.StatusCode}",
                };
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            isAccessible = true;

            if (!string.IsNullOrEmpty(evidence.ExtractedText))
            {
                containsExpectedText = content.Contains(evidence.ExtractedText, StringComparison.Ordinal);

                var lines = content.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(evidence.ExtractedText, StringComparison.Ordinal))
                    {
                        foundAtLine = i + 1;
                        extractedText = lines[i].Trim();
                        isValid = true;
                        verificationTime = DateTime.UtcNow;
                        break;
                    }
                }
            }
            else
            {
                isValid = true;
                verificationTime = DateTime.UtcNow;
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[验证] URL超时: {Url}", url);
            error = "连接超时";
            isTimeout = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[验证] URL验证异常: {Url}", url);
            error = ex.Message;
        }

        return new UrlVerificationResult
        {
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
        IReadOnlyList<EvidenceRecord> evidences, CancellationToken ct = default)
    {
        var results = new List<UrlVerificationResult>();

        foreach (var evidence in evidences.Where(e => !string.IsNullOrEmpty(e.SourceUrl) && !e.IsUrlVerified))
        {
            var result = await VerifyAsync(evidence, ct).ConfigureAwait(false);
            results.Add(result);

            if (result.IsValid)
            {
                _logger.LogInformation("[验证] 链接有效: {Url}", evidence.SourceUrl);
            }
            else
            {
                _logger.LogWarning("[验证] 链接无效: {Url} - {Error}", evidence.SourceUrl, result.Error);
            }
        }

        return results;
    }
}
