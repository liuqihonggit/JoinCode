// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9001, JCC9002
namespace Guard.Tests.Security.PowerShell;

/// <summary>
/// PsPathValidation 单元测试 — 验证路径约束检查
/// </summary>
public class PsPathValidationTests
{
    private static readonly string WorkDir = Directory.GetCurrentDirectory();

    [Fact]
    public void CheckPathConstraints_SimpleReadCommand_ReturnsPassthrough()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "Get-Process", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Passthrough, result.Behavior);
    }

    [Fact]
    public void Debug_UncPath_ParseResult()
    {
        var parsed = JoinCode.Guard.Security.PowerShell.PsAstParser.Parse(@"Get-Content -Path \\server\share\file.txt");
        Assert.True(parsed.Valid, $"Parse failed: {string.Join(", ", parsed.Errors?.Select(e => e.Message) ?? [])}");
        Assert.NotEmpty(parsed.Statements);
        var stmt = parsed.Statements[0];
        Assert.NotEmpty(stmt.Commands);
        var cmd = stmt.Commands[0];
        Assert.Equal("Get-Content", cmd.Name);
        Assert.True(cmd.Args.Length > 0, $"No args found. ElementTypes: {string.Join(", ", cmd.ElementTypes)}");
    }

    [Fact]
    public void CheckPathConstraints_UncPath_ReturnsDeny()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            @"Get-Content -Path \\server\share\file.txt", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_VariableInPath_ReturnsAsk()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "Get-Content -Path $filePath", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Ask, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_WriteWithGlob_ReturnsDeny()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "Set-Content -Path *.txt -Value 'hello'", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_NonFsProviderPath_ReturnsAsk()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "Get-Content -Path env:PATH", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Ask, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_DenyDirectory_ReturnsDeny()
    {
        var denyDirs = new List<string> { @"C:\Windows" };
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            @"Get-Content -Path C:\Windows\System32\config\SAM", WorkDir, [], denyDirs);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_WorkDirRead_ReturnsPassthrough()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "Get-Content -Path test.txt", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Passthrough, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_BacktickInPath_Passthrough()
    {
        // SMA Parser 会自动剥离反引号转义，PsPathExtractor 收到的是已解析的路径
        // 所以反引号路径在 AST 层面已经被规范化了
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "Get-Content -Path te`st.txt", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Passthrough, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_EmptyCommand_ReturnsPassthrough()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "", WorkDir, [], []);
        Assert.Equal(PermissionBehavior.Passthrough, result.Behavior);
    }

    [Fact]
    public void CheckPathConstraints_AllowedDirectory_ReturnsPassthrough()
    {
        var allowedDirs = new List<string> { WorkDir };
        var result = JoinCode.Guard.Security.PowerShell.PsPathValidation.CheckPathConstraints(
            "Get-Content -Path test.txt", WorkDir, allowedDirs, []);
        Assert.Equal(PermissionBehavior.Passthrough, result.Behavior);
    }
}
