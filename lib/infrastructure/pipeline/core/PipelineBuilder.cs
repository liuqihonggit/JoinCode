namespace Infrastructure.Pipeline;

/// <summary>
/// 管道构建器基类 — 统一 Task/Stream 两种构建器的 Hook/Error/ShortCircuit 配置逻辑
/// CRTP 模式保持 Fluent API 的具体类型返回
/// </summary>
public abstract class PipelineBuilderBase<TContext, TSelf> where TSelf : PipelineBuilderBase<TContext, TSelf> {
    private Action<TContext, Exception>? _onError;
    private PipelinePreHookDelegate<TContext>? _onPreExecute;
    private PipelinePostHookDelegate<TContext>? _onPostExecute;
    private Func<TContext, bool>? _shortCircuitPredicate;

    /// <summary>
    /// 注册错误处理回调 — 管道执行异常时调用
    /// </summary>
    /// <param name="onError">错误处理委托，接收上下文和异常</param>
    /// <returns>当前构建器实例（Fluent 链式）</returns>
    public TSelf OnError(Action<TContext, Exception> onError) {
        _onError = onError;
        return (TSelf)this;
    }

    /// <summary>
    /// 注册前置 Hook — 中间件执行前调用
    /// </summary>
    /// <param name="onPreExecute">前置 Hook 委托</param>
    /// <returns>当前构建器实例（Fluent 链式）</returns>
    public TSelf WithPreHook(PipelinePreHookDelegate<TContext> onPreExecute) {
        _onPreExecute = onPreExecute;
        return (TSelf)this;
    }

    /// <summary>
    /// 注册后置 Hook — 中间件执行后调用
    /// </summary>
    /// <param name="onPostExecute">后置 Hook 委托</param>
    /// <returns>当前构建器实例（Fluent 链式）</returns>
    public TSelf WithPostHook(PipelinePostHookDelegate<TContext> onPostExecute) {
        _onPostExecute = onPostExecute;
        return (TSelf)this;
    }

