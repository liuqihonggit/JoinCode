namespace Guard.Tests.Permission.Permission;

public sealed class ToolPermissionFilterTests
{
    private readonly ToolPermissionFilter _sut;

    public ToolPermissionFilterTests()
    {
        _sut = new ToolPermissionFilter(NullLogger<ToolPermissionFilter>.Instance);
    }

    [Fact]
    public void IsToolDenied_NoRules_ShouldReturnFalse()
    {
        var result = _sut.IsToolDenied("any_tool");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsToolDenied_ExactMatchRule_ShouldReturnTrue()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-shell",
            ToolPattern = ShellToolNameConstants.ShellExecute,
            IsRegex = false
        });

        var result = _sut.IsToolDenied(ShellToolNameConstants.ShellExecute);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsToolDenied_NonMatchingRule_ShouldReturnFalse()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-shell",
            ToolPattern = ShellToolNameConstants.ShellExecute,
            IsRegex = false
        });

        var result = _sut.IsToolDenied(FileToolNameConstants.FileRead);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsToolDenied_WildcardPattern_ShouldMatch()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-all-shell",
            ToolPattern = "*sh*",
            IsRegex = false
        });

        _sut.IsToolDenied(ShellToolNameConstants.ShellExecute).Should().BeTrue();
        _sut.IsToolDenied("shell_bash").Should().BeTrue();
        _sut.IsToolDenied(FileToolNameConstants.FileRead).Should().BeFalse();
    }

    [Fact]
    public void IsToolDenied_RegexPattern_ShouldMatch()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-regex",
            ToolPattern = @"^(Bash|PowerShell|shell_|powershell_).*$",
            IsRegex = true
        });

        _sut.IsToolDenied(ShellToolNameConstants.ShellExecute).Should().BeTrue();
        _sut.IsToolDenied(ShellToolNameConstants.Powershell).Should().BeTrue();
        _sut.IsToolDenied("powershell_run").Should().BeTrue();
        _sut.IsToolDenied(FileToolNameConstants.FileRead).Should().BeFalse();
    }

    [Fact]
    public void IsToolDenied_InvalidRegex_ShouldNotThrow()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "bad-regex",
            ToolPattern = "[invalid(regex",
            IsRegex = true
        });

        var act = () => _sut.IsToolDenied("any_tool");

        act.Should().NotThrow();
    }

    [Fact]
    public void IsToolDenied_WithPermissionMode_ShouldOnlyMatchMatchingMode()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-plan-write",
            ToolPattern = FileToolNameConstants.FileWrite,
            PermissionMode = "plan",
            IsRegex = false
        });

        _sut.IsToolDenied(FileToolNameConstants.FileWrite, "plan").Should().BeTrue();
        _sut.IsToolDenied(FileToolNameConstants.FileWrite, "auto").Should().BeFalse();
        _sut.IsToolDenied(FileToolNameConstants.FileWrite).Should().BeFalse();
    }

    [Fact]
    public void IsToolDenied_CaseInsensitiveMatch_ShouldMatch()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-shell",
            ToolPattern = ShellToolNameConstants.ShellExecute,
            IsRegex = false
        });

        _sut.IsToolDenied(ShellToolNameConstants.ShellExecute).Should().BeTrue();
        _sut.IsToolDenied(ShellToolNameConstants.ShellExecute).Should().BeTrue();
    }

    [Fact]
    public void FilterToolsByDenyRules_ShouldFilterDeniedTools()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-shell",
            ToolPattern = ShellToolNameConstants.ShellExecute,
            IsRegex = false
        });

        var tools = new List<string> { FileToolNameConstants.FileRead, ShellToolNameConstants.ShellExecute, FileToolNameConstants.FileWrite, SearchToolNameConstants.Grep };
        var result = _sut.FilterToolsByDenyRules(tools);

        result.Should().NotContain(ShellToolNameConstants.ShellExecute);
        result.Should().Contain(FileToolNameConstants.FileRead);
        result.Should().Contain(FileToolNameConstants.FileWrite);
        result.Should().Contain(SearchToolNameConstants.Grep);
    }

    [Fact]
    public void FilterToolsByDenyRules_NoRules_ShouldReturnAllTools()
    {
        var tools = new List<string> { FileToolNameConstants.FileRead, ShellToolNameConstants.ShellExecute, FileToolNameConstants.FileWrite };
        var result = _sut.FilterToolsByDenyRules(tools);

        result.Should().BeEquivalentTo(tools);
    }

    [Fact]
    public void AddDenyRule_NullRule_ShouldThrowArgumentNullException()
    {
        var act = () => _sut.AddDenyRule(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveDenyRule_ShouldRemoveMatchingRule()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-shell",
            ToolPattern = ShellToolNameConstants.ShellExecute,
            IsRegex = false
        });

        _sut.IsToolDenied(ShellToolNameConstants.ShellExecute).Should().BeTrue();

        _sut.RemoveDenyRule("deny-shell");

        _sut.IsToolDenied(ShellToolNameConstants.ShellExecute).Should().BeFalse();
    }

    [Fact]
    public void RemoveDenyRule_NullOrEmptyName_ShouldThrowArgumentException()
    {
        var act1 = () => _sut.RemoveDenyRule(null!);
        var act2 = () => _sut.RemoveDenyRule("");
        var act3 = () => _sut.RemoveDenyRule("   ");

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RemoveDenyRule_NonExistentRule_ShouldNotThrow()
    {
        var act = () => _sut.RemoveDenyRule("nonexistent");

        act.Should().NotThrow();
    }

    [Fact]
    public void FilterToolsByDenyRules_WithPermissionMode_ShouldFilterModeSpecificRules()
    {
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-plan-write",
            ToolPattern = FileToolNameConstants.FileWrite,
            PermissionMode = "plan",
            IsRegex = false
        });
        _sut.AddDenyRule(new ToolDenyRule
        {
            RuleName = "deny-shell-global",
            ToolPattern = ShellToolNameConstants.ShellExecute,
            IsRegex = false
        });

        var tools = new List<string> { FileToolNameConstants.FileRead, FileToolNameConstants.FileWrite, ShellToolNameConstants.ShellExecute };

        var planResult = _sut.FilterToolsByDenyRules(tools, "plan");
        planResult.Should().NotContain(FileToolNameConstants.FileWrite);
        planResult.Should().NotContain(ShellToolNameConstants.ShellExecute);
        planResult.Should().Contain(FileToolNameConstants.FileRead);

        var autoResult = _sut.FilterToolsByDenyRules(tools, "auto");
        autoResult.Should().Contain(FileToolNameConstants.FileWrite);
        autoResult.Should().NotContain(ShellToolNameConstants.ShellExecute);
    }
}
