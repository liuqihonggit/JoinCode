
namespace Core.Tests.Security;

public sealed class GitSecretScannerTests
{
    private readonly GitSecretScanner _scanner = new(NullLogger<GitSecretScanner>.Instance);

    [Fact]
    public async Task ScanFileNamesAsync_EnvFile_ReturnsBlocked()
    {
        var stagedFiles = new List<string> { ".env", "Program.cs", "README.md" };

        var result = await _scanner.ScanFileNamesAsync(stagedFiles).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.FilePath == ".env");
        result.Findings.Should().Contain(f => f.Type == SecretType.SensitiveFile);
    }

    [Fact]
    public async Task ScanFileNamesAsync_MultipleSensitiveFiles_ReturnsAll()
    {
        var stagedFiles = new List<string> { ".env", "id_rsa", "credentials.json" };

        var result = await _scanner.ScanFileNamesAsync(stagedFiles).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().HaveCount(3);
    }

    [Fact]
    public async Task ScanFileNamesAsync_SafeFiles_ReturnsSafe()
    {
        var stagedFiles = new List<string> { "Program.cs", "README.md", "appsettings.json" };

        var result = await _scanner.ScanFileNamesAsync(stagedFiles).ConfigureAwait(true);

        result.IsBlocked.Should().BeFalse();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanFileNamesAsync_PemFile_ReturnsBlocked()
    {
        var stagedFiles = new List<string> { "server.pem" };

        var result = await _scanner.ScanFileNamesAsync(stagedFiles).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.PrivateKey);
    }

    [Fact]
    public async Task ScanFileNamesAsync_KeyFile_ReturnsBlocked()
    {
        var stagedFiles = new List<string> { "private.key" };

        var result = await _scanner.ScanFileNamesAsync(stagedFiles).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.PrivateKey);
    }

    [Fact]
    public async Task ScanFileNamesAsync_CredentialFile_ReturnsBlocked()
    {
        var stagedFiles = new List<string> { "auth.json" };

        var result = await _scanner.ScanFileNamesAsync(stagedFiles).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.Credential);
    }

    [Fact]
    public async Task ScanContentAsync_OpenAIKey_ReturnsBlocked()
    {
        var diff = @"diff --git a/config.txt b/config.txt
new file mode 100644
--- /dev/null
+++ b/config.txt
@@ -0,0 +1 @@
+API_KEY=sk-abcdefghijklmnopqrstuvwxyz123456";

        var result = await _scanner.ScanContentAsync(diff).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public async Task ScanContentAsync_AnthropicKey_ReturnsBlocked()
    {
        var diff = "+ANTHROPIC_API_KEY=sk-ant-api03-abcdefghijklmnopqrstuvwxyz123456";

        var result = await _scanner.ScanContentAsync(diff).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public async Task ScanContentAsync_AwsKey_ReturnsBlocked()
    {
        var diff = "+AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE";

        var result = await _scanner.ScanContentAsync(diff).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public async Task ScanContentAsync_GitHubPat_ReturnsBlocked()
    {
        var diff = "+GITHUB_TOKEN=ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghij";

        var result = await _scanner.ScanContentAsync(diff).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public async Task ScanContentAsync_SafeContent_ReturnsSafe()
    {
        var diff = @"diff --git a/Program.cs b/Program.cs
--- a/Program.cs
+++ b/Program.cs
@@ -1,1 +1,2 @@
+Console.WriteLine(""Hello World"");";

        var result = await _scanner.ScanContentAsync(diff).ConfigureAwait(true);

        result.IsBlocked.Should().BeFalse();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanContentAsync_EmptyDiff_ReturnsSafe()
    {
        var result = await _scanner.ScanContentAsync(string.Empty).ConfigureAwait(true);

        result.IsBlocked.Should().BeFalse();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanContentAsync_IgnoresRemovedLines()
    {
        var diff = "-API_KEY=sk-abcdefghijklmnopqrstuvwxyz123456";

        var result = await _scanner.ScanContentAsync(diff).ConfigureAwait(true);

        result.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ScanContentAsync_DetectsLineNumber()
    {
        var diff = @"diff --git a/config.txt b/config.txt
@@ -0,0 +1,3 @@
+var x = 1;
+API_KEY=sk-abcdefghijklmnopqrstuvwxyz123456
+var z = 3;";

        var result = await _scanner.ScanContentAsync(diff).ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.LineNumber.HasValue);
    }
}
