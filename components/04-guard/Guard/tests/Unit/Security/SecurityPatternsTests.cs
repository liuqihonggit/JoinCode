
namespace Core.Tests.Security;

public sealed class SecurityPatternsTests
{
    [Fact]
    public void SensitiveFilePatterns_Contains_EnvFiles()
    {
        var patterns = SecurityPatterns.SensitiveFilePatterns;

        patterns.Should().Contain(".env");
        patterns.Should().Contain(".env.local");
        patterns.Should().Contain(".env.production");
    }

    [Fact]
    public void SensitiveFilePatterns_Contains_KeyFiles()
    {
        var patterns = SecurityPatterns.SensitiveFilePatterns;

        patterns.Should().Contain("*.pem");
        patterns.Should().Contain("*.key");
        patterns.Should().Contain("*.p12");
        patterns.Should().Contain("*.pfx");
    }

    [Fact]
    public void SensitiveFilePatterns_Contains_SshKeys()
    {
        var patterns = SecurityPatterns.SensitiveFilePatterns;

        patterns.Should().Contain("id_rsa");
        patterns.Should().Contain("id_ed25519");
        patterns.Should().Contain("id_ecdsa");
    }

    [Fact]
    public void SensitiveFilePatterns_Contains_CredentialFiles()
    {
        var patterns = SecurityPatterns.SensitiveFilePatterns;

        patterns.Should().Contain("credentials.json");
        patterns.Should().Contain("auth.json");
        patterns.Should().Contain("service-account.json");
    }

    [Fact]
    public void SensitiveFilePatterns_Contains_ConfigFiles()
    {
        var patterns = SecurityPatterns.SensitiveFilePatterns;

        patterns.Should().Contain(".npmrc");
        patterns.Should().Contain(".pypirc");
        patterns.Should().Contain(".netrc");
    }

    [Fact]
    public void SecretRegexPatterns_Contains_OpenAI()
    {
        var patterns = SecurityPatterns.SecretRegexPatterns;

        patterns.Should().ContainMatch("sk-*");
    }

    [Fact]
    public void SecretRegexPatterns_Contains_Anthropic()
    {
        var patterns = SecurityPatterns.SecretRegexPatterns;

        patterns.Should().ContainMatch("sk-ant-*");
    }

    [Fact]
    public void SecretRegexPatterns_Contains_Aws()
    {
        var patterns = SecurityPatterns.SecretRegexPatterns;

        patterns.Should().ContainMatch("AKIA*");
    }

    [Fact]
    public void SecretRegexPatterns_Contains_GitHub()
    {
        var patterns = SecurityPatterns.SecretRegexPatterns;

        patterns.Should().ContainMatch("ghp_*");
    }

    [Fact]
    public void CompiledSecretRegexes_IsNotEmpty()
    {
        var regexes = SecurityPatterns.CompiledSecretRegexes;

        regexes.Should().NotBeEmpty();
        regexes.Should().OnlyContain(r => r != null);
    }

    [Fact]
    public void IsSensitiveFileName_Matches_EnvFile()
    {
        SecurityPatterns.IsSensitiveFileName(".env").Should().BeTrue();
        SecurityPatterns.IsSensitiveFileName(".env.local").Should().BeTrue();
        SecurityPatterns.IsSensitiveFileName(".env.production").Should().BeTrue();
    }

    [Fact]
    public void IsSensitiveFileName_Matches_KeyFile()
    {
        SecurityPatterns.IsSensitiveFileName("server.pem").Should().BeTrue();
        SecurityPatterns.IsSensitiveFileName("private.key").Should().BeTrue();
        SecurityPatterns.IsSensitiveFileName("cert.p12").Should().BeTrue();
    }

    [Fact]
    public void IsSensitiveFileName_Matches_SshKey()
    {
        SecurityPatterns.IsSensitiveFileName("id_rsa").Should().BeTrue();
        SecurityPatterns.IsSensitiveFileName("id_ed25519").Should().BeTrue();
    }

    [Fact]
    public void IsSensitiveFileName_DoesNotMatch_SafeFile()
    {
        SecurityPatterns.IsSensitiveFileName("Program.cs").Should().BeFalse();
        SecurityPatterns.IsSensitiveFileName("README.md").Should().BeFalse();
        SecurityPatterns.IsSensitiveFileName("package.json").Should().BeFalse();
    }

    [Fact]
    public void IsSensitiveFileName_IsCaseInsensitive()
    {
        SecurityPatterns.IsSensitiveFileName(".ENV").Should().BeTrue();
        SecurityPatterns.IsSensitiveFileName("ID_RSA").Should().BeTrue();
    }

