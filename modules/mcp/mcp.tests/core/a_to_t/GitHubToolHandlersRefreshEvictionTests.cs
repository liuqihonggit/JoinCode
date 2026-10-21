namespace Mcp.Tests;

/// <summary>
/// gh 工具 refresh 强制刷新测试 — 缺陷修复守卫：
/// pr checks / pr view 此前无 refresh 参数（CLI --refresh 报未知选项），且 watch 轮询与普通查询
/// 均命中 API 缓存（detail 10min）导致状态冻结（PR 已合并仍 open / run 已 cancelled 仍 pending）。
/// 修复 = refresh=true 时 evict API 缓存 key 后重拉。
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    /// <summary>构造带内存 KV 缓存的 handler（真实走 GhCacheService 路径，非 NoOp）</summary>
    private static (GitHubToolHandlers Handler, FakeGitHubApiClient Api) CreateCachedHandler() {
        var api = new FakeGitHubApiClient();
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), api,
            git: null, logger: NullLogger<GitHubToolHandlers>.Instance, kvStore: new InMemoryKvStore());
        return (handler, api);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试断言取首个文本非空")]
    [Fact]
    public async Task PrChecks_WithoutRefresh_HitsApiCache() {
        var (handler, api) = CreateCachedHandler();
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc1"}}""" });
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","conclusion":"success"}]}""" });
        var first = await handler.GhPrChecksAsync("42", common: new GitHubCommonOptions { Repo = "owner/repo" });
        first.GetFirstText()!.Should().Contain("1 通过");

        // 换新响应但缓存 TTL 内 — 不带 refresh 应命中缓存不消费队列
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc1"}}""" });
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","conclusion":"failure"}]}""" });
        var second = await handler.GhPrChecksAsync("42", common: new GitHubCommonOptions { Repo = "owner/repo" });
        second.GetFirstText()!.Should().Contain("1 通过", "缓存命中返回旧状态（TTL 内）");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试断言取首个文本非空")]
    [Fact]
    public async Task PrChecks_WithRefresh_EvictsCacheAndRefetches() {
        var (handler, api) = CreateCachedHandler();
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc1"}}""" });
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","conclusion":"success"}]}""" });
        await handler.GhPrChecksAsync("42", common: new GitHubCommonOptions { Repo = "owner/repo" });

        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc1"}}""" });
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","conclusion":"failure"}]}""" });
        var refreshed = await handler.GhPrChecksAsync("42", refresh: true, common: new GitHubCommonOptions { Repo = "owner/repo" });
        refreshed.GetFirstText()!.Should().Contain("1 失败", "refresh=true 应 evict 缓存并重拉最新 check-runs");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试断言取首个文本非空")]
    [Fact]
    public async Task PrView_WithRefresh_EvictsCacheAndRefetches() {
        var (handler, api) = CreateCachedHandler();
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":42,"state":"open","title":"t"}""" });
        var first = await handler.GhPrViewAsync("42", common: new GitHubCommonOptions { Repo = "owner/repo" });
        first.GetFirstText()!.Should().Contain("open");

        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":42,"state":"closed","title":"t"}""" });
        var refreshed = await handler.GhPrViewAsync("42", refresh: true, common: new GitHubCommonOptions { Repo = "owner/repo" });
        refreshed.GetFirstText()!.Should().Contain("closed", "refresh=true 应 evict 缓存并重拉最新 PR 状态");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试断言取首个文本非空")]
    [Fact]
    public async Task PrChecks_WatchMode_EachRoundBypassesCache() {
        // watch 轮询语义要求看最新状态 — 内层每轮强制 refresh，不得命中缓存冻结状态
        var (handler, api) = CreateCachedHandler();
        // 第 1 轮：pending；第 2 轮：success（interval 最小 1s，两轮各 enq 两个响应）
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc1"}}""" });
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","status":"in_progress"}]}""" });
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc1"}}""" });
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","conclusion":"success"}]}""" });

        var result = await handler.GhPrChecksAsync("42", watch: true, interval: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        var text = result.GetFirstText()!;
        text.Should().Contain("1 通过", "watch 第二轮应看到最新 success 状态（轮询绕缓存+完成判定精确子串）");
    }
}
