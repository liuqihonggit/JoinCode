// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9001, JCC9002
namespace Host.Tests.ChatCommands;

public sealed class AddDirCommandTests : FileSystemTestBase
{
    [Fact]
    public void Name_Should_Be_add_dir()
    {
        var cmd = new AddDirCommand();
        cmd.Name.Should().Be("add-dir");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new AddDirCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new AddDirCommand();
        cmd.Usage.Should().StartWith("/add-dir");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new AddDirCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new AddDirCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void ArgumentHint_Should_Not_Be_Empty()
    {
        var cmd = new AddDirCommand();
        cmd.ArgumentHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Return_Continue()
    {
        var cmd = new AddDirCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
             FileSystem = GetFileSystem(),
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithNonExistentPath_Should_Return_Continue()
    {
        var cmd = new AddDirCommand();
        var context = new ChatCommandContext {
            Arguments = "Z:\\nonexistent\\path",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
             FileSystem = GetFileSystem(),
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WhenWorkspaceServiceIsNull_Should_Return_Continue()
    {
        var cmd = new AddDirCommand();
        var tempDir = Path.Combine(Path.GetTempPath(), "jcc-test-" + Guid.NewGuid().ToString("N")[..8]);

        if (UseRealFileSystem) Directory.CreateDirectory(tempDir);
        else InMemoryFs.CreateDirectory(tempDir);

        try
        {
            var context = new ChatCommandContext {
                Arguments = tempDir,
                CancellationToken = CancellationToken.None,
                 Services = new CommandServices
                 {
                    ChatService = Mock.Of<IChatService>(),
                    CodeService = Mock.Of<ICodeService>(),
                    PlanService = Mock.Of<IPlanService>(),
                    WorkspaceService = null,
                 FileSystem = GetFileSystem(),
                 },
            };

            var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

            result.ShouldContinue.Should().BeTrue();
            result.IsHandled.Should().BeTrue();
        }
        finally
        {
            if (UseRealFileSystem) TestFileSystem.Current.DeleteDirectory(tempDir, true);
        }
    }

    [Fact]
    public async Task Execute_WithValidPath_Should_AddDirectory()
    {
        var cmd = new AddDirCommand();
        var tempDir = Path.Combine(Path.GetTempPath(), "jcc-test-" + Guid.NewGuid().ToString("N")[..8]);

        if (UseRealFileSystem) Directory.CreateDirectory(tempDir);
        else InMemoryFs.CreateDirectory(tempDir);

        try
        {
            var workspaceService = new Mock<IWorkspaceService>();
            workspaceService.Setup(w => w.AddDirectory(It.IsAny<string>())).Returns(true);
            workspaceService.Setup(w => w.GetAdditionalDirectories())
                .Returns(Array.Empty<string>().ToList().AsReadOnly());

            var context = new ChatCommandContext {
                Arguments = tempDir,
                CancellationToken = CancellationToken.None,
                 Services = new CommandServices
                 {
                    ChatService = Mock.Of<IChatService>(),
                    CodeService = Mock.Of<ICodeService>(),
                    PlanService = Mock.Of<IPlanService>(),
                    WorkspaceService = workspaceService.Object,
                 FileSystem = GetFileSystem(),
                 },
            };

            var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

            result.ShouldContinue.Should().BeTrue();
            result.IsHandled.Should().BeTrue();
            workspaceService.Verify(w => w.AddDirectory(It.IsAny<string>()), Times.Once);
        }
        finally
        {
            if (UseRealFileSystem) TestFileSystem.Current.DeleteDirectory(tempDir, true);
        }
    }
}
