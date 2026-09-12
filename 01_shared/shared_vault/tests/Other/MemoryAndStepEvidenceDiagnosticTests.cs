namespace JoinCode.Vault.Other.Tests;

/// <summary>
/// MemoryExtensionToolHandlers 诊断方法单元测试
/// </summary>
public class MemoryExtensionToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildEmptyContentDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryExtensionToolHandlers.BuildEmptyContentDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "content");
    }

    [Fact]
    public void BuildEmptyQueryDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryExtensionToolHandlers.BuildEmptyQueryDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "query");
    }

    [Fact]
    public void BuildEmptyTeamIdDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryExtensionToolHandlers.BuildEmptyTeamIdDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "team_id");
    }

    [Fact]
    public void BuildSyncServiceNotRegisteredDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryExtensionToolHandlers.BuildSyncServiceNotRegisteredDiagnostic();
        diagnostic.Reason.Should().Be("服务不可用");
    }

    [Fact]
    public void BuildSyncServiceNotRegisteredStatusDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryExtensionToolHandlers.BuildSyncServiceNotRegisteredStatusDiagnostic();
        diagnostic.Reason.Should().Be("服务不可用");
    }
}

/// <summary>
/// CompleteStepToolHandlers 诊断方法单元测试
/// </summary>
public class CompleteStepToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildEmptyStepDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = CompleteStepToolHandlers.BuildEmptyStepDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "step");
    }

    [Fact]
    public void BuildEmptyResultDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = CompleteStepToolHandlers.BuildEmptyResultDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "result");
    }

    [Fact]
    public void BuildNoEvidenceDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = CompleteStepToolHandlers.BuildNoEvidenceDiagnostic();
        diagnostic.Reason.Should().Be("证据缺失");
        diagnostic.Suggestions.Should().HaveCount(3);
    }

    [Fact]
    public void BuildInvalidKindDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = CompleteStepToolHandlers.BuildInvalidKindDiagnostic(1, "invalid");
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "kind" && d.Value == "invalid");
    }

    [Fact]
    public void BuildEmptySummaryDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = CompleteStepToolHandlers.BuildEmptySummaryDiagnostic(2);
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "evidence_index" && d.Value == "2");
    }

    [Fact]
    public void BuildMissingVerificationCommandDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = CompleteStepToolHandlers.BuildMissingVerificationCommandDiagnostic(1);
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Suggestions.Should().HaveCount(2);
    }

    [Fact]
    public void BuildMissingPathsDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = CompleteStepToolHandlers.BuildMissingPathsDiagnostic(3, "diff");
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "kind" && d.Value == "diff");
    }
}
