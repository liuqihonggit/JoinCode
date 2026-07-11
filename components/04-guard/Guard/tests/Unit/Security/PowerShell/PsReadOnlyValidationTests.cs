namespace Guard.Tests.Security.PowerShell;

/// <summary>
/// PsReadOnlyValidation 单元测试 — 验证只读命令白名单
/// </summary>
public class PsReadOnlyValidationTests
{
    [Fact]
    public void IsReadOnlyCommand_GetProcess_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Get-Process"));
    }

    [Fact]
    public void IsReadOnlyCommand_GetChildItem_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Get-ChildItem"));
    }

    [Fact]
    public void IsReadOnlyCommand_SetContent_ReturnsFalse()
    {
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Set-Content -Path test.txt -Value 'hello'"));
    }

    [Fact]
    public void IsReadOnlyCommand_RemoveItem_ReturnsFalse()
    {
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Remove-Item test.txt"));
    }

    [Fact]
    public void IsReadOnlyCommand_EmptyCommand_ReturnsFalse()
    {
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand(""));
    }

    [Fact]
    public void IsReadOnlyCommand_WithSubExpression_ReturnsFalse()
    {
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Get-Process $(Get-Date)"));
    }

    [Fact]
    public void IsReadOnlyCommand_WithScriptBlock_ReturnsFalse()
    {
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Get-Process | Where-Object { $_.CPU -gt 10 }"));
    }

    [Fact]
    public void IsReadOnlyCommand_PipelineOfReadOnlyCmdlets_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Get-Process | Select-Object Name"));
    }

    [Fact]
    public void IsReadOnlyCommand_ExternalExe_ReturnsFalse()
    {
        // 外部可执行文件（除 where.exe）不在白名单
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("notepad.exe"));
    }

    [Fact]
    public void IsReadOnlyCommand_TestPath_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Test-Path test.txt"));
    }

    [Fact]
    public void IsReadOnlyCommand_WriteHostWithVariable_ReturnsFalse()
    {
        // Write-Host 带 argLeaksValue 守卫，含 $ 的参数应拒绝
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Write-Host $env:PATH"));
    }

    [Fact]
    public void IsReadOnlyCommand_WriteHostWithLiteral_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("Write-Host 'hello'"));
    }

    [Fact]
    public void IsReadOnlyCommand_GitStatus_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("git status"));
    }

    [Fact]
    public void IsReadOnlyCommand_DotnetVersion_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsReadOnlyCommand("dotnet --version"));
    }

    [Fact]
    public void IsCwdChangingCmdlet_SetLocation_ReturnsTrue()
    {
        Assert.True(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsCwdChangingCmdlet("Set-Location"));
    }

    [Fact]
    public void IsCwdChangingCmdlet_GetProcess_ReturnsFalse()
    {
        Assert.False(JoinCode.Guard.Security.PowerShell.PsReadOnlyValidation.IsCwdChangingCmdlet("Get-Process"));
    }
}
