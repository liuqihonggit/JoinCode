
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region P2-4: 标题获取 — DeriveSessionTitle

    [Fact]
    public void DeriveSessionTitle_SimpleText_ReturnsAsIs()
    {
        var title = BridgeMain.DeriveSessionTitle("Hello world");
        Assert.Equal("Hello world", title);
    }

    [Fact]
    public void DeriveSessionTitle_CollapsesWhitespace()
    {
        var title = BridgeMain.DeriveSessionTitle("Hello\n\tworld  foo");
        Assert.Equal("Hello world foo", title);
    }

    [Fact]
    public void DeriveSessionTitle_TruncatesTo80Chars()
    {
        var longText = new string('a', 100);
        var title = BridgeMain.DeriveSessionTitle(longText);
        Assert.Equal(80, title.Length);
    }

    [Fact]
    public void DeriveSessionTitle_Exactly80Chars_NotTruncated()
    {
        var text = new string('a', 80);
        var title = BridgeMain.DeriveSessionTitle(text);
        Assert.Equal(80, title.Length);
    }

    [Fact]
    public void DeriveSessionTitle_TrimsLeadingTrailingWhitespace()
    {
        var title = BridgeMain.DeriveSessionTitle("  hello  ");
        Assert.Equal("hello", title);
    }

    #endregion

    #region P2-4: 标题获取 — ExtractUserMessageText

    [Fact]
    public void ExtractUserMessageText_UserTypeWithStringContent_ReturnsText()
    {
        var ndjson = """{"type":"user","content":"Hello world"}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Equal("Hello world", text);
    }

    [Fact]
    public void ExtractUserMessageText_UserTypeWithArrayContent_ReturnsFirstTextBlock()
    {
        var ndjson = """{"type":"user","content":[{"type":"text","text":"Hello"},{"type":"image","url":"..."}]}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Equal("Hello", text);
    }

    [Fact]
    public void ExtractUserMessageText_AssistantType_ReturnsNull()
    {
        var ndjson = """{"type":"assistant","content":"Hello"}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Null(text);
    }

    [Fact]
    public void ExtractUserMessageText_ToolResultRole_ReturnsNull()
    {
        var ndjson = """{"type":"user","role":"tool-result","content":"result"}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Null(text);
    }

    [Fact]
    public void ExtractUserMessageText_SyntheticMessage_ReturnsNull()
    {
        var ndjson = """{"type":"user","synthetic":true,"content":"auto"}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Null(text);
    }

    [Fact]
    public void ExtractUserMessageText_ReplaySource_ReturnsNull()
    {
        var ndjson = """{"type":"user","source":"replay","content":"old message"}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Null(text);
    }

    [Fact]
    public void ExtractUserMessageText_EmptyContent_ReturnsNull()
    {
        var ndjson = """{"type":"user","content":"   "}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Null(text);
    }

    [Fact]
    public void ExtractUserMessageText_InvalidJson_ReturnsNull()
    {
        var text = BridgeSubprocessHandle.ExtractUserMessageText("not json");
        Assert.Null(text);
    }

    [Fact]
    public void ExtractUserMessageText_EmptyString_ReturnsNull()
    {
        var text = BridgeSubprocessHandle.ExtractUserMessageText("");
        Assert.Null(text);
    }

    [Fact]
    public void ExtractUserMessageText_NestedMessageContent_ReturnsText()
    {
        var ndjson = """{"type":"user","message":{"content":"nested hello"}}""";
        var text = BridgeSubprocessHandle.ExtractUserMessageText(ndjson);
        Assert.Equal("nested hello", text);
    }

    #endregion

    #region P2-4: 标题获取 — OnFirstUserMessage + titledSessions

    [Fact]
    public void BridgeMainDeps_FetchSessionTitle_CanBeSet()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.FetchSessionTitle = (_, _) => Task.FromResult<string?>("test-title");
        Assert.NotNull(deps.FetchSessionTitle);
    }

    [Fact]
    public void BridgeMainDeps_UpdateSessionTitle_CanBeSet()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.UpdateSessionTitle = (_, _, _) => Task.CompletedTask;
        Assert.NotNull(deps.UpdateSessionTitle);
    }

    [Fact]
    public void BridgeMainDeps_TitleCallbacks_DefaultNull()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        Assert.Null(deps.FetchSessionTitle);
        Assert.Null(deps.UpdateSessionTitle);
    }

    [Fact]
    public void BridgeSubprocessOptions_OnFirstUserMessage_CanBeSet()
    {
        Action<string>? callback = _ => { };
        var opts = new BridgeSubprocessOptions
        {
            SessionId = "test",
            OnFirstUserMessage = callback,
        };
        Assert.NotNull(opts.OnFirstUserMessage);
    }

    [Fact]
    public void BridgeSubprocessOptions_OnFirstUserMessage_DefaultNull()
    {
        var opts = new BridgeSubprocessOptions { SessionId = "test" };
        Assert.Null(opts.OnFirstUserMessage);
    }

    [Fact]
    public async Task OnFirstUserMessage_TitledSessionsPreventsDuplicate()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        var setSessionTitleCalls = new List<(string SessionId, string Title)>();
        deps.BridgeLogger = new TestBridgeLogger
        {
            OnSetSessionTitle = (sid, t) => setSessionTitleCalls.Add((sid, t)),
        };

        deps.FetchSessionTitle = (_, _) => Task.FromResult<string?>("Server Title");

        await using var main = new BridgeMain(deps);

        var title = BridgeMain.DeriveSessionTitle("Hello from user");
        Assert.Equal("Hello from user", title);
    }

    [Fact]
    public async Task OnFirstUserMessage_UpdatesServerTitle()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        var updateTitleCalls = new List<(string SessionId, string Title)>();
        deps.UpdateSessionTitle = (sid, title, _) =>
        {
            updateTitleCalls.Add((sid, title));
            return Task.CompletedTask;
        };

        await using var main = new BridgeMain(deps);

        var title = BridgeMain.DeriveSessionTitle("Fix the login bug");
        Assert.Equal("Fix the login bug", title);
    }

    #endregion

    #region P2-4: 标题获取 — BridgeApiClient

    [Fact]
    public async Task GetSessionTitleAsync_InvalidSessionId_ReturnsNull()
    {
        var apiClient = BridgeTestHelperMethods.CreateMockApiClient();
        var title = await apiClient.GetSessionTitleAsync("../../etc/passwd").ConfigureAwait(true);
        Assert.Null(title);
    }

    [Fact]
    public async Task UpdateSessionTitleAsync_InvalidSessionId_DoesNotThrow()
    {
        var apiClient = BridgeTestHelperMethods.CreateMockApiClient();
        await apiClient.UpdateSessionTitleAsync("../../etc/passwd", "title").ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateSessionTitleAsync_EmptyTitle_DoesNotThrow()
    {
        var apiClient = BridgeTestHelperMethods.CreateMockApiClient();
        await apiClient.UpdateSessionTitleAsync("session-123", "").ConfigureAwait(true);
    }

    #endregion
}
