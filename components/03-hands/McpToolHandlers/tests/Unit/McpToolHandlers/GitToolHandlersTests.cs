namespace Sync.Tests.ToolHandlers;

/// <summary>
/// GitSubCommand 枚举 + GitToolHandlers 参数构建 单元测试
/// </summary>
public sealed class GitToolHandlersTests
{
    // === GitSubCommand 枚举源码生成器测试 ===

    [Fact]
    public void GitSubCommand_ToValue_AllValues()
    {
        // 验证每个枚举值映射到正确的字符串
        GitSubCommand.Status.ToValue().Should().Be("status");
        GitSubCommand.Add.ToValue().Should().Be("add");
        GitSubCommand.Commit.ToValue().Should().Be("commit");
        GitSubCommand.Push.ToValue().Should().Be("push");
        GitSubCommand.Pull.ToValue().Should().Be("pull");
        GitSubCommand.Log.ToValue().Should().Be("log");
        GitSubCommand.Diff.ToValue().Should().Be("diff");
        GitSubCommand.Branch.ToValue().Should().Be("branch");
        GitSubCommand.Switch.ToValue().Should().Be("switch");
        GitSubCommand.Clone.ToValue().Should().Be("clone");
        GitSubCommand.Reset.ToValue().Should().Be("reset");
        GitSubCommand.Clean.ToValue().Should().Be("clean");
        GitSubCommand.LsFiles.ToValue().Should().Be("ls-files");
        GitSubCommand.RevParse.ToValue().Should().Be("rev-parse");
        GitSubCommand.RevList.ToValue().Should().Be("rev-list");
        GitSubCommand.SparseCheckout.ToValue().Should().Be("sparse-checkout");
    }

    [Fact]
    public void GitSubCommand_FromValue_KnownValues()
    {
        // 验证 FromValue 返回正确的枚举值
        GitSubCommandExtensions.FromValue("status").Should().Be(GitSubCommand.Status);
        GitSubCommandExtensions.FromValue("add").Should().Be(GitSubCommand.Add);
        GitSubCommandExtensions.FromValue("commit").Should().Be(GitSubCommand.Commit);
        GitSubCommandExtensions.FromValue("push").Should().Be(GitSubCommand.Push);
        GitSubCommandExtensions.FromValue("pull").Should().Be(GitSubCommand.Pull);
        GitSubCommandExtensions.FromValue("log").Should().Be(GitSubCommand.Log);
        GitSubCommandExtensions.FromValue("diff").Should().Be(GitSubCommand.Diff);
        GitSubCommandExtensions.FromValue("branch").Should().Be(GitSubCommand.Branch);
        GitSubCommandExtensions.FromValue("switch").Should().Be(GitSubCommand.Switch);
        GitSubCommandExtensions.FromValue("clone").Should().Be(GitSubCommand.Clone);
        GitSubCommandExtensions.FromValue("reset").Should().Be(GitSubCommand.Reset);
        GitSubCommandExtensions.FromValue("clean").Should().Be(GitSubCommand.Clean);
    }

    [Fact]
    public void GitSubCommand_FromValue_UnknownValue()
    {
        // 验证未知值返回 null
        GitSubCommandExtensions.FromValue("unknown").Should().BeNull();
        GitSubCommandExtensions.FromValue("not_a_command").Should().BeNull();
    }

    [Fact]
    public void GitSubCommand_FromValue_CaseInsensitive()
    {
        // 源码生成器使用 OrdinalIgnoreCase，大小写不敏感匹配
        GitSubCommandExtensions.FromValue("STATUS").Should().Be(GitSubCommand.Status);
        GitSubCommandExtensions.FromValue("Add").Should().Be(GitSubCommand.Add);
        GitSubCommandExtensions.FromValue("COMMIT").Should().Be(GitSubCommand.Commit);
    }

    [Fact]
    public void GitSubCommand_AllEnumValues_HaveToValueMapping()
    {
        // 验证所有枚举值都能通过 ToValue 映射到非空字符串
        var allValues = Enum.GetValues<GitSubCommand>();
        allValues.Should().HaveCount(16);

        // 验证每个值都有映射，且与 GitSubCommandConstants 常量一致
        foreach (var value in allValues)
        {
            var mapped = value.ToValue();
            mapped.Should().NotBeNullOrEmpty($"枚举值 {value} 应有 ToValue 映射");
        }

        // 验证关键映射与 Constants 常量一致
        GitSubCommand.Status.ToValue().Should().Be(GitSubCommandConstants.Status);
        GitSubCommand.Add.ToValue().Should().Be(GitSubCommandConstants.Add);
        GitSubCommand.Commit.ToValue().Should().Be(GitSubCommandConstants.Commit);
        GitSubCommand.Push.ToValue().Should().Be(GitSubCommandConstants.Push);
        GitSubCommand.Clean.ToValue().Should().Be(GitSubCommandConstants.Clean);
    }

    // === GitToolHandlers 参数验证测试 ===

    private readonly GitToolHandlers _handler = new(new IO.FileSystem.PhysicalFileSystem(), new NoOpProcessService());

    [Fact]
    public async Task GitToolHandlers_GitAdd_EmptyPath_ReturnsError()
    {
        // 空路径应返回错误
        var result = await _handler.GitAddAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task GitToolHandlers_GitAdd_WhitespacePath_ReturnsError()
    {
        // 纯空白路径应返回错误
        var result = await _handler.GitAddAsync("   ", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task GitToolHandlers_GitCommit_EmptyMessage_ReturnsError()
    {
        // 空提交消息应返回错误
        var result = await _handler.GitCommitAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task GitToolHandlers_GitCommit_WhitespaceMessage_ReturnsError()
    {
        // 纯空白提交消息应返回错误
        var result = await _handler.GitCommitAsync("   ", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task GitToolHandlers_GitBranch_EmptyBranchName_ReturnsError()
    {
        // 空分支名应返回错误
        var result = await _handler.GitBranchAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task GitToolHandlers_GitBranch_WhitespaceBranchName_ReturnsError()
    {
        // 纯空白分支名应返回错误
        var result = await _handler.GitBranchAsync("   ", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task GitToolHandlers_GitClone_EmptyUrl_ReturnsError()
    {
        // 空 URL 应返回错误
        var result = await _handler.GitCloneAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task GitToolHandlers_GitClone_WhitespaceUrl_ReturnsError()
    {
        // 纯空白 URL 应返回错误
        var result = await _handler.GitCloneAsync("   ", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }
}
