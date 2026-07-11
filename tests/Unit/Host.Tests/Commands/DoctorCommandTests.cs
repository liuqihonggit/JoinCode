
namespace Core.Tests.Commands;

public class DoctorCommandTests
{
    private readonly DoctorCommand _command;

    public DoctorCommandTests()
    {
        _command = new DoctorCommand();
    }

    private static ChatCommandContext CreateContext()
    {
        return new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = null!,
                CodeService = null!,
                PlanService = null!,
             FileSystem = TestFileSystem.Current,
             },
        };
    }

    [Fact]
    public void Name_ShouldBeDoctor()
    {
        _command.Name.Should().Be("doctor");
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        _command.Description.Should().NotBeNullOrEmpty();
        _command.Description.Should().Contain("诊断");
    }

    [Fact]
    public void Usage_ShouldContainDoctor()
    {
        _command.Usage.Should().Contain("doctor");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnContinue()
    {
        var context = CreateContext();
        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);
        result.Should().BeEquivalentTo(ChatCommandResult.Continue());
    }
}
