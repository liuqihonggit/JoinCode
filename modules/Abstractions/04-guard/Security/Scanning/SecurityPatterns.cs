
namespace JoinCode.Abstractions.Security.Scanning;

/// <summary>
/// 密钥扫描匹配结果
/// 对齐 TS: secretScanner.ts SecretMatch
/// </summary>
public sealed record SecretMatch
{
    /// <summary>
    /// gitleaks 规则 ID（如 "github-pat", "aws-access-token"）
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// 人类可读的标签（如 "GitHub PAT", "AWS Access Token"）
    /// </summary>
    public required string Label { get; init; }
}

public static partial class SecurityPatterns
{
    /// <summary>
    /// 敏感文件模式按枚举分类存储 — 单一数据源来自 SensitiveFilePattern 枚举
    /// 消费方通过 ToValue() 或 IsDefined() 获取分类标识,通过本字典获取具体 Glob 模式列表
    /// </summary>
    private static readonly FrozenDictionary<SensitiveFilePattern, string[]> SensitiveFilePatternsByCategory =
        new Dictionary<SensitiveFilePattern, string[]>
        {
            [SensitiveFilePattern.EnvFiles] =
            [
                ".env", ".env.local", ".env.production", ".env.staging", ".env.development",
            ],
            [SensitiveFilePattern.CryptoKeys] =
            [
                "*.pem", "*.key", "*.p12", "*.pfx", "*.jks",
            ],
            [SensitiveFilePattern.SshKeys] =
            [
                "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa",
            ],
            [SensitiveFilePattern.Credentials] =
            [
                "credentials.json", "auth.json", "service-account.json",
            ],
            [SensitiveFilePattern.PackageRegistries] =
            [
                ".npmrc", ".pypirc", ".netrc", ".htpasswd",
            ],
            [SensitiveFilePattern.SecretsConfig] =
            [
                "secrets.yml", "secrets.yaml", "secrets.json",
            ],
            [SensitiveFilePattern.VcsInternal] =
            [
                ".git", ".git/**", ".svn", ".svn/**", ".hg", ".hg/**",
            ],
        }.ToFrozenDictionary();

    /// <summary>
    /// 扁平化的全部敏感文件模式 — 由分类字典聚合派生
    /// 保留为 public readonly 数组以兼容既有消费方(如 GitSecretScanner/GitSecurityInterceptor)
    /// </summary>
    public static readonly string[] SensitiveFilePatterns = SensitiveFilePatternsByCategory
        .Values
        .SelectMany(p => p)
        .ToArray();

    /// <summary>
    /// VCS 内部路径段集合 — 由 SensitiveFilePattern.VcsInternal 分类派生
    /// 过滤掉 glob 模式(如 .git/**),仅保留目录名段
    /// </summary>
    private static readonly FrozenSet<string> VcsInternalPathSegments = SensitiveFilePatternsByCategory[
            SensitiveFilePattern.VcsInternal]
        .Where(p => !p.Contains("**"))
        .Select(p => p.TrimStart('/').TrimEnd('/').ToLowerInvariant())
        .Where(p => p.Length > 0)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取指定分类下的所有敏感文件模式
    /// </summary>
    public static string[] GetPatternsForCategory(SensitiveFilePattern category)
        => SensitiveFilePatternsByCategory.TryGetValue(category, out var patterns) ? patterns : [];

    /// <summary>
    /// 枚举所有敏感文件模式分类及其对应模式
    /// </summary>
    public static IEnumerable<KeyValuePair<SensitiveFilePattern, string[]>> EnumerateByCategory()
        => SensitiveFilePatternsByCategory;

