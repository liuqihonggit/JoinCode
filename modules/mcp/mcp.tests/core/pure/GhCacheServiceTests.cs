// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Mcp.Tests;

/// <summary>
/// GhCacheService 统一缓存服务测试 — 验证 API/Graphql/JobLog/RunLog/Jobs/反查索引/告警/清理 的
/// key 构造、TTL 策略、命中/未命中、NoOp(kvStore=null) 行为及异常容错。
/// <para>使用 InMemoryKvStore(纯内存,0磁盘IO)作为 IKvStore 测试替身,确定性测试。</para>
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GhCacheServiceTest {
    private static readonly byte[] SampleLog = Encoding.UTF8.GetBytes("line1\nline2\n##[error] boom");
    private static readonly byte[] SampleJson = Encoding.UTF8.GetBytes("""{"jobs":[{"id":1}]}""");

    /// <summary>创建带 InMemoryKvStore 的 GhCacheService,返回两者供断言</summary>
    private static (GhCacheService Service, InMemoryKvStore Kv) CreateService() {
        var kv = new InMemoryKvStore();
        return (new GhCacheService(kv, null), kv);
    }

    /// <summary>从 TTL 包装值提取过期时间(前8字节 UTC Ticks)</summary>
    private static DateTime ExtractExpiry(byte[] wrapped) =>
        new(BitConverter.ToInt64(wrapped, 0), DateTimeKind.Utc);

    /// <summary>断言 expiry 在预期 TTL 容差范围内(±5秒,容忍写入/断言间延迟)</summary>
    private static void AssertTtl(byte[] wrapped, TimeSpan expectedTtl) {
        var expiry = ExtractExpiry(wrapped);
        var now = DateTime.UtcNow;
        (expiry - now).Should().BeCloseTo(expectedTtl, TimeSpan.FromSeconds(5),
            "TTL 应为 {0}", expectedTtl);
    }

    // === NoOp 行为(kvStore=null) ===

    [Fact]
    public async Task NoOp_NullKvStore_AllTryGetReturnNull() {
        var svc = new GhCacheService(null, null);
        var query = new Dictionary<string, string> { ["page"] = "1" };

        (await svc.TryGetApiAsync("/repos/x/y", query, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetGraphqlAsync("query{}", CancellationToken.None)).Should().BeNull();
        (await svc.TryGetJobLogAsync(42, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunIdByJobIdAsync(42, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunLogAsync("42", CancellationToken.None)).Should().BeNull();
        (await svc.TryGetJobsAsync(42, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunIdByPrAsync(7, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunIdByShaAsync("abc123", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task NoOp_NullKvStore_AllWriteNoOp_ScanEmpty() {
        var svc = new GhCacheService(null, null);
        var query = new Dictionary<string, string> { ["page"] = "1" };

        // 所有 Write 不应抛异常(NoOp)
        await svc.WriteApiAsync("/repos/x/y", query, "body", CancellationToken.None);
        await svc.WriteGraphqlAsync("query{}", "resp", CancellationToken.None);
        await svc.WriteJobLogAsync(42, 100, SampleLog, CancellationToken.None);
        await svc.WriteRunLogAsync("42", SampleLog, CancellationToken.None);
        await svc.WriteJobsAsync(42, SampleJson, CancellationToken.None);
        await svc.WriteRunIdByPrAsync(7, "100", CancellationToken.None);
        await svc.WriteRunIdByShaAsync("abc123", "100", CancellationToken.None);
        await svc.WriteAlertAsync("gh:ci_alert:1:2:3", SampleLog, CancellationToken.None);
        await svc.DeleteAlertAsync(Encoding.UTF8.GetBytes("gh:ci_alert:1:2:3"), CancellationToken.None);
        await svc.DeleteAsync(Encoding.UTF8.GetBytes("gh:api:x"), CancellationToken.None);

        // Scan 应为空
        (await CollectAsync(svc.ScanAlertsAsync(CancellationToken.None))).Should().BeEmpty();
        (await CollectAsync(svc.ScanAllGhAsync(CancellationToken.None))).Should().BeEmpty();
    }

    // === API 级缓存 ===

    [Fact]
    public async Task TryGetApiAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetApiAsync("/repos/x/y/issues", null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteApiAsync_ThenTryGetApiAsync_HitReturnsBody() {
        var (svc, _) = CreateService();
        await svc.WriteApiAsync("/repos/x/y/issues", null, "issue-list", CancellationToken.None);
        var hit = await svc.TryGetApiAsync("/repos/x/y/issues", null, CancellationToken.None);
        hit.Should().Be("issue-list");
    }

    [Fact]
    public async Task WriteApiAsync_WithQuery_KeyIncludesSortedQuery_HitOnSameQuery() {
        var (svc, _) = CreateService();
        // 乱序 query,缓存 key 应按 key 排序构造
        var query = new Dictionary<string, string> { ["per_page"] = "100", ["page"] = "2", ["state"] = "open" };
        await svc.WriteApiAsync("/repos/x/y/issues", query, "sorted-result", CancellationToken.None);

        // 相同内容不同顺序的 query 应命中同一 slot
        var reordered = new Dictionary<string, string> { ["state"] = "open", ["page"] = "2", ["per_page"] = "100" };
        var hit = await svc.TryGetApiAsync("/repos/x/y/issues", reordered, CancellationToken.None);
        hit.Should().Be("sorted-result");
    }

    [Fact]
    public async Task WriteApiAsync_DifferentQuery_DoesNotPolluteOtherSlot() {
        var (svc, _) = CreateService();
        var q1 = new Dictionary<string, string> { ["page"] = "1" };
        var q2 = new Dictionary<string, string> { ["page"] = "2" };
        await svc.WriteApiAsync("/repos/x/y/issues", q1, "page1", CancellationToken.None);

        (await svc.TryGetApiAsync("/repos/x/y/issues", q2, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetApiAsync("/repos/x/y/issues", q1, CancellationToken.None)).Should().Be("page1");
    }

    [Fact]
    public async Task WriteApiAsync_SearchEndpoint_Ttl2Minutes() {
        var (svc, kv) = CreateService();
        await svc.WriteApiAsync("/search/issues?q=bug", null, "search-result", CancellationToken.None);
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:api:/search/issues?q=bug"), CancellationToken.None);
        raw.Should().NotBeNull();
        AssertTtl(raw!, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task WriteApiAsync_DetailEndpoint_Ttl10Minutes() {
        var (svc, kv) = CreateService();
        // 末段是数字 → detail 端点
        await svc.WriteApiAsync("/repos/x/y/issues/123", null, "detail", CancellationToken.None);
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:api:/repos/x/y/issues/123"), CancellationToken.None);
        raw.Should().NotBeNull();
        AssertTtl(raw!, TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task WriteApiAsync_DefaultEndpoint_Ttl5Minutes() {
        var (svc, kv) = CreateService();
        await svc.WriteApiAsync("/repos/x/y/issues", null, "list", CancellationToken.None);
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:api:/repos/x/y/issues"), CancellationToken.None);
        raw.Should().NotBeNull();
        AssertTtl(raw!, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task WriteApiAsync_ApiKeyPrefix_IsGhApiColon() {
        var (svc, kv) = CreateService();
        await svc.WriteApiAsync("/repos/x/y", null, "v", CancellationToken.None);
        // 通过 ScanAllGh 验证 key 以 gh:api: 开头
        var entries = await CollectAsync(kv.ScanAsync(Encoding.UTF8.GetBytes("gh:api:"), Encoding.UTF8.GetBytes("gh:api;"), CancellationToken.None));
        entries.Should().ContainSingle(e => Encoding.UTF8.GetString(e.Key) == "gh:api:/repos/x/y");
    }

    // === GraphQL 缓存 ===

    [Fact]
    public async Task TryGetGraphqlAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetGraphqlAsync("query { viewer }", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteGraphqlAsync_ThenTryGetGraphqlAsync_HitReturnsBody() {
        var (svc, _) = CreateService();
        const string body = """{"query":"query { viewer { login } }"}""";
        await svc.WriteGraphqlAsync(body, """{"data":{"viewer":{"login":"x"}}}""", CancellationToken.None);
        var hit = await svc.TryGetGraphqlAsync(body, CancellationToken.None);
        hit.Should().Be("""{"data":{"viewer":{"login":"x"}}}""");
    }

    [Fact]
    public async Task WriteGraphqlAsync_SameBody_HitsSameSlot_DifferentBodyMisses() {
        var (svc, _) = CreateService();
        const string body1 = """{"query":"query { a }"}""";
        const string body2 = """{"query":"query { b }"}""";
        await svc.WriteGraphqlAsync(body1, "resp-a", CancellationToken.None);

        (await svc.TryGetGraphqlAsync(body2, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetGraphqlAsync(body1, CancellationToken.None)).Should().Be("resp-a");
    }

    [Fact]
    public async Task WriteGraphqlAsync_KeyIsGhGraphqlColonSha256Prefix8() {
        var (svc, kv) = CreateService();
        const string body = "query { viewer }";
        await svc.WriteGraphqlAsync(body, "resp", CancellationToken.None);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..8].ToLowerInvariant();
        var expectedKey = $"gh:graphql:{expectedHash}";
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes(expectedKey), CancellationToken.None);
        raw.Should().NotBeNull("GraphQL key 应为 gh:graphql:{sha256前8位小写}");
    }

    // === job 日志缓存 ===

    [Fact]
    public async Task TryGetJobLogAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetJobLogAsync(99, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteJobLogAsync_ThenTryGetJobLogAsync_ReturnsRunIdExpiryLogPrefix() {
        var (svc, _) = CreateService();
        const long jobId = 42L;
        const long runId = 100L;
        await svc.WriteJobLogAsync(jobId, runId, SampleLog, CancellationToken.None);

        var cached = await svc.TryGetJobLogAsync(jobId, CancellationToken.None);
        cached.Should().NotBeNull();
        cached!.Length.Should().Be(16 + SampleLog.Length, "value = [runId(8)][expiry(8)][日志]");
        BitConverter.ToInt64(cached, 0).Should().Be(runId, "前8字节为 runId");
        cached[16..].Should().Equal(SampleLog, "16字节后为日志内容");
    }

    [Fact]
    public async Task WriteJobLogAsync_InnerExpiryIs7DaysFromNow() {
        var (svc, _) = CreateService();
        var before = DateTime.UtcNow;
        await svc.WriteJobLogAsync(1, 200, SampleLog, CancellationToken.None);
        var cached = await svc.TryGetJobLogAsync(1, CancellationToken.None);
        cached.Should().NotBeNull();
        var innerExpiry = new DateTime(BitConverter.ToInt64(cached!, 8), DateTimeKind.Utc);
        (innerExpiry - before).Should().BeCloseTo(TimeSpan.FromDays(7), TimeSpan.FromSeconds(5),
            "内层 expiry 应为 now + 7天");
    }

    [Fact]
    public async Task TryGetRunIdByJobIdAsync_Hit_ReturnsRunId() {
        var (svc, _) = CreateService();
        await svc.WriteJobLogAsync(77, 888, SampleLog, CancellationToken.None);
        var runId = await svc.TryGetRunIdByJobIdAsync(77, CancellationToken.None);
        runId.Should().Be(888L);
    }

    [Fact]
    public async Task TryGetRunIdByJobIdAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetRunIdByJobIdAsync(999, CancellationToken.None)).Should().BeNull();
    }

    // === run 日志缓存 ===

    [Fact]
    public async Task TryGetRunLogAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetRunLogAsync("555", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteRunLogAsync_ThenTryGetRunLogAsync_HitReturnsLogContent() {
        var (svc, kv) = CreateService();
        await svc.WriteRunLogAsync("555", SampleLog, CancellationToken.None);
        var hit = await svc.TryGetRunLogAsync("555", CancellationToken.None);
        hit.Should().NotBeNull();
        hit.Should().Equal(SampleLog);
        // 验证 key = gh:log:{runId}:run
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:log:555:run"), CancellationToken.None);
        raw.Should().NotBeNull("key 应为 gh:log:555:run");
    }

    // === jobs 列表缓存 ===

    [Fact]
    public async Task TryGetJobsAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetJobsAsync(33, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteJobsAsync_ThenTryGetJobsAsync_ReturnsTtlWrappedJson() {
        var (svc, kv) = CreateService();
        await svc.WriteJobsAsync(33, SampleJson, CancellationToken.None);
        // TryGetJobsAsync 用 GetAsync(不解包 TTL),返回 [expiry(8)][json]
        var hit = await svc.TryGetJobsAsync(33, CancellationToken.None);
        hit.Should().NotBeNull();
        hit!.Length.Should().Be(8 + SampleJson.Length, "带 TTL 前缀的原始字节");
        hit[8..].Should().Equal(SampleJson, "8字节后为 JSON 内容");
        // 验证 key = gh:jobs:full:{runId}
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:jobs:full:33"), CancellationToken.None);
        raw.Should().NotBeNull("key 应为 gh:jobs:full:33");
    }

    // === 反查索引 PR → runId ===

    [Fact]
    public async Task TryGetRunIdByPrAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetRunIdByPrAsync(13, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteRunIdByPrAsync_ThenTryGetRunIdByPrAsync_HitReturnsRunId() {
        var (svc, kv) = CreateService();
        await svc.WriteRunIdByPrAsync(13, "run-777", CancellationToken.None);
        var hit = await svc.TryGetRunIdByPrAsync(13, CancellationToken.None);
        hit.Should().Be("run-777");
        // 验证 key = gh:runid:pr:{prNumber}
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:runid:pr:13"), CancellationToken.None);
        raw.Should().NotBeNull("key 应为 gh:runid:pr:13");
        AssertTtl(raw!, TimeSpan.FromMinutes(5));
    }

    // === 反查索引 SHA → runId ===

    [Fact]
    public async Task TryGetRunIdByShaAsync_CacheMiss_ReturnsNull() {
        var (svc, _) = CreateService();
        (await svc.TryGetRunIdByShaAsync("deadbeef", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteRunIdByShaAsync_ThenTryGetRunIdByShaAsync_HitReturnsRunId() {
        var (svc, kv) = CreateService();
        await svc.WriteRunIdByShaAsync("deadbeef", "run-999", CancellationToken.None);
        var hit = await svc.TryGetRunIdByShaAsync("deadbeef", CancellationToken.None);
        hit.Should().Be("run-999");
        // 验证 key = gh:runid:sha:{headSha},TTL 7天
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:runid:sha:deadbeef"), CancellationToken.None);
        raw.Should().NotBeNull("key 应为 gh:runid:sha:deadbeef");
        AssertTtl(raw!, TimeSpan.FromDays(7));
    }

    // === 告警缓存 ===

    [Fact]
    public async Task WriteAlertAsync_ThenScanAlertsAsync_ReturnsEntry() {
        var (svc, _) = CreateService();
        var alertKey = "gh:ci_alert:100:200:1234567890";
        var alertValue = Encoding.UTF8.GetBytes("build failed");
        await svc.WriteAlertAsync(alertKey, alertValue, CancellationToken.None);

        var entries = await CollectAsync(svc.ScanAlertsAsync(CancellationToken.None));
        entries.Should().ContainSingle();
        Encoding.UTF8.GetString(entries[0].Key).Should().Be(alertKey);
        entries[0].Value.Should().Equal(alertValue);
    }

    [Fact]
    public async Task ScanAlertsAsync_FiltersOutNonAlertKeys() {
        var (svc, _) = CreateService();
        // 写入告警 + 非告警 gh 缓存
        await svc.WriteAlertAsync("gh:ci_alert:1:2:3", Encoding.UTF8.GetBytes("alert"), CancellationToken.None);
        await svc.WriteApiAsync("/repos/x/y", null, "api-data", CancellationToken.None);

        var entries = await CollectAsync(svc.ScanAlertsAsync(CancellationToken.None));
        entries.Should().ContainSingle("只应返回 gh:ci_alert: 前缀的 key");
        Encoding.UTF8.GetString(entries[0].Key).Should().StartWith("gh:ci_alert:");
    }

    [Fact]
    public async Task DeleteAlertAsync_RemovesEntry() {
        var (svc, _) = CreateService();
        var keyBytes = Encoding.UTF8.GetBytes("gh:ci_alert:1:2:3");
        await svc.WriteAlertAsync("gh:ci_alert:1:2:3", Encoding.UTF8.GetBytes("alert"), CancellationToken.None);
        (await CollectAsync(svc.ScanAlertsAsync(CancellationToken.None))).Should().ContainSingle();

        await svc.DeleteAlertAsync(keyBytes, CancellationToken.None);
        (await CollectAsync(svc.ScanAlertsAsync(CancellationToken.None))).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAlertAsync_NoTtlPrefix_RawValueStored() {
        var (svc, kv) = CreateService();
        var value = Encoding.UTF8.GetBytes("no-ttl-alert");
        await svc.WriteAlertAsync("gh:ci_alert:5:6:7", value, CancellationToken.None);
        // WriteAlert 用原始 PutAsync(无 TTL 包装),GetAsync 应返回原始 value
        var raw = await kv.GetAsync(Encoding.UTF8.GetBytes("gh:ci_alert:5:6:7"), CancellationToken.None);
        raw.Should().Equal(value, "告警无 TTL,应原始存储不带时间戳前缀");
    }

    // === 缓存清理 ===

    [Fact]
    public async Task ScanAllGhAsync_ReturnsAllGhPrefixedKeys() {
        var (svc, _) = CreateService();
        await svc.WriteApiAsync("/repos/a/b", null, "api", CancellationToken.None);
        await svc.WriteRunIdByPrAsync(1, "r1", CancellationToken.None);
        await svc.WriteAlertAsync("gh:ci_alert:1:2:3", Encoding.UTF8.GetBytes("alert"), CancellationToken.None);

        var entries = await CollectAsync(svc.ScanAllGhAsync(CancellationToken.None));
        entries.Should().HaveCount(3);
        foreach (var (key, _) in entries)
            Encoding.UTF8.GetString(key).Should().StartWith("gh:", "所有 key 应以 gh: 开头");
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry() {
        var (svc, _) = CreateService();
        await svc.WriteApiAsync("/repos/x/y", null, "data", CancellationToken.None);
        var before = await CollectAsync(svc.ScanAllGhAsync(CancellationToken.None));
        before.Should().ContainSingle();

        await svc.DeleteAsync(Encoding.UTF8.GetBytes("gh:api:/repos/x/y"), CancellationToken.None);
        var after = await CollectAsync(svc.ScanAllGhAsync(CancellationToken.None));
        after.Should().BeEmpty();
    }

    // === 异常容错(disposed KvStore 触发 ObjectDisposedException,应被吞) ===

    [Fact]
    public async Task TryGetApiAsync_KvStoreThrows_ReturnsNull_NoException() {
        var kv = new InMemoryKvStore();
        await kv.DisposeAsync();
        var svc = new GhCacheService(kv, null);
        (await svc.TryGetApiAsync("/repos/x/y", null, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetGraphqlAsync("q", CancellationToken.None)).Should().BeNull();
        (await svc.TryGetJobLogAsync(1, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunIdByJobIdAsync(1, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunLogAsync("1", CancellationToken.None)).Should().BeNull();
        (await svc.TryGetJobsAsync(1, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunIdByPrAsync(1, CancellationToken.None)).Should().BeNull();
        (await svc.TryGetRunIdByShaAsync("s", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteApiAsync_KvStoreThrows_NoException() {
        var kv = new InMemoryKvStore();
        await kv.DisposeAsync();
        var svc = new GhCacheService(kv, null);
        // 所有 Write 应吞异常,不抛出
        await svc.WriteApiAsync("/repos/x/y", null, "b", CancellationToken.None);
        await svc.WriteGraphqlAsync("q", "r", CancellationToken.None);
        await svc.WriteJobLogAsync(1, 2, SampleLog, CancellationToken.None);
        await svc.WriteRunLogAsync("1", SampleLog, CancellationToken.None);
        await svc.WriteJobsAsync(1, SampleJson, CancellationToken.None);
        await svc.WriteRunIdByPrAsync(1, "r", CancellationToken.None);
        await svc.WriteRunIdByShaAsync("s", "r", CancellationToken.None);
        await svc.WriteAlertAsync("gh:ci_alert:1:2:3", SampleLog, CancellationToken.None);
        await svc.DeleteAlertAsync(SampleLog, CancellationToken.None);
        await svc.DeleteAsync(SampleLog, CancellationToken.None);
    }

    // === JobLogCacheKey 辅助方法(GhCacheService 间接使用) ===

    [Fact]
    public async Task JobLogCacheKey_TryExtractExpiry_ValidCached_Returns7DayExpiry() {
        var (svc, _) = CreateService();
        var before = DateTime.UtcNow;
        await svc.WriteJobLogAsync(1, 2, SampleLog, CancellationToken.None);
        var cached = await svc.TryGetJobLogAsync(1, CancellationToken.None);
        cached.Should().NotBeNull();
        // TryExtractExpiry 从 value 第9-16字节读取内层 expiry
        var expiry = JobLogCacheKey.TryExtractExpiry(cached);
        expiry.Should().NotBeNull();
        (expiry!.Value - before).Should().BeCloseTo(TimeSpan.FromDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void JobLogCacheKey_TryExtractExpiry_NullOrShort_ReturnsNull() {
        JobLogCacheKey.TryExtractExpiry(null).Should().BeNull();
        JobLogCacheKey.TryExtractExpiry([]).Should().BeNull();
        JobLogCacheKey.TryExtractExpiry(new byte[15]).Should().BeNull("长度<16 返回 null");
    }

    [Fact]
    public void JobLogCacheKey_TryExtractRunId_NullOrShort_ReturnsNull() {
        JobLogCacheKey.TryExtractRunId(null).Should().BeNull();
        JobLogCacheKey.TryExtractRunId([]).Should().BeNull();
        JobLogCacheKey.TryExtractRunId(new byte[15]).Should().BeNull("长度<16 返回 null");
    }

    /// <summary>收集 IAsyncEnumerable 到 List</summary>
    private static async Task<List<(byte[] Key, byte[] Value)>> CollectAsync(
        IAsyncEnumerable<(byte[] Key, byte[] Value)> source) {
        var list = new List<(byte[] Key, byte[] Value)>();
        await foreach (var item in source.ConfigureAwait(false))
            list.Add(item);
        return list;
    }
}
