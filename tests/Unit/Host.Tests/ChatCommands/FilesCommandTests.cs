namespace Host.Tests.ChatCommands;

public sealed class FilesCommandTests
{
    [Fact]
    public void Name_Should_Be_files()
    {
        var cmd = new FilesCommand();
        cmd.Name.Should().Be("files");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new FilesCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new FilesCommand();
        cmd.Usage.Should().StartWith("/files");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new FilesCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new FilesCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WhenTrackerIsNull_Should_Return_Continue()
    {
        var cmd = new FilesCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileOperationTracker = null,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WhenNoEntries_Should_Return_Continue()
    {
        var cmd = new FilesCommand();
        var tracker = new Mock<IFileOperationTracker>();
        tracker.Setup(t => t.GetAllEntries()).Returns(Array.Empty<FileOperationEntry>().ToList().AsReadOnly());
        tracker.Setup(t => t.GetOperatedFilePaths()).Returns(Array.Empty<string>().ToList().AsReadOnly());

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileOperationTracker = tracker.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEntries_Should_Return_Continue()
    {
        var cmd = new FilesCommand();
        var entries = new List<FileOperationEntry>
        {
            new() { FilePath = "C:\\test\\file1.cs", OperationType = FileOperationType.Read, Timestamp = DateTime.UtcNow.AddMinutes(-5) },
            new() { FilePath = "C:\\test\\file1.cs", OperationType = FileOperationType.Edit, Timestamp = DateTime.UtcNow.AddMinutes(-1) },
            new() { FilePath = "C:\\test\\file2.cs", OperationType = FileOperationType.Write, Timestamp = DateTime.UtcNow.AddMinutes(-3) },
        };

        var paths = new List<string> { "C:\\test\\file1.cs", "C:\\test\\file2.cs" };

        var tracker = new Mock<IFileOperationTracker>();
        tracker.Setup(t => t.GetAllEntries()).Returns(entries.AsReadOnly());
        tracker.Setup(t => t.GetOperatedFilePaths()).Returns(paths.AsReadOnly());

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileOperationTracker = tracker.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }
}