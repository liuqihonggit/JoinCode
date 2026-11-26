namespace CliNodes.Tests;

/// <summary>
/// 策略信任目录 — 锁定「宿主只认构建期生成的策略哈希」这条完整性边界（ADR 0168）。
/// </summary>
public sealed class CliToolPolicyTrustTests {
    [Fact]
    public void TrustCatalog_ShippedPolicyHashMatchesEmbeddedCopy() {
        var hash = HashEmbeddedPolicy("CliNodes.Tests.policy.file.yaml");

        Assert.Null(CliToolPolicyTrustCatalog.Default.Verify("cli.file", hash, "jcc-tool-file.exe"));
        Assert.NotNull(CliToolPolicyTrustCatalog.Default.Verify("cli.file", new string('0', 64), "jcc-tool-file.exe"));
        Assert.NotNull(CliToolPolicyTrustCatalog.Default.Verify("cli.unknown", hash, "jcc-tool-file.exe"));
        Assert.NotNull(CliToolPolicyTrustCatalog.Default.Verify("cli.file", hash, "evil.exe"));
    }

    [Fact]
    public void Reader_ReportsHashMatchingTrustCatalog() {
        var loaded = CliToolPolicyResourceReader.LoadWithHashFromExecutable(
            typeof(CliToolPolicyTrustTests).Assembly.Location,
            "jcc-tool-file.exe");

        Assert.Equal("cli.file", loaded.Manifest.PolicyId);
        Assert.Equal(HashEmbeddedPolicy("CliNodes.Tests.policy.file.yaml"), loaded.Sha256Hex);
        Assert.Null(CliToolPolicyTrustCatalog.Default.Verify(
            loaded.Manifest.PolicyId, loaded.Sha256Hex, "jcc-tool-file.exe"));
    }

    [Fact]
    public async Task Broker_TrustEnforced_RejectsPolicyOutsideCatalog() {
        await using var broker = new CliAuthorizationBroker(enforcePolicyTrust: true);
        var session = broker.CreateSession(new CliAuthorizationScope {
            AgentId = "agent-test",
            SessionId = "session-test"
        });

        var decision = await CliToolAuthorizationClient.AuthorizeAsync(
            session, "file", "read",
            typeof(CliToolPolicyTrustTests).Assembly.Location,
            "JoinCode.CliNodes.Tests.dll",
            new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("Policy.Trust", decision.Layer);
        Assert.Contains("信任目录", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Broker_TrustEnforced_AllowsShippedPolicyWithMatchingHash() {
        await using var broker = new CliAuthorizationBroker(enforcePolicyTrust: true);
        var session = broker.CreateSession(new CliAuthorizationScope {
            AgentId = "agent-test",
            SessionId = "session-test"
        });

        var decision = await CliToolAuthorizationClient.AuthorizeAsync(
            session, "file", "read",
            typeof(CliToolPolicyTrustTests).Assembly.Location,
            "jcc-tool-file.exe",
            new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
            CancellationToken.None);

        Assert.True(decision.Allowed, decision.Reason);
    }

    private static string HashEmbeddedPolicy(string resourceName) {
        using var stream = typeof(CliToolPolicyTrustTests).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"测试资源不存在: {resourceName}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }
}