    /// <summary>
    /// 设置短路谓词 — 每个中间件执行前检查，返回 true 则跳过后续中间件
    /// </summary>
    public TSelf WithShortCircuit(Func<TContext, bool> predicate) {
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
public sealed class PipelineBuilder<TContext> : PipelineBuilderBase<TContext, PipelineBuilder<TContext>> {
    private readonly List<IMiddleware<TContext>> _middlewares = [];

    /// <summary>
    /// 注册中间件 — 添加到管道末尾
    /// </summary>
    /// <param name="middleware">中间件实例</param>
    /// <returns>当前构建器实例（Fluent 链式）</returns>
    public PipelineBuilder<TContext> Use(IMiddleware<TContext> middleware) {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// 批量注册中间件 — 一次性添加多个中间件到管道末尾
    /// </summary>
    /// <param name="middlewares">中间件集合</param>
    /// <returns>当前构建器实例（Fluent 链式）</returns>
    public PipelineBuilder<TContext> UseRange(IEnumerable<IMiddleware<TContext>> middlewares) {
        _middlewares.AddRange(middlewares);
        return this;
    }

    /// <summary>
    /// 条件修饰 — 修饰最后一个 Use() 注册的中间件，predicate 返回 true 时执行，否则跳过
    /// LINQ 风格链式调用：.Use(mw).Where(ctx => ctx.Enabled)
    /// </summary>
    public PipelineBuilder<TContext> Where(Func<TContext, bool> predicate) {
        if (_middlewares.Count == 0)
            throw new InvalidOperationException("[PPL004] Where() 必须在 Use() 之后调用");

        var last = _middlewares[^1];
        _middlewares[^1] = new ConditionalMiddleware<TContext>(predicate, last);
        return this;
    }

    /// <summary>
    /// 异步条件修饰 — 异步 predicate 版本
    /// </summary>
    public PipelineBuilder<TContext> Where(Func<TContext, CancellationToken, ValueTask<bool>> predicate) {
        if (_middlewares.Count == 0)
            throw new InvalidOperationException("[PPL005] Where() 必须在 Use() 之后调用");

        var last = _middlewares[^1];
        _middlewares[^1] = new AsyncConditionalMiddleware<TContext>(predicate, last);
        return this;
    }

    /// <summary>
    /// 构建管道 — 生成不可变的中间件管道实例
    /// </summary>
    /// <returns>中间件管道</returns>
    public MiddlewarePipeline<TContext> Build()
        => new(_middlewares, OnErrorHandler, PreHook, PostHook, ShortCircuitPredicate);

    /// <summary>
    /// 从 DI 容器解析中间件并构建管道 — 将所有注册的 IMiddleware&lt;TContext&gt; 服务追加到管道
    /// </summary>
    /// <param name="serviceProvider">DI 服务提供者</param>
    /// <returns>中间件管道</returns>
    public MiddlewarePipeline<TContext> BuildFromServices(IServiceProvider serviceProvider) {
        var resolved = serviceProvider.GetServices<IMiddleware<TContext>>();
        _middlewares.AddRange(resolved);
        return Build();
    }
}

/// <summary>
/// Stream 管道构建器 — Fluent API，支持手动注册中间件、条件注册和 Hook
/// </summary>
public sealed class StreamPipelineBuilder<TContext, TEvent> : PipelineBuilderBase<TContext, StreamPipelineBuilder<TContext, TEvent>> {
    private readonly List<IStreamMiddleware<TContext, TEvent>> _middlewares = [];

    /// <summary>
    /// 注册流式中间件 — 添加到管道末尾
    /// </summary>
    /// <param name="middleware">流式中间件实例</param>
    /// <returns>当前构建器实例（Fluent 链式）</returns>
    public StreamPipelineBuilder<TContext, TEvent> Use(IStreamMiddleware<TContext, TEvent> middleware) {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// 批量注册流式中间件 — 一次性添加多个中间件到管道末尾
    /// </summary>
    /// <param name="middlewares">流式中间件集合</param>
    /// <returns>当前构建器实例（Fluent 链式）</returns>
    public StreamPipelineBuilder<TContext, TEvent> UseRange(IEnumerable<IStreamMiddleware<TContext, TEvent>> middlewares) {
        _middlewares.AddRange(middlewares);
        return this;
    }

    /// <summary>
    /// 条件修饰 — 修饰最后一个 Use() 注册的中间件，predicate 返回 true 时执行，否则跳过
    /// LINQ 风格链式调用：.Use(mw).Where(ctx => ctx.Enabled)
    /// </summary>
    public StreamPipelineBuilder<TContext, TEvent> Where(Func<TContext, bool> predicate) {
        if (_middlewares.Count == 0)
            throw new InvalidOperationException("[PPL006] Where() 必须在 Use() 之后调用");

        var last = _middlewares[^1];
        _middlewares[^1] = new ConditionalStreamMiddleware<TContext, TEvent>(predicate, last);
        return this;
    }

    /// <summary>
    /// 构建流式管道 — 生成不可变的流式中间件管道实例
    /// </summary>
    /// <returns>流式中间件管道</returns>
    public StreamMiddlewarePipeline<TContext, TEvent> Build()
        => new(_middlewares, OnErrorHandler, PreHook, PostHook, ShortCircuitPredicate);

    /// <summary>
    /// 从 DI 容器解析流式中间件并构建管道 — 将所有注册的 IStreamMiddleware&lt;TContext, TEvent&gt; 服务追加到管道
    /// </summary>
    /// <param name="serviceProvider">DI 服务提供者</param>
    /// <returns>流式中间件管道</returns>
    public StreamMiddlewarePipeline<TContext, TEvent> BuildFromServices(IServiceProvider serviceProvider) {
        var resolved = serviceProvider.GetServices<IStreamMiddleware<TContext, TEvent>>();
        _middlewares.AddRange(resolved);
        return Build();
    }
}