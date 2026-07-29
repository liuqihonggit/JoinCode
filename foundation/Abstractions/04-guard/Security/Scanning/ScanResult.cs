namespace JoinCode.Abstractions.Security.Scanning;

public enum SecretType
{
    [EnumValue("sensitiveFile")] SensitiveFile,
    [EnumValue("apiKey")] ApiKey,
    [EnumValue("privateKey")] PrivateKey,
    [EnumValue("credential")] Credential
}

public sealed record SecretFinding
{
    public required string FilePath { get; init; }
    public int? LineNumber { get; init; }
    public required string MatchedPattern { get; init; }
    public required string MatchedContent { get; init; }
    public required SecretType Type { get; init; }
}

public sealed record ScanResult
{
    public required bool IsBlocked { get; init; }
    public required IReadOnlyList<SecretFinding> Findings { get; init; }

    public static ScanResult Safe => new() { IsBlocked = false, Findings = [] };

    public static ScanResult Blocked(IReadOnlyList<SecretFinding> findings) =>
        new() { IsBlocked = true, Findings = findings };

    public string FormatReport()
    {
        if (!IsBlocked || Findings.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🚫 安全拦截: 检测到敏感内容，提交已被阻止！");
        sb.AppendLine();

        foreach (var finding in Findings)
        {
            sb.AppendLine($"  [{finding.Type}] {finding.FilePath}");
            if (finding.LineNumber.HasValue)
                sb.AppendLine($"    行号: {finding.LineNumber}");
            sb.AppendLine($"    匹配模式: {finding.MatchedPattern}");
            var truncated = finding.MatchedContent.Length > 40
                ? finding.MatchedContent[..40] + "..."
                : finding.MatchedContent;
            sb.AppendLine($"    匹配内容: {truncated}");
            sb.AppendLine();
        }

        sb.AppendLine("修复建议:");
        sb.AppendLine("  1. 将敏感文件添加到 .gitignore");
        sb.AppendLine("  2. 使用环境变量替代硬编码密钥");
        sb.AppendLine("  3. 使用 git reset HEAD <file> 取消暂存");

        return sb.ToString();
    }
}
