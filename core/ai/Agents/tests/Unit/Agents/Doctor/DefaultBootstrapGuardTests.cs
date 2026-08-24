namespace Core.Tests.Agents.Doctor;

using JoinCode.Abstractions.Interfaces.Doctor;

public class DefaultBootstrapGuardTests
{
    [Fact]
    public async Task ReviewAsync_GuardFile_Rejected()
    {
        var guard = CreateGuard();
        var request = CreateRequest(targetPath: "/src/Guard/PermissionChecker.cs");

        var decision = await guard.ReviewAsync(request);

        Assert.False(decision.Approved);
        Assert.Contains("安全守卫", decision.Reason);
    }

    [Fact]
    public async Task ReviewAsync_VaultFile_Rejected()
    {
        var guard = CreateGuard();
        var request = CreateRequest(targetPath: "/src/Vault/SecretStore.cs");

        var decision = await guard.ReviewAsync(request);

        Assert.False(decision.Approved);
    }

    [Fact]
    public async Task ReviewAsync_RemovedRegisterAttribute_Rejected()
    {
        var guard = CreateGuard();
        var request = CreateRequest(
            originalContent: "[Register(typeof(IFoo), ServiceLifetime.Singleton)]\npublic class Foo { }",
            proposedContent: "public class Foo { }"
        );

        var decision = await guard.ReviewAsync(request);

        Assert.False(decision.Approved);
        Assert.Contains("[Register]", decision.Reason);
    }

    [Fact]
    public async Task ReviewAsync_CsprojFile_Rejected()
    {
        var guard = CreateGuard();
        var request = CreateRequest(targetPath: "/src/Foo.csproj");

        var decision = await guard.ReviewAsync(request);

        Assert.False(decision.Approved);
        Assert.Contains("项目配置", decision.Reason);
    }

    [Fact]
    public async Task ReviewAsync_UnbalancedBraces_Rejected()
    {
        var guard = CreateGuard();
        var request = CreateRequest(proposedContent: "class Foo { { {");

        var decision = await guard.ReviewAsync(request);

        Assert.False(decision.Approved);
        Assert.Contains("语法", decision.Reason);
    }

    [Fact]
    public async Task ReviewAsync_ValidModification_Approved()
    {
        var guard = CreateGuard();
        var request = CreateRequest(
            originalContent: "class Foo { void Bar() { } }",
            proposedContent: "class Foo { void Bar() { var x = 1; } }"
        );

        var decision = await guard.ReviewAsync(request);

        Assert.True(decision.Approved);
    }

    [Fact]
    public async Task ReviewAsync_LargeChange_ApprovedWithWarning()
    {
        var guard = CreateGuard();
        var originalLines = new string[60];
        var proposedLines = new string[60];
        for (var i = 0; i < 60; i++)
        {
            originalLines[i] = $"class Foo{i} {{ }}";
            proposedLines[i] = $"class Bar{i} {{ }}";
        }
        var original = string.Join("\n", originalLines);
        var proposed = string.Join("\n", proposedLines);
        var request = CreateRequest(originalContent: original, proposedContent: proposed);

        var decision = await guard.ReviewAsync(request);

        Assert.True(decision.Approved);
        Assert.NotEmpty(decision.Warnings);
    }

    [Fact]
    public void IsGuardOrVaultFile_GuardPath_ReturnsTrue()
    {
        Assert.True(DefaultBootstrapGuard.IsGuardOrVaultFile("/src/Guard/Permission.cs"));
        Assert.True(DefaultBootstrapGuard.IsGuardOrVaultFile("/src/Vault/Secrets.cs"));
    }

    [Fact]
    public void IsGuardOrVaultFile_NormalPath_ReturnsFalse()
    {
        Assert.False(DefaultBootstrapGuard.IsGuardOrVaultFile("/src/Doctor/DiagnosticEngine.cs"));
    }

    [Fact]
    public void CountChangedLines_SingleLineChange_Returns1()
    {
        var changed = DefaultBootstrapGuard.CountChangedLines("line1\nline2", "line1\nline2_changed");
        Assert.Equal(1, changed);
    }

    [Fact]
    public void RemovedRegisterAttribute_Removed_ReturnsTrue()
    {
        var result = DefaultBootstrapGuard.RemovedRegisterAttribute(
            "[Register] class Foo { }",
            "class Foo { }"
        );
        Assert.True(result);
    }

    [Fact]
    public void IsProjectConfigFile_Csproj_ReturnsTrue()
    {
        Assert.True(DefaultBootstrapGuard.IsProjectConfigFile("/src/Foo.csproj"));
        Assert.True(DefaultBootstrapGuard.IsProjectConfigFile("/src/Directory.Build.props"));
    }

    [Fact]
    public void BasicSyntaxCheck_BalancedBraces_ReturnsTrue()
    {
        Assert.True(DefaultBootstrapGuard.BasicSyntaxCheck("class Foo { void Bar() { } }"));
    }

    [Fact]
    public void BasicSyntaxCheck_EmptyContent_ReturnsFalse()
    {
        Assert.False(DefaultBootstrapGuard.BasicSyntaxCheck(""));
    }

    private static DefaultBootstrapGuard CreateGuard()
    {
        return new DefaultBootstrapGuard(new InMemoryFileSystem());
    }

    private static BootstrapModificationRequest CreateRequest(
        string targetPath = "/src/Doctor/DiagnosticEngine.cs",
        string originalContent = "class Original { }",
        string proposedContent = "class Modified { }")
    {
        return new BootstrapModificationRequest
        {
            ModificationType = BootstrapFixType.SourceCodePatch,
            TargetPath = targetPath,
            OriginalContent = originalContent,
            ProposedContent = proposedContent,
            Justification = "Fix the bug"
        };
    }
}
