using Core.Security.Services;

namespace JoinCode.Tests.Guard;

public class CommandClassifierSearchScopeTests
{
    private readonly CommandClassifier _classifier;

    public CommandClassifierSearchScopeTests()
    {
        var pathValidator = new StubPathValidator();
        var destructiveDetector = new StubDestructiveCommandDetector();
        var readOnlyDetector = new StubReadOnlyCommandDetector();
        var searchScopeValidator = new SearchScopeValidator();

        _classifier = new CommandClassifier(
            pathValidator,
            destructiveDetector,
            readOnlyDetector,
            searchScopeValidator);
    }

    [Fact]
    public void Classify_RgNoIgnore_ReturnsExcessiveSearchScope()
    {
        var cmd = ShellCommand.Parse("rg --no-ignore \"test\"");
        var result = _classifier.Classify(cmd, @"D:\project\w3");

        Assert.Equal(CommandCategory.ExcessiveSearchScope, result.Category);
        Assert.Contains(CommandRisk.ExcessiveSearchScope, result.Risks);
    }

    [Fact]
    public void Classify_RgUnrestrictedFlag_ReturnsExcessiveSearchScope()
    {
        var cmd = ShellCommand.Parse("rg -u \"test\"");
        var result = _classifier.Classify(cmd, @"D:\project\w3");

        Assert.Equal(CommandCategory.ExcessiveSearchScope, result.Category);
    }

    [Fact]
    public void Classify_RgSystemRootPath_ReturnsExcessiveSearchScope()
    {
        var cmd = ShellCommand.Parse(@"rg ""test"" C:\");
        var result = _classifier.Classify(cmd, @"D:\project\w3");

        Assert.Equal(CommandCategory.ExcessiveSearchScope, result.Category);
    }

    [Fact]
    public void Classify_RgNormalSearch_ReturnsUnknown()
    {
        var cmd = ShellCommand.Parse("rg \"test\" src/");
        var result = _classifier.Classify(cmd, @"D:\project\w3");

        Assert.Equal(CommandCategory.Unknown, result.Category);
    }

    [Fact]
    public void Classify_GrepRecursiveOnHomeDir_ReturnsExcessiveSearchScope()
    {
        var cmd = ShellCommand.Parse("grep -r \"test\" /home");
        var result = _classifier.Classify(cmd, "/home/user/project");

        Assert.Equal(CommandCategory.ExcessiveSearchScope, result.Category);
    }

    [Fact]
    public void Classify_FindOnUnixRoot_ReturnsExcessiveSearchScope()
    {
        var cmd = ShellCommand.Parse("find / -name \"*.cs\"");
        var result = _classifier.Classify(cmd, "/home/user/project");

        Assert.Equal(CommandCategory.ExcessiveSearchScope, result.Category);
    }

    [Fact]
    public void Classify_NonSearchCommand_ReturnsUnknown()
    {
        var cmd = ShellCommand.Parse("dotnet build");
        var result = _classifier.Classify(cmd, @"D:\project\w3");

        Assert.Equal(CommandCategory.Unknown, result.Category);
    }

    [Fact]
    public void Classify_DestructiveCommand_StillDetectedAsDestructive()
    {
        var destructiveDetector = new StubDestructiveCommandDetector(isDestructive: true);
        var classifier = new CommandClassifier(
            new StubPathValidator(),
            destructiveDetector,
            new StubReadOnlyCommandDetector(),
            new SearchScopeValidator());

        var cmd = ShellCommand.Parse("rm -rf /tmp/test");
        var result = classifier.Classify(cmd, "/home/user/project");

        Assert.Equal(CommandCategory.Destructive, result.Category);
    }

    [Fact]
    public void Classify_WithoutSearchScopeValidator_ReturnsUnknownForSearchCommands()
    {
        var classifier = new CommandClassifier(
            new StubPathValidator(),
            new StubDestructiveCommandDetector(),
            new StubReadOnlyCommandDetector(),
            searchScopeValidator: null);

        var cmd = ShellCommand.Parse("rg --no-ignore \"test\"");
        var result = classifier.Classify(cmd, @"D:\project\w3");

        Assert.Equal(CommandCategory.Unknown, result.Category);
    }

    private sealed class StubPathValidator : IPathValidator
    {
        public ValidationResult ValidatePaths(ShellCommand command, string workingDirectory)
            => ValidationResult.Valid();
        public bool IsPathWithinWorkspace(string path, string workingDirectory) => true;
    }

    private sealed class StubDestructiveCommandDetector : IDestructiveCommandDetector
    {
        private readonly bool _isDestructive;
        public StubDestructiveCommandDetector(bool isDestructive = false) => _isDestructive = isDestructive;
        public DestructiveCommandResult Detect(ShellCommand command)
            => new(_isDestructive, _isDestructive ? [CommandRisk.FileDeletion] : []);
    }

    private sealed class StubReadOnlyCommandDetector : IReadOnlyCommandDetector
    {
        public bool IsReadOnly(ShellCommand command) => false;
        public ShellPermissionCheckResult CheckReadOnlyConstraints(string command, bool compoundCommandHasCd = false)
            => new(PermissionBehavior.Passthrough);
    }
}
