namespace Infrastructure.Pipeline;

/// <summary>
/// 管道构建器基类 — 统一 Task/Stream 两种构建器的 Hook/Error/ShortCircuit 配置逻辑
/// CRTP 模式保持 Fluent API 的具体类型返回
/// </summary>
public abstract class PipelineBuilderBase<TContext, TSelf> where TSelf : PipelineBuilderBase<TContext, TSelf>
{
    private Action<TContext, Exception>? _onError;
    private PipelinePreHookDelegate<TContext>? _onPreExecute;
    private PipelinePostHookDelegate<TContext>? _onPostExecute;
    private Func<TContext, bool>? _shortCircuitPredicate;

    public TSelf OnError(Action<TContext, Exception> onError)
    {
        _onError = onError;
        return (TSelf)this;
    }

    public TSelf WithPreHook(PipelinePreHookDelegate<TContext> onPreExecute)
    {
        _onPreExecute = onPreExecute;
        return (TSelf)this;
    }

    public TSelf WithPostHook(PipelinePostHookDelegate<TContext> onPostExecute)
    {
        _onPostExecute = onPostExecute;
        return (TSelf)this;
    }

    /// <summary>
    /// 设置短路谓词 — 每个中间件执行前检查，返回 true 则跳过后续中间件
    /// </summary>
    public TSelf WithShortCircuit(Func<TContext, bool> predicate)
    {
        _shortCircuitPredicate = predicate;
        return (TSelf)this;
    }

    internal Action<TContext, Exception>? OnErrorHandler => _onError;
    internal PipelinePreHookDelegate<TContext>? PreHook => _onPreExecute;
    internal PipelinePostHookDelegate<TContext>? PostHook => _onPostExecute;
    internal Func<TContext, bool>? ShortCircuitPredicate => _shortCircuitPredicate;
}

/// <summary>
/// Task 管道构建器 — Fluent API，支持手动注册中间件、条件注册和 Hook
/// </summary>
public sealed class PipelineBuilder<TContext> : PipelineBuilderBase<TContext, PipelineBuilder<TContext>>
{
    private readonly List<IMiddleware<TContext>> _middlewares = [];

    public PipelineBuilder<TContext> Use(IMiddleware<TContext> middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public PipelineBuilder<TContext> UseRange(IEnumerable<IMiddleware<TContext>> middlewares)
    {
        _middlewares.AddRange(middlewares);
        return this;
    }

    /// <summary>
    /// 条件修饰 — 修饰最后一个 Use() 注册的中间件，predicate 返回 true 时执行，否则跳过
    /// LINQ 风格链式调用：.Use(mw).Where(ctx => ctx.Enabled)
    /// </summary>
    public PipelineBuilder<TContext> Where(Func<TContext, bool> predicate)
    {
        if (_middlewares.Count == 0)
            throw new InvalidOperationException("Where() 必须在 Use() 之后调用");

        var last = _middlewares[^1];
        _middlewares[^1] = new ConditionalMiddleware<TContext>(predicate, last);
        return this;
    }

    /// <summary>
    /// 异步条件修饰 — 异步 predicate 版本
    /// </summary>
    public PipelineBuilder<TContext> Where(Func<TContext, CancellationToken, ValueTask<bool>> predicate)
    {
        if (_middlewares.Count == 0)
            throw new InvalidOperationException("Where() 必须在 Use() 之后调用");

        var last = _middlewares[^1];
        _middlewares[^1] = new AsyncConditionalMiddleware<TContext>(predicate, last);
        return this;
    }

    public MiddlewarePipeline<TContext> Build()
        => new(_middlewares, OnErrorHandler, PreHook, PostHook, ShortCircuitPredicate);

    public MiddlewarePipeline<TContext> BuildFromServices(IServiceProvider serviceProvider)
    {
        var resolved = serviceProvider.GetServices<IMiddleware<TContext>>();
        _middlewares.AddRange(resolved);
        return Build();
    }
}

/// <summary>
/// Stream 管道构建器 — Fluent API，支持手动注册中间件、条件注册和 Hook
/// </summary>
public sealed class StreamPipelineBuilder<TContext, TEvent> : PipelineBuilderBase<TContext, StreamPipelineBuilder<TContext, TEvent>>
{
    private readonly List<IStreamMiddleware<TContext, TEvent>> _middlewares = [];

    public StreamPipelineBuilder<TContext, TEvent> Use(IStreamMiddleware<TContext, TEvent> middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public StreamPipelineBuilder<TContext, TEvent> UseRange(IEnumerable<IStreamMiddleware<TContext, TEvent>> middlewares)
    {
        _middlewares.AddRange(middlewares);
        return this;
    }

    /// <summary>
    /// 条件修饰 — 修饰最后一个 Use() 注册的中间件，predicate 返回 true 时执行，否则跳过
    /// LINQ 风格链式调用：.Use(mw).Where(ctx => ctx.Enabled)
    /// </summary>
    public StreamPipelineBuilder<TContext, TEvent> Where(Func<TContext, bool> predicate)
    {
        if (_middlewares.Count == 0)
            throw new InvalidOperationException("Where() 必须在 Use() 之后调用");

        var last = _middlewares[^1];
        _middlewares[^1] = new ConditionalStreamMiddleware<TContext, TEvent>(predicate, last);
        return this;
    }

    public StreamMiddlewarePipeline<TContext, TEvent> Build()
        => new(_middlewares, OnErrorHandler, PreHook, PostHook, ShortCircuitPredicate);

    public StreamMiddlewarePipeline<TContext, TEvent> BuildFromServices(IServiceProvider serviceProvider)
    {
        var resolved = serviceProvider.GetServices<IStreamMiddleware<TContext, TEvent>>();
        _middlewares.AddRange(resolved);
        return Build();
    }
}
