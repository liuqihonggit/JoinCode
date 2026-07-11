
namespace Core.Tests.Ssh;

public sealed class SshCommandResultTests
{
    [Fact]
    public void IsSuccess_WithZeroExitCode_ReturnsTrue()
    {
        var result = new SshCommandResult
        {
            Command = "ls",
            ExitCode = 0,
            Stdout = "file1\nfile2",
            Stderr = "",
            Duration = TimeSpan.FromMilliseconds(100)
        };

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(255)]
    public void IsSuccess_WithNonZeroExitCode_ReturnsFalse(int exitCode)
    {
        var result = new SshCommandResult
        {
            Command = "ls",
            ExitCode = exitCode,
            Stdout = "",
            Stderr = "error",
            Duration = TimeSpan.FromMilliseconds(50)
        };

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Constructor_SetsDefaults()
    {
        var result = new SshCommandResult
        {
            Command = "test",
            ExitCode = 0
        };

        result.Stdout.Should().BeEmpty();
        result.Stderr.Should().BeEmpty();
        result.Duration.Should().Be(TimeSpan.Zero);
        result.IsSuccess.Should().BeTrue();
    }
}