    [Fact]
    public void ScanForSecrets_Detects_OpenAIKey()
    {
        var content = "API_KEY=" + "sk-" + "abcdefghijklmnopqrstuvwxyz123456";
        var findings = SecurityPatterns.ScanForSecrets(content, "config.txt");

        findings.Should().NotBeEmpty();
        findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public void ScanForSecrets_Detects_AnthropicKey()
    {
        var content = "ANTHROPIC_KEY=" + "sk-ant-" + "api03-abcdefghijklmnopqrstuvwxyz";
        var findings = SecurityPatterns.ScanForSecrets(content, "config.txt");

        findings.Should().NotBeEmpty();
        findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public void ScanForSecrets_Detects_AwsKey()
    {
        var content = "AWS_ACCESS_KEY_ID=" + "AKIA" + "IOSFODNN7EXAMPLE";
        var findings = SecurityPatterns.ScanForSecrets(content, "config.txt");

        findings.Should().NotBeEmpty();
        findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public void ScanForSecrets_Detects_GitHubPat()
    {
        var content = "GITHUB_TOKEN=" + "ghp_" + "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghij";
        var findings = SecurityPatterns.ScanForSecrets(content, "config.txt");

        findings.Should().NotBeEmpty();
        findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public void ScanForSecrets_NoSecrets_ReturnsEmpty()
    {
        var content = "Console.WriteLine(\"Hello World\");";
        var findings = SecurityPatterns.ScanForSecrets(content, "Program.cs");

        findings.Should().BeEmpty();
    }

    // === Gitleaks 规则测试（对齐 TS: secretScanner.ts）===

    [Fact]
    public void GitleaksRules_Contains30PlusRules()
    {
        SecurityPatterns.GitleaksRules.Should().HaveCountGreaterThanOrEqualTo(30);
    }

    [Fact]
    public void ScanForSecretMatches_Detects_GitHubPat()
    {
        var content = "token=" + "ghp_" + "0123456789abcdef0123456789abcdef0123";
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        matches.Should().Contain(m => m.RuleId == "github-pat");
        matches.Should().Contain(m => m.Label == "GitHub PAT");
    }

    [Fact]
    public void ScanForSecretMatches_Detects_AWSKey()
    {
        var content = "key=" + "AKIA" + "IOSFODNN7EXAMPLE";
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        matches.Should().Contain(m => m.RuleId == "aws-access-token");
    }

    [Fact]
    public void ScanForSecretMatches_Detects_SlackBotToken()
    {
        var content = "SLACK_TOKEN=" + "xoxb-" + "1234567890-1234567890-abcdefghijklmnop";
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        matches.Should().Contain(m => m.RuleId == "slack-bot-token");
    }

    [Fact]
    public void ScanForSecretMatches_Detects_GitLabPat()
    {
        var content = "token=" + "glpat-" + "abcdefghijklmnopqrst";
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        matches.Should().Contain(m => m.RuleId == "gitlab-pat");
    }

    [Fact]
    public void ScanForSecretMatches_Detects_StripeKey()
    {
        var content = "key=" + "sk_test_" + "abcdefghijklmnopqrstuvwxyz1234567890";
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        matches.Should().Contain(m => m.RuleId == "stripe-access-token");
    }

    [Fact]
    public void ScanForSecretMatches_Detects_NpmToken()
    {
        var content = "token=" + "npm_" + "0123456789abcdef0123456789abcdef0123";
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        matches.Should().Contain(m => m.RuleId == "npm-access-token");
    }

    [Fact]
    public void ScanForSecretMatches_DeduplicatesByRuleId()
    {
        var content = "key1=" + "ghp_" + "0123456789abcdef0123456789abcdef0123 key2=" + "ghp_" + "abcdefghij0123456789abcdefghij01";
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        matches.Count(m => m.RuleId == "github-pat").Should().Be(1);
    }

    [Fact]
    public void ScanForSecretMatches_EmptyContent_ReturnsEmpty()
    {
        var matches = SecurityPatterns.ScanForSecretMatches("");
        matches.Should().BeEmpty();
    }

    [Fact]
    public void ScanForSecretMatches_SafeContent_ReturnsEmpty()
    {
        var matches = SecurityPatterns.ScanForSecretMatches("Hello World, this is safe content");
        matches.Should().BeEmpty();
    }

    [Fact]
    public void RuleIdToLabel_ConvertsCorrectly()
    {
        SecurityPatterns.RuleIdToLabel("github-pat").Should().Be("GitHub PAT");
        SecurityPatterns.RuleIdToLabel("aws-access-token").Should().Be("AWS Access Token");
        SecurityPatterns.RuleIdToLabel("openai-api-key").Should().Be("OpenAI API Key");
        SecurityPatterns.RuleIdToLabel("private-key").Should().Be("Private Key");
    }

    [Fact]
    public void RedactSecrets_ReplacesSecretWithRedacted()
    {
        var secret = "ghp_" + "0123456789abcdef0123456789abcdef0123";
        var content = "token=" + secret;
        var redacted = SecurityPatterns.RedactSecrets(content);
        redacted.Should().Contain("[REDACTED]");
        redacted.Should().NotContain(secret);
    }

    [Fact]
    public void RedactSecrets_SafeContent_ReturnsSame()
    {
        var content = "Hello World";
        var redacted = SecurityPatterns.RedactSecrets(content);
        redacted.Should().Be(content);
    }

    #region HasSuspiciousWindowsPathPattern 测试（对齐 TS: hasSuspiciousWindowsPathPattern）

    [Fact]
    public void HasSuspiciousWindowsPathPattern_NtfsAds_ReturnsTrue()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern("file.txt::$DATA").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".bashrc:hidden").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("settings.json:stream").Should().BeTrue();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_DriveLetterNotFlagged_ReturnsFalse()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern("C:\\Users\\test\\file.txt").Should().BeFalse();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("D:\\project\\src").Should().BeFalse();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_ShortName_ReturnsTrue()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern("GIT~1").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("CLAUDE~1").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("SETTIN~1.JSON").Should().BeTrue();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_LongPathPrefix_ReturnsTrue()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(@"\\?\C:\Users\test").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern(@"\\.\C:\Users\test").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("//?/C:/Users/test").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("//./C:/Users/test").Should().BeTrue();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_TrailingDotsOrSpaces_ReturnsTrue()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".git.").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".claude ").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".bashrc...").Should().BeTrue();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_DosDeviceName_ReturnsTrue()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".git.CON").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("settings.json.PRN").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".bashrc.AUX").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("file.COM1").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("file.LPT9").Should().BeTrue();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_ThreeDots_ReturnsTrue()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".../file.txt").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("path/.../file").Should().BeTrue();
        SecurityPatterns.HasSuspiciousWindowsPathPattern(@"path\...\file").Should().BeTrue();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_NormalPath_ReturnsFalse()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern("C:\\Users\\test\\file.txt").Should().BeFalse();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("/home/user/project").Should().BeFalse();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("src/main.ts").Should().BeFalse();
        SecurityPatterns.HasSuspiciousWindowsPathPattern(".gitignore").Should().BeFalse();
    }

    [Fact]
    public void HasSuspiciousWindowsPathPattern_NullOrEmpty_ReturnsFalse()
    {
        SecurityPatterns.HasSuspiciousWindowsPathPattern("").Should().BeFalse();
        SecurityPatterns.HasSuspiciousWindowsPathPattern("  ").Should().BeFalse();
    }

    #endregion

    #region ComputeShortHash

    [Fact]
    public void ComputeShortHash_NullOrEmpty_ReturnsEmpty()
    {
        SecurityPatterns.ComputeShortHash(null!).Should().BeEmpty();
        SecurityPatterns.ComputeShortHash("").Should().BeEmpty();
    }

    [Fact]
    public void ComputeShortHash_Returns16CharHex()
    {
        var hash = SecurityPatterns.ComputeShortHash("/test/file.txt");
        hash.Should().HaveLength(16);
        hash.Should().MatchRegex("^[0-9A-F]{16}$");
    }

    [Fact]
    public void ComputeShortHash_SameInput_SameOutput()
    {
        var hash1 = SecurityPatterns.ComputeShortHash("/test/file.txt");
        var hash2 = SecurityPatterns.ComputeShortHash("/test/file.txt");
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeShortHash_DifferentInput_DifferentOutput()
    {
        var hash1 = SecurityPatterns.ComputeShortHash("/test/file1.txt");
        var hash2 = SecurityPatterns.ComputeShortHash("/test/file2.txt");
        hash1.Should().NotBe(hash2);
    }

    #endregion

    #region MatchesVcsInternalPath 测试

    [Fact]
    public void MatchesVcsInternalPath_DotGitSegment_ReturnsTrue()
    {
        SecurityPatterns.MatchesVcsInternalPath(".git").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath(".git/config").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("project/.git/HEAD").Should().BeTrue();
    }

    [Fact]
    public void MatchesVcsInternalPath_DotSvnSegment_ReturnsTrue()
    {
        SecurityPatterns.MatchesVcsInternalPath(".svn").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("project/.svn/entries").Should().BeTrue();
    }

    [Fact]
    public void MatchesVcsInternalPath_DotHgSegment_ReturnsTrue()
    {
        SecurityPatterns.MatchesVcsInternalPath(".hg").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("project/.hg/store").Should().BeTrue();
    }

    [Fact]
    public void MatchesVcsInternalPath_BackslashSeparator_ReturnsTrue()
    {
        SecurityPatterns.MatchesVcsInternalPath(@"project\.git\HEAD").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath(@"project\.svn\entries").Should().BeTrue();
    }

    [Fact]
    public void MatchesVcsInternalPath_CaseInsensitive_ReturnsTrue()
    {
        SecurityPatterns.MatchesVcsInternalPath(".GIT").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath(".Git/config").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("project/.SVN").Should().BeTrue();
    }

    [Fact]
    public void MatchesVcsInternalPath_NormalPath_ReturnsFalse()
    {
        SecurityPatterns.MatchesVcsInternalPath("src/main.ts").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath("README.md").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath(".gitignore").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath("project/Program.cs").Should().BeFalse();
    }

    [Fact]
    public void MatchesVcsInternalPath_NullOrEmpty_ReturnsFalse()
    {
        SecurityPatterns.MatchesVcsInternalPath(null).Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath("").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath("   ").Should().BeFalse();
    }

    [Fact]
    public void MatchesVcsInternalPath_RootPath_ReturnsFalse()
    {
        SecurityPatterns.MatchesVcsInternalPath("/").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath(@"\\").Should().BeFalse();
    }

    #endregion
}
