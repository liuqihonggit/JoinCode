// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace JoinCode.Tests.Guard;

public class SearchScopeValidatorTests {
    private readonly SearchScopeValidator _validator = new();

    [Fact]
    public void Validate_RgWithNoIgnoreFlag_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse("rg --no-ignore \"test\"");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
        Assert.Contains("--no-ignore", risk.Details);
    }

    [Fact]
    public void Validate_RgWithShortUnrestrictedFlag_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse("rg -u \"test\"");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
        Assert.Contains("-u", risk.Details);
    }

    [Fact]
    public void Validate_RgWithSystemRootPath_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse(@"rg ""test"" C:\");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
        Assert.Contains("C:\\", risk.Details);
    }

    [Fact]
    public void Validate_RgWithUsersPath_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse(@"rg ""test"" C:\Users");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
    }

    [Fact]
    public void Validate_RgNormalSearch_ReturnsNull() {
        var cmd = ShellCommand.Parse("rg \"test\" src/");
        Assert.False(_validator.TryValidate(cmd, @"D:\project\w3", out var result));

        Assert.Null(result);
    }

    [Fact]
    public void Validate_RgWithProjectPath_ReturnsNull() {
        var cmd = ShellCommand.Parse(@"rg ""test"" D:\project\w3\core");
        Assert.False(_validator.TryValidate(cmd, @"D:\project\w3", out var result));

        Assert.Null(result);
    }

    [Fact]
    public void Validate_GrepWithRecursiveFlag_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse("grep -r \"test\" /home");
        Assert.True(_validator.TryValidate(cmd, "/home/user/project", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
    }

    [Fact]
    public void Validate_FindWithUnixRootPath_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse("find / -name \"*.cs\"");
        Assert.True(_validator.TryValidate(cmd, "/home/user/project", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
    }

    [Fact]
    public void Validate_NonSearchCommand_ReturnsNull() {
        var cmd = ShellCommand.Parse("dotnet build");
        Assert.False(_validator.TryValidate(cmd, @"D:\project\w3", out var result));

        Assert.Null(result);
    }

    [Fact]
    public void Validate_RgNoIgnoreParentFlag_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse("rg --no-ignore-parent \"test\"");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
        Assert.Contains("--no-ignore-parent", risk.Details);
    }

    [Fact]
    public void Validate_RgWithNoIgnoreAndRootPath_ReturnsExcessiveScopeWithBothRisks() {
        var cmd = ShellCommand.Parse(@"rg --no-ignore ""test"" C:\");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
        Assert.Contains("--no-ignore", risk.Details);
        Assert.Contains("C:\\", risk.Details);
    }

    [Fact]
    public void Validate_SuggestionProvided_WhenDangerousFlagsDetected() {
        var cmd = ShellCommand.Parse("rg --no-ignore \"test\"");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.False(string.IsNullOrEmpty(risk.Suggestion));
    }

    [Fact]
    public void Validate_AgWithUnrestrictedFlag_ReturnsExcessiveScope() {
        var cmd = ShellCommand.Parse("ag -u \"test\"");
        Assert.True(_validator.TryValidate(cmd, "/home/user/project", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
    }

    [Fact]
    public void Validate_FdCommand_ReturnsNullForNormalUsage() {
        var cmd = ShellCommand.Parse("fd \"*.cs\" src/");
        Assert.False(_validator.TryValidate(cmd, @"D:\project\w3", out var result));

        Assert.Null(result);
    }

    [Fact]
    public void Validate_DriveRootPattern_DetectsAllDrives() {
        var cmd = ShellCommand.Parse(@"rg ""test"" D:\");
        Assert.True(_validator.TryValidate(cmd, @"D:\project\w3", out var result));
        var risk = result ?? throw new InvalidOperationException("应检测到风险");
        Assert.Equal(CommandRisk.ExcessiveSearchScope, risk.Risk);
    }
}