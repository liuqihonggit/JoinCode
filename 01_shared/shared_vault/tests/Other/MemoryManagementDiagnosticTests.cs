namespace JoinCode.Vault.Other.Tests;

/// <summary>
/// MemoryManagementToolHandlers 诊断方法单元测试
/// </summary>
public class MemoryManagementToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildEmptyQueryDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryManagementToolHandlers.BuildEmptyQueryDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "query");
    }

    [Fact]
    public void BuildCleanupConfirmRequiredDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryManagementToolHandlers.BuildCleanupConfirmRequiredDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
    }

    [Fact]
    public void BuildEmptyTeamIdDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryManagementToolHandlers.BuildEmptyTeamIdDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "team_id");
    }

    [Fact]
    public void BuildEmptyPathDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryManagementToolHandlers.BuildEmptyPathDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "path");
    }

    [Fact]
    public void BuildTeamNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = MemoryManagementToolHandlers.BuildTeamNotFoundDiagnostic();
        diagnostic.Reason.Should().Be("团队未找到");
    }
}
