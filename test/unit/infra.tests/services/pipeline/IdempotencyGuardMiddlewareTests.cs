namespace Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 测试用幂等上下文 — 实现 IIdempotentContext&lt;string&gt;。
/// </summary>
internal sealed class TestIdempotentContext : IIdempotentContext<string> {
    /// <summary>幂等键</summary>
    public IdempotencyKey IdempotencyKey { get; set; }
    /// <summary>操作结果</summary>
    public string? Result { get; set; }
}

/// <summary>
/// IdempotencyGuardMiddleware 单元测试 — 验证键控去重 + 短路 + 缓存。
/// </summary>
public class IdempotencyGuardMiddlewareTests {
    /// <summary>
    /// 空键透传 — key 为 None 时,中间件透传,执行 next。
    /// </summary>
    [Fact]
    public async Task EmptyKey_PassesThrough() {
        var store = new IdempotencyStore();
        var middleware = new IdempotencyGuardMiddleware<TestIdempotentContext, string>(store);
        var context = new TestIdempotentContext { IdempotencyKey = IdempotencyKey.None };
        var nextExecuted = false;

        await middleware.InvokeAsync(context, (_, _) => { nextExecuted = true; return Task.CompletedTask; }, default);

        nextExecuted.Should().BeTrue();
    }

    /// <summary>
    /// 命中缓存短路 — key 已注册时,从缓存恢复 Result,不执行 next。
    /// </summary>
    [Fact]
    public async Task CacheHit_ShortCircuits() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");
        store.TryRegister(key, "cached-result");

        var middleware = new IdempotencyGuardMiddleware<TestIdempotentContext, string>(store);
        var context = new TestIdempotentContext { IdempotencyKey = key };
        var nextExecuted = false;

        await middleware.InvokeAsync(context, (_, _) => { nextExecuted = true; return Task.CompletedTask; }, default);

        nextExecuted.Should().BeFalse();
        context.Result.Should().Be("cached-result");
    }

    /// <summary>
    /// 未命中执行并缓存 — key 未注册时,执行 next,缓存 Result。
    /// </summary>
    [Fact]
    public async Task CacheMiss_ExecutesAndCaches() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");
        var middleware = new IdempotencyGuardMiddleware<TestIdempotentContext, string>(store);
        var context = new TestIdempotentContext { IdempotencyKey = key };

        await middleware.InvokeAsync(context, (ctx, _) => { ctx.Result = "fresh-result"; return Task.CompletedTask; }, default);

        context.Result.Should().Be("fresh-result");
        store.IsRegistered(key).Should().BeTrue();
        store.TryGetResult<string>(key, out var cached).Should().BeTrue();
        cached.Should().Be("fresh-result");
    }

    /// <summary>
    /// 重复请求去重 — 同一 key 第二次请求时,从缓存恢复,不执行 next。
    /// </summary>
    [Fact]
    public async Task DuplicateRequest_SecondFromCache() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");
        var middleware = new IdempotencyGuardMiddleware<TestIdempotentContext, string>(store);

        var context1 = new TestIdempotentContext { IdempotencyKey = key };
        await middleware.InvokeAsync(context1, (ctx, _) => { ctx.Result = "result-1"; return Task.CompletedTask; }, default);
        context1.Result.Should().Be("result-1");

        var context2 = new TestIdempotentContext { IdempotencyKey = key };
        var nextExecuted = false;
        await middleware.InvokeAsync(context2, (_, _) => { nextExecuted = true; return Task.CompletedTask; }, default);

        nextExecuted.Should().BeFalse();
        context2.Result.Should().Be("result-1");
    }

    /// <summary>
    /// null 结果能正确缓存和恢复。
    /// </summary>
    [Fact]
    public async Task NullResult_CachedAndRestored() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");
        var middleware = new IdempotencyGuardMiddleware<TestIdempotentContext, string>(store);

        var context1 = new TestIdempotentContext { IdempotencyKey = key };
        await middleware.InvokeAsync(context1, (ctx, _) => { ctx.Result = null; return Task.CompletedTask; }, default);

        var context2 = new TestIdempotentContext { IdempotencyKey = key };
        var nextExecuted = false;
        await middleware.InvokeAsync(context2, (_, _) => { nextExecuted = true; return Task.CompletedTask; }, default);

        nextExecuted.Should().BeFalse();
        context2.Result.Should().BeNull();
    }
}
