namespace Mcp.Tests;

/// <summary>
/// AuthToolHandlers 单元测试 — 验证 auth_get_status/auth_refresh/auth_logout 工具
/// </summary>
public sealed class AuthToolHandlersTests {
    [Fact]
    public async Task AuthGetStatusAsync_NoConfigs_ReturnsStatus() {
        var (handler, _) = CreateHandler();
        var result = await handler.AuthGetStatusAsync();
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AuthRefreshAsync_NonExistentConfig_ReturnsError() {
        var (handler, _) = CreateHandler();
        var result = await handler.AuthRefreshAsync("nonexistent");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task AuthLogoutAsync_EmptyName_ReturnsError() {
        var (handler, _) = CreateHandler();
        var result = await handler.AuthLogoutAsync("");
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("auth_name cannot be empty");
    }

    [Fact]
    public async Task AuthLogoutAsync_UserConfirms_NonExistentConfig_ReturnsError() {
        var (handler, _) = CreateHandler();
        var result = await handler.AuthLogoutAsync("nonexistent");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task AuthLogoutAsync_UserDeclines_ReturnsCancelled() {
        var (handler, mock) = CreateHandler(confirmResult: false);
        var result = await handler.AuthLogoutAsync("test-config");
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("cancelled");
        mock.LastConfirmMessage.Should().Contain("test-config");
    }

    [Fact]
    public async Task AuthLogoutAsync_PassesConfigNameToConfirmMessage() {
        var (handler, mock) = CreateHandler(confirmResult: false);
        await handler.AuthLogoutAsync("my-auth");
        mock.LastConfirmMessage.Should().Contain("my-auth");
    }

    private static (AuthToolHandlers handler, MockUserInteractionService mock) CreateHandler(bool confirmResult = true) {
        var mcpAuth = new McpAuthToolHandlers();
        var mock = new MockUserInteractionService { ConfirmResult = confirmResult };
        var handler = new AuthToolHandlers(mcpAuth, mock);
        return (handler, mock);
    }

    private sealed class MockUserInteractionService : IUserInteractionService {
        public bool ConfirmResult { get; set; } = true;
        public string? LastConfirmMessage { get; private set; }

        public Task<UserInteractionResult> AskQuestionAsync(string question, List<string>? options = null, bool multiSelect = false, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserInteractionResult(true));

        public Task SendMessageAsync(string message, MessageType messageType = MessageType.Info, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default) {
            LastConfirmMessage = message;
            return Task.FromResult(ConfirmResult);
        }
    }
}