    /// <summary>
    /// 判断路径中的任意段是否为 VCS 内部路径(由 SensitiveFilePattern.VcsInternal 分类决定)
    /// 对齐 TS: gitSafety.ts 的 .git/.svn/.hg 路径匹配逻辑
    /// 支持 / 与 \ 两种分隔符,大小写不敏感
    /// </summary>
    public static bool MatchesVcsInternalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        foreach (var segment in path.Split('/', '\\'))
        {
            if (VcsInternalPathSegments.Contains(segment)) return true;
        }
        return false;
    }

    /// <summary>
    /// 高置信度 gitleaks 密钥扫描规则
    /// 对齐 TS: secretScanner.ts SECRET_RULES（30+ 条规则）
    /// 仅包含具有独特前缀、误报率近零的规则
    /// </summary>
    public static readonly (string Id, string Pattern, string? Flags)[] GitleaksRules =
    [
        // — 云服务商 —
        ("aws-access-token", @"\b((?:A3T[A-Z0-9]|AKIA|ASIA|ABIA|ACCA)[A-Z2-7]{16})\b", null),
        ("gcp-api-key", @"\b(AIza[\w-]{35})(?:[`'""\s;]|\\[nr]|$)", null),
        ("azure-ad-client-secret", @"(?:^|[\\""'`\s>=:(,)]) ([a-zA-Z0-9_~.]{3}\dQ~[a-zA-Z0-9_~.-]{31,34})(?:$|[\\""'`\s<),])", null),
        ("digitalocean-pat", @"\b(dop_v1_[a-f0-9]{64})(?:[`'""\s;]|\\[nr]|$)", null),
        ("digitalocean-access-token", @"\b(doo_v1_[a-f0-9]{64})(?:[`'""\s;]|\\[nr]|$)", null),

        // — AI API —
        ("anthropic-api-key", @"\b(sk-ant-api03-[a-zA-Z0-9_\-]{93}AA)(?:[`'""\s;]|\\[nr]|$)", null),
        ("anthropic-admin-api-key", @"\b(sk-ant-admin01-[a-zA-Z0-9_\-]{93}AA)(?:[`'""\s;]|\\[nr]|$)", null),
        ("openai-api-key", @"\b(sk-(?:proj|svcacct|admin)-(?:[A-Za-z0-9_-]{74}|[A-Za-z0-9_-]{58})T3BlbkFJ(?:[A-Za-z0-9_-]{74}|[A-Za-z0-9_-]{58})\b|sk-[a-zA-Z0-9]{20}T3BlbkFJ[a-zA-Z0-9]{20})(?:[`'""\s;]|\\[nr]|$)", null),
        ("huggingface-access-token", @"\b(hf_[a-zA-Z]{34})(?:[`'""\s;]|\\[nr]|$)", null),

        // — 版本控制 —
        ("github-pat", @"ghp_[0-9a-zA-Z]{36}", null),
        ("github-fine-grained-pat", @"github_pat_\w{82}", null),
        ("github-app-token", @"(?:ghu|ghs)_[0-9a-zA-Z]{36}", null),
        ("github-oauth", @"gho_[0-9a-zA-Z]{36}", null),
        ("github-refresh-token", @"ghr_[0-9a-zA-Z]{36}", null),
        ("gitlab-pat", @"glpat-[\w-]{20}", null),
        ("gitlab-deploy-token", @"gldt-[0-9a-zA-Z_\-]{20}", null),

        // — 通讯 —
        ("slack-bot-token", @"xoxb-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*", null),
        ("slack-user-token", @"xox[pe](?:-[0-9]{10,13}){3}-[a-zA-Z0-9-]{28,34}", null),
        ("slack-app-token", @"xapp-\d-[A-Z0-9]+-\d+-[a-z0-9]+", "i"),
        ("twilio-api-key", @"SK[0-9a-fA-F]{32}", null),
        ("sendgrid-api-token", @"\b(SG\.[a-zA-Z0-9=_\-.]{66})(?:[`'""\s;]|\\[nr]|$)", null),

        // — 开发工具 —
        ("npm-access-token", @"\b(npm_[a-zA-Z0-9]{36})(?:[`'""\s;]|\\[nr]|$)", null),
        ("pypi-upload-token", @"pypi-AgEIcHlwaS5vcmc[\w-]{50,1000}", null),
        ("databricks-api-token", @"\b(dapi[a-f0-9]{32}(?:-\d)?)(?:[`'""\s;]|\\[nr]|$)", null),
        ("hashicorp-tf-api-token", @"[a-zA-Z0-9]{14}\.atlasv1\.[a-zA-Z0-9\-_=]{60,70}", null),
        ("pulumi-api-token", @"\b(pul-[a-f0-9]{40})(?:[`'""\s;]|\\[nr]|$)", null),
        ("postman-api-token", @"\b(PMAK-[a-fA-F0-9]{24}-[a-fA-F0-9]{34})(?:[`'""\s;]|\\[nr]|$)", null),

        // — 可观测性 —
        ("grafana-api-key", @"\b(eyJrIjoi[A-Za-z0-9+/]{70,400}={0,3})(?:[`'""\s;]|\\[nr]|$)", null),
        ("grafana-cloud-api-token", @"\b(glc_[A-Za-z0-9+/]{32,400}={0,3})(?:[`'""\s;]|\\[nr]|$)", null),
        ("grafana-service-account-token", @"\b(glsa_[A-Za-z0-9]{32}_[A-Fa-f0-9]{8})(?:[`'""\s;]|\\[nr]|$)", null),
        ("sentry-user-token", @"\b(sntryu_[a-f0-9]{64})(?:[`'""\s;]|\\[nr]|$)", null),

        // — 支付/电商 —
        ("stripe-access-token", @"\b((?:sk|rk)_(?:test|live|prod)_[a-zA-Z0-9]{10,99})(?:[`'""\s;]|\\[nr]|$)", null),
        ("shopify-access-token", @"shpat_[a-fA-F0-9]{32}", null),
        ("shopify-shared-secret", @"shpss_[a-fA-F0-9]{32}", null),

        // — 加密 —
        ("private-key", @"-----BEGIN[ A-Z0-9_-]{0,100}PRIVATE KEY(?: BLOCK)?-----[\s\S-]{64,}?-----END[ A-Z0-9_-]{0,100}PRIVATE KEY(?: BLOCK)?-----", "i"),
    ];

    /// <summary>
    /// 规则 ID 到人类可读标签的映射
    /// 对齐 TS: secretScanner.ts ruleIdToLabel
    /// </summary>
    private static readonly FrozenDictionary<string, string> RuleIdLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["aws"] = "AWS",
        ["gcp"] = "GCP",
        ["api"] = "API",
        ["pat"] = "PAT",
        ["ad"] = "AD",
        ["tf"] = "TF",
        ["oauth"] = "OAuth",
        ["npm"] = "NPM",
        ["pypi"] = "PyPI",
        ["jwt"] = "JWT",
        ["github"] = "GitHub",
        ["gitlab"] = "GitLab",
        ["openai"] = "OpenAI",
        ["digitalocean"] = "DigitalOcean",
        ["huggingface"] = "HuggingFace",
        ["hashicorp"] = "HashiCorp",
        ["sendgrid"] = "SendGrid",
        ["anthropic"] = "Anthropic",
        ["stripe"] = "Stripe",
        ["shopify"] = "Shopify",
        ["grafana"] = "Grafana",
        ["sentry"] = "Sentry",
        ["slack"] = "Slack",
        ["twilio"] = "Twilio",
        ["pulumi"] = "Pulumi",
        ["postman"] = "Postman",
        ["databricks"] = "Databricks",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 将 gitleaks 规则 ID（kebab-case）转换为人类可读标签
    /// 对齐 TS: ruleIdToLabel("github-pat") → "GitHub PAT"
    /// </summary>
    public static string RuleIdToLabel(string ruleId)
    {
        var parts = ruleId.Split('-');
        var result = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            result[i] = RuleIdLabels.GetValueOrDefault(parts[i], Capitalize(parts[i]));
        }
        return string.Join(' ', result);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// 惰性编译的 gitleaks 规则缓存
    /// </summary>
    private static (string Id, Regex Re)[]? _compiledGitleaksRules;

    /// <summary>
    /// 获取编译后的 gitleaks 规则（首次调用时编译）
    /// </summary>
    public static (string Id, Regex Re)[] GetCompiledGitleaksRules()
    {
        if (_compiledGitleaksRules is null)
        {
            _compiledGitleaksRules = GitleaksRules
                .Select(r => (r.Id, new Regex(r.Pattern, r.Flags == "i" ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(5))))
                .ToArray();
        }
        return _compiledGitleaksRules;
    }

    /// <summary>
    /// 扫描内容中的密钥（对齐 TS: scanForSecrets）
    /// 返回匹配的规则列表（按规则 ID 去重），不返回匹配的密钥值本身
    /// </summary>
    public static List<SecretMatch> ScanForSecretMatches(string content)
    {
        var matches = new List<SecretMatch>();
        if (string.IsNullOrWhiteSpace(content))
            return matches;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, re) in GetCompiledGitleaksRules())
        {
            if (seen.Contains(id))
                continue;
            if (re.IsMatch(content))
            {
                seen.Add(id);
                matches.Add(new SecretMatch { RuleId = id, Label = RuleIdToLabel(id) });
            }
        }
        return matches;
    }

    /// <summary>
    /// 涂抹内容中的密钥（对齐 TS: redactSecrets）
    /// 将匹配的密钥值替换为 [REDACTED]
    /// </summary>
    public static string RedactSecrets(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        foreach (var (_, re) in GetCompiledGitleaksRules())
        {
            content = re.Replace(content, match =>
            {
                if (match.Groups.Count > 1 && match.Groups[1].Success)
                {
                    var groupValue = match.Groups[1].Value;
                    return match.Value.Replace(groupValue, "[REDACTED]", StringComparison.Ordinal);
                }
                return "[REDACTED]";
            });
        }
        return content;
    }

    #region 旧版兼容（GitSecretScanner 使用）

    public static readonly string[] SecretRegexPatterns =
    [
        @"sk-[a-zA-Z0-9]{20,}",
        @"sk-ant-[a-zA-Z0-9\-]{20,}",
        @"AKIA[0-9A-Z]{16}",
        @"ghp_[a-zA-Z0-9]{36}",
        @"gho_[a-zA-Z0-9]{36}",
        @"glpat-[a-zA-Z0-9\-]{20,}",
        @"xox[bpas]-[a-zA-Z0-9\-]{10,}",
        @"hooks\.slack\.com/services/T",
        @"eyJ[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]{10,}"
    ];

    private static readonly Regex[] _compiledSecretRegexes = SecretRegexPatterns
        .Select(p => new Regex(p, RegexOptions.None, TimeSpan.FromSeconds(5)))
        .ToArray();

    public static IReadOnlyList<Regex> CompiledSecretRegexes => _compiledSecretRegexes;

    #endregion

    private static readonly FrozenSet<string> SensitiveFileNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ".env", ".env.local", ".env.production", ".env.staging", ".env.development",
        "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa",
        "credentials.json", "auth.json", "service-account.json",
        ".npmrc", ".pypirc", ".netrc", ".htpasswd",
        "secrets.yml", "secrets.yaml", "secrets.json");

    private static readonly FrozenSet<string> SensitiveFileExtensions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ".pem", ".key", ".p12", ".pfx", ".jks");

    private static readonly FrozenSet<string> SensitivePathSegments = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ".env", ".ssh", ".git", ".gnupg",
        "credentials", "secrets", "auth",
        "id_rsa", "id_ed25519", "id_ecdsa");

    private static readonly FrozenSet<string> PrivateKeyFileNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa");

    private static readonly FrozenSet<string> CredentialFileNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "credentials.json", "auth.json", "service-account.json",
        ".npmrc", ".pypirc", ".netrc", ".htpasswd");

    /// <summary>
    /// 判断文件名是否为敏感文件（含 .env 变体、密钥文件、凭据文件等）
    /// </summary>
    public static bool IsSensitiveFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var lowerName = Path.GetFileName(fileName).ToLowerInvariant();

        if (SensitiveFileNames.Contains(lowerName))
            return true;

        var extension = Path.GetExtension(lowerName);
        if (SensitiveFileExtensions.Contains(extension))
            return true;

        if (lowerName.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// 判断路径中是否包含敏感路径段（如 .ssh、.env、credentials 等）
    /// </summary>
    public static bool IsSensitivePathSegment(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        foreach (var segment in path.Split('/', '\\'))
        {
            if (SensitivePathSegments.Contains(segment))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 判断文件是否为私钥文件（扩展名或文件名匹配）
    /// </summary>
    public static bool IsPrivateKeyFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (SensitiveFileExtensions.Contains(extension))
            return true;

        var name = Path.GetFileName(fileName).ToLowerInvariant();
        return PrivateKeyFileNames.Contains(name);
    }

    /// <summary>
    /// 检测可疑的 Windows 路径模式
    /// 对齐 TS: hasSuspiciousWindowsPathPattern — 防御路径注入攻击
    /// 包括: NTFS ADS、8.3短名、长路径前缀、尾部点/空格、DOS设备名、三点路径
    /// </summary>
    public static bool HasSuspiciousWindowsPathPattern(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // NTFS 备用数据流 (ADS): 跳过前2个字符以忽略驱动器号 (C:)
        // 示例: file.txt::$DATA, .bashrc:hidden, settings.json:stream
        var colonIndex = path.IndexOf(':', 2);
        if (colonIndex != -1)
            return true;

        // 8.3 短文件名: ~后跟数字
        // 示例: GIT~1, CLAUDE~1, SETTIN~1.JSON
        if (ShortNameRegex().IsMatch(path))
            return true;

        // 长路径前缀 (\\?\ 和 \\.\)
        // 示例: \\?\C:\Users\..., \\.\C:\...
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith("//?/", StringComparison.Ordinal)
            || path.StartsWith("//./", StringComparison.Ordinal))
            return true;

        // 尾部点或空格: Windows 在路径解析时会剥离
        // 示例: .git., .claude , .bashrc...
        if (TrailingDotOrSpaceRegex().IsMatch(path))
            return true;

        // DOS 设备名: Windows 将其视为特殊设备
        // 示例: .git.CON, settings.json.PRN, .bashrc.AUX
        if (DosDeviceNameRegex().IsMatch(path))
            return true;

        // 三个或更多连续点作为路径组件
        // 示例: .../file.txt, path/.../file
        if (ThreeDotsRegex().IsMatch(path))
            return true;

        return false;
    }

    /// <summary>
    /// 8.3 短文件名正则 (~后跟数字)
    /// </summary>
    [GeneratedRegex(@"~\d", RegexOptions.Compiled)]
    private static partial Regex ShortNameRegex();

    /// <summary>
    /// 尾部点或空格正则
    /// </summary>
    [GeneratedRegex(@"[.\s]+$", RegexOptions.Compiled)]
    private static partial Regex TrailingDotOrSpaceRegex();

    /// <summary>
    /// DOS 设备名正则 (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
    /// </summary>
    [GeneratedRegex(@"\.(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DosDeviceNameRegex();

    /// <summary>
    /// 三点路径正则 (路径分隔符之间的3+个点)
    /// </summary>
    [GeneratedRegex(@"(^|/|\\)\.{3,}(/|\\|$)", RegexOptions.Compiled)]
    private static partial Regex ThreeDotsRegex();

    /// <summary>
    /// 判断文件是否为凭据文件（credentials.json、auth.json 等）
    /// </summary>
    public static bool IsCredentialFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var name = Path.GetFileName(fileName).ToLowerInvariant();
        return CredentialFileNames.Contains(name);
    }

    public static List<SecretFinding> ScanForSecrets(string content, string filePath)
    {
        var findings = new List<SecretFinding>();

        if (string.IsNullOrWhiteSpace(content))
            return findings;

        int lineNumber = 0;
        foreach (var line in content.Split('\n'))
        {
            lineNumber++;
            var trimmedLine = line.TrimStart();

            var scanLine = trimmedLine;
            if (trimmedLine.StartsWith('+') && !trimmedLine.StartsWith("++"))
                scanLine = trimmedLine;
            else if (trimmedLine.StartsWith('-'))
                continue;
            else
                scanLine = trimmedLine;

            foreach (var regex in _compiledSecretRegexes)
            {
                var match = regex.Match(scanLine);
                if (match.Success)
                {
                    findings.Add(new SecretFinding
                    {
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        MatchedPattern = regex.ToString(),
                        MatchedContent = match.Value,
                        Type = SecretType.ApiKey
                    });
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// 计算字符串的 SHA256 短哈希（前16字符）。
    /// 对齐 TS: fileOperationAnalytics.ts — filePathHash 用于遥测路径脱敏。
    /// </summary>
    public static string ComputeShortHash(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // 取前8字节（16个十六进制字符）
        return Convert.ToHexString(hashBytes).AsSpan(0, 16).ToString();
    }
}
