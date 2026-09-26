namespace McpToolDispatch.Tests;

/// <summary>
/// GitHubRunCachePaths sessionId 隔离单元测试 — 验证不同 session 缓存路径隔离
/// </summary>
public sealed class GitHubRunCachePathsTest {
    /// <summary>
    /// 有 SubAgentContext 时,缓存路径应含 sessionId 子目录
    /// <para>红测试: 当前 GetCacheDir 不含 sessionId,多 session 写同一文件冲突</para>
    /// </summary>
    [Fact]
    public void GetCacheDir_WithSubAgentContext_ContainsSessionId() {
        var fs = new InMemoryFileSystem();
        var ctx = new SubAgentContext {
            AgentId = "test-agent",
            Role = AgentRole.Coordinator,
            Task = "test",
            SessionId = "session-AAA"
        };
        using var scope = ctx.EnterScope();

        var dir = GitHubRunCachePaths.GetCacheDir(fs, workingDir: null);

        dir.Should().Contain("session-AAA", "缓存路径应含 sessionId 子目录以隔离不同 session");
    }

    /// <summary>
    /// 不同 sessionId 的缓存路径应不同(隔离)
    /// </summary>
    [Fact]
    public void GetCacheDir_DifferentSessionIds_AreIsolated() {
        var fs = new InMemoryFileSystem();

        string dirA, dirB;
        var ctxA = new SubAgentContext {
            AgentId = "a", Role = AgentRole.Coordinator, Task = "t", SessionId = "session-A"
        };
        using (var scope = ctxA.EnterScope()) {
            dirA = GitHubRunCachePaths.GetCacheDir(fs, workingDir: null);
        }

        var ctxB = new SubAgentContext {
            AgentId = "b", Role = AgentRole.Coordinator, Task = "t", SessionId = "session-B"
        };
        using (var scope = ctxB.EnterScope()) {
            dirB = GitHubRunCachePaths.GetCacheDir(fs, workingDir: null);
        }

        dirA.Should().NotBe(dirB, "不同 sessionId 应隔离到不同缓存目录");
        dirA.Should().Contain("session-A");
        dirB.Should().Contain("session-B");
    }
}
