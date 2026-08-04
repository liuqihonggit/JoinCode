namespace JoinCode.Tests.Guard;

public class SearchScopeValidatorTests
{
    private readonly SearchScopeValidator _validator = new();

    [Fact]
    public void Validate_RgWithNoIgnoreFlag_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse("rg --no-ignore \"test\"");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
        Assert.Contains("--no-ignore", result.Details);
    }

    [Fact]
    public void Validate_RgWithShortUnrestrictedFlag_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse("rg -u \"test\"");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
        Assert.Contains("-u", result.Details);
    }

    [Fact]
    public void Validate_RgWithSystemRootPath_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse(@"rg ""test"" C:\");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
        Assert.Contains("C:\\", result.Details);
    }

    [Fact]
    public void Validate_RgWithUsersPath_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse(@"rg ""test"" C:\Users");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
    }

    [Fact]
    public void Validate_RgNormalSearch_ReturnsNull()
    {
        var cmd = ShellCommand.Parse("rg \"test\" src/");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.Null(result);
    }

    [Fact]
    public void Validate_RgWithProjectPath_ReturnsNull()
    {
        var cmd = ShellCommand.Parse(@"rg ""test"" D:\project\w3\core");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.Null(result);
    }

    [Fact]
    public void Validate_GrepWithRecursiveFlag_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse("grep -r \"test\" /home");
        var result = _validator.Validate(cmd, "/home/user/project");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
    }

    [Fact]
    public void Validate_FindWithUnixRootPath_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse("find / -name \"*.cs\"");
        var result = _validator.Validate(cmd, "/home/user/project");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
    }

    [Fact]
    public void Validate_NonSearchCommand_ReturnsNull()
    {
        var cmd = ShellCommand.Parse("dotnet build");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.Null(result);
    }

    [Fact]
    public void Validate_RgNoIgnoreParentFlag_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse("rg --no-ignore-parent \"test\"");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
        Assert.Contains("--no-ignore-parent", result.Details);
    }

    [Fact]
    public void Validate_RgWithNoIgnoreAndRootPath_ReturnsExcessiveScopeWithBothRisks()
    {
        var cmd = ShellCommand.Parse(@"rg --no-ignore ""test"" C:\");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
        Assert.Contains("--no-ignore", result.Details);
        Assert.Contains("C:\\", result.Details);
    }

    [Fact]
    public void Validate_SuggestionProvided_WhenDangerousFlagsDetected()
    {
        var cmd = ShellCommand.Parse("rg --no-ignore \"test\"");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Suggestion));
    }

    [Fact]
    public void Validate_AgWithUnrestrictedFlag_ReturnsExcessiveScope()
    {
        var cmd = ShellCommand.Parse("ag -u \"test\"");
        var result = _validator.Validate(cmd, "/home/user/project");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
    }

    [Fact]
    public void Validate_FdCommand_ReturnsNullForNormalUsage()
    {
        var cmd = ShellCommand.Parse("fd \"*.cs\" src/");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.Null(result);
    }

    [Fact]
    public void Validate_DriveRootPattern_DetectsAllDrives()
    {
        var cmd = ShellCommand.Parse(@"rg ""test"" D:\");
        var result = _validator.Validate(cmd, @"D:\project\w3");

        Assert.NotNull(result);
        Assert.Equal(CommandRisk.ExcessiveSearchScope, result.Risk);
    }
}
