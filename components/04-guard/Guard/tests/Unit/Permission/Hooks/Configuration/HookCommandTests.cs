
namespace Core.Tests.Hooks.Configuration;

/// <summary>
/// HookCommand 测试
/// </summary>
public class HookCommandTests
{
    [Fact]
    public void BashCommandHook_ShouldHaveCorrectType()
    {
        // Arrange
        var hook = new BashCommandHook
        {
            Command = "echo test"
        };

        // Assert
        hook.Type.Should().Be(HookTypeConstants.Command);
    }

    [Fact]
    public void BashCommandHook_GetDisplayText_WithStatusMessage_ShouldReturnStatusMessage()
    {
        // Arrange
        var hook = new BashCommandHook
        {
            Command = "echo test",
            StatusMessage = "Running test command"
        };

        // Act
        var result = hook.GetDisplayText();

        // Assert
        result.Should().Be("Running test command");
    }

    [Fact]
    public void BashCommandHook_GetDisplayText_WithoutStatusMessage_ShouldReturnCommand()
    {
        // Arrange
        var hook = new BashCommandHook
        {
            Command = "echo test"
        };

        // Act
        var result = hook.GetDisplayText();

        // Assert
        result.Should().Be("echo test");
    }

    [Fact]
    public void BashCommandHook_IsEqualTo_SameCommand_ShouldReturnTrue()
    {
        // Arrange
        var hook1 = new BashCommandHook { Command = "git status", Shell = ShellToolNameConstants.ShellExecute };
        var hook2 = new BashCommandHook { Command = "git status", Shell = ShellToolNameConstants.ShellExecute };

        // Act
        var result = hook1.IsEqualTo(hook2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void BashCommandHook_IsEqualTo_DifferentCommand_ShouldReturnFalse()
    {
        // Arrange
        var hook1 = new BashCommandHook { Command = "git status" };
        var hook2 = new BashCommandHook { Command = "git log" };

        // Act
        var result = hook1.IsEqualTo(hook2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void PromptHook_ShouldHaveCorrectType()
    {
        // Arrange
        var hook = new PromptHook
        {
            Prompt = "Validate this command"
        };

        // Assert
        hook.Type.Should().Be(HookTypeConstants.Prompt);
    }

    [Fact]
    public void AgentHook_ShouldHaveCorrectType()
    {
        // Arrange
        var hook = new AgentHook
        {
            Prompt = "Check security"
        };

        // Assert
        hook.Type.Should().Be(HookTypeConstants.Agent);
    }

    [Fact]
    public void HttpHook_ShouldHaveCorrectType()
    {
        // Arrange
        var hook = new HttpHook
        {
            Url = "https://example.com/hook"
        };

        // Assert
        hook.Type.Should().Be(HookTypeConstants.Http);
    }

    [Fact]
    public void FunctionHook_ShouldHaveCorrectType()
    {
        // Arrange
        var hook = new FunctionHook
        {
            Id = "test-hook",
            Callback = (input, ct) => Task.FromResult(HookResult.Success())
        };

        // Assert
        hook.Type.Should().Be(HookTypeConstants.Function);
    }

    [Fact]
    public void HookCommand_Properties_ShouldBeSettable()
    {
        // Arrange & Act
        var hook = new BashCommandHook
        {
            Command = "echo test",
            If = "Bash(git *)",
            Timeout = 10,
            StatusMessage = "Testing",
            Once = true
        };

        // Assert
        hook.Command.Should().Be("echo test");
        hook.If.Should().Be("Bash(git *)");
        hook.Timeout.Should().Be(10);
        hook.StatusMessage.Should().Be("Testing");
        hook.Once.Should().BeTrue();
    }
}
