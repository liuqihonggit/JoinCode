namespace Core.Utils;

/// <summary>
/// 消息重试引擎 — 封装重试队列、背压延迟信号队列、重试循环、退避计算、最大重试控制(关注点分离)。
/// <para>原 ActorBase._retryQueue/_bpDelayQueue/ProcessRetryQueueAsync/ConsumeBackpressureDelay 逻辑抽离至此。</para>
/// <para>P0 修复:重试循环增加全局兜底异常捕获,重试引擎崩溃时记 LogCritical 告警(原仅吞 OperationCanceledException,其他异常静默崩溃无告警)。</para>
/// <para>职责边界:持有重试队列与背压延迟队列,接收远程背压信号,重试循环将命令回写输入通道;不持有输入通道,不触发水位事件(通过回调通知 ActorBase)。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
internal sealed class MessageRetryEngine<TCommand> {
    private readonly Channel<RetryEntry<TCommand>> _retryQueue;
    private readonly Channel<TimeSpan> _bpDelayQueue;
    private readonly ChannelWriter<TCommand> _inputWriter;
    private readonly ILogger? _logger;
    private readonly string _actorId;
    private readonly int _maxRetries;
    private readonly Action<TCommand, int> _onSendFailed;
    private readonly Action _onEnqueuedToInput;
    private readonly CancellationToken _shutdownCt;
    private Task? _retryTask;

    /// <summary>初始化重试引擎</summary>
    /// <param name="retryQueueCapacity">重试队列容量(满时回写失败触发 SendFailed)</param>
    /// <param name="maxRetries">最大重试次数(耗尽触发 SendFailed)</param>
    /// <param name="inputWriter">输入通道写入端(重试成功时回写命令)</param>
    /// <param name="actorId">Actor 标识(日志用)</param>
    /// <param name="logger">日志记录器(null=静默)</param>
    /// <param name="shutdownCt">关闭取消令牌(Actor 释放时取消重试循环)</param>
    /// <param name="onSendFailed">重试失败回调(命令,重试次数)→由 ActorBase 触发 SendFailed 事件</param>
    /// <param name="onEnqueuedToInput">命令成功回写输入通道后回调→由 ActorBase 执行水位检查</param>
    public MessageRetryEngine(
        int retryQueueCapacity,
        int maxRetries,
        ChannelWriter<TCommand> inputWriter,
        string actorId,
        ILogger? logger,
        CancellationToken shutdownCt,
        Action<TCommand, int> onSendFailed,
        Action onEnqueuedToInput) {
        _maxRetries = maxRetries;
        _actorId = actorId;
        _logger = logger;
        _inputWriter = inputWriter;
        _shutdownCt = shutdownCt;
        _onSendFailed = onSendFailed;
        _onEnqueuedToInput = onEnqueuedToInput;
        _retryQueue = Channel.CreateBounded<RetryEntry<TCommand>>(new BoundedChannelOptions(retryQueueCapacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _bpDelayQueue = Channel.CreateBounded<TimeSpan>(new BoundedChannelOptions(16) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>启动重试后台循环</summary>
    public void Start() {
        _retryTask = Task.Run(RunLoopAsync, _shutdownCt);
    }

    /// <summary>接收远程背压延迟信号 — 写入延迟信号队列,供重试时消费</summary>
    /// <param name="suggestedDelay">建议延迟时间</param>
    public void ReceiveBackpressureSignal(TimeSpan suggestedDelay) => _bpDelayQueue.Writer.TryWrite(suggestedDelay);

    /// <summary>尝试首次入重试队列(Tell 入队失败时调用)</summary>
    /// <param name="entry">重试条目(Attempt=1)</param>
    /// <returns>true=入队成功;false=重试队列满</returns>
    public bool TryEnqueue(RetryEntry<TCommand> entry) => _retryQueue.Writer.TryWrite(entry);

    /// <summary>
    /// 尝试将重试条目回写到重试队列 — 超过最大重试次数或重试队列满时触发 onSendFailed 回调。
    /// <para>返回 true 表示成功回写,false 表示失败(已触发 onSendFailed)。</para>
    /// </summary>
    /// <param name="entry">待回写的重试条目</param>
    /// <returns>true=成功回写;false=失败(已触发 SendFailed 回调)</returns>
    public bool TryRequeue(RetryEntry<TCommand> entry) {
        if (entry.Attempt >= _maxRetries) {
            _onSendFailed(entry.Command, _maxRetries);
            return false;
        }
        if (!_retryQueue.Writer.TryWrite(new RetryEntry<TCommand>(entry.Command, entry.Attempt + 1))) {
            _logger?.LogWarning("[Actor:{ActorId}] 重试队列满,重试回写失败,触发SendFailed attempt={Attempt}", _actorId, entry.Attempt);
            _onSendFailed(entry.Command, entry.Attempt);
            return false;
        }
        return true;
    }

    /// <summary>完成队列,停止读取(Actor 释放时调用)</summary>
    public void Complete() {
        _retryQueue.Writer.TryComplete();
        _bpDelayQueue.Writer.TryComplete();
    }

    /// <summary>等待重试任务结束(Actor 释放时 await)</summary>
    public Task WaitForCompletionAsync() => _retryTask ?? Task.CompletedTask;

    /// <summary>重试队列当前消息数</summary>
    public int RetryQueueCount => _retryQueue.Reader.Count;

    /// <summary>重试队列原始通道 — 供 ActorBase 转发 RetryQueueInternal 门面(测试直接写入构造边界条件)</summary>
    internal Channel<RetryEntry<TCommand>> RetryQueue => _retryQueue;

    /// <summary>
    /// 处理单条重试条目(无退避延迟) — 回写输入通道或 TryRequeue,供测试确定性调用。
    /// </summary>
    /// <returns>true=回写输入通道成功;false=回写失败(已 TryRequeue)</returns>
    internal bool ProcessOneEntryImmediate(RetryEntry<TCommand> entry) {
        if (_inputWriter.TryWrite(entry.Command)) {
            _onEnqueuedToInput();
            return true;
        }
        TryRequeue(entry);
        return false;
    }

    /// <summary>
    /// 重试循环 — 单例后台 Task 处理所有重试消息(P1-2: 替代每消息一 Task,高负载时不产生大量 Delay 任务)。
    /// <para>P0 修复:全局兜底 catch(Exception),重试引擎崩溃时记 LogCritical 告警(原仅吞 OperationCanceledException 静默崩溃)。</para>
    /// </summary>
    private async Task RunLoopAsync() {
        try {
            await foreach (var entry in _retryQueue.Reader.ReadAllAsync(_shutdownCt).ConfigureAwait(false)) {
                var bpDelay = ConsumeLatestBackpressureDelay();
                var backoffMs = 100 * (1 << Math.Min(entry.Attempt, 10));
                backoffMs = Math.Min(backoffMs, 5000);
                var totalDelayMs = Math.Min(bpDelay.TotalMilliseconds + backoffMs, 10_000);
                if (totalDelayMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(totalDelayMs), _shutdownCt).ConfigureAwait(false);

                ProcessOneEntryImmediate(entry);
            }
        } catch (OperationCanceledException) {
            // 正常关闭
        } catch (Exception ex) {
            _logger?.LogCritical(ex, "[Actor:{ActorId}] MessageRetryEngine 循环异常,重试引擎停止工作", _actorId);
        }
    }

    /// <summary>消费所有待处理背压延迟信号,取最新延迟(R2: 远程背压信号是瞬时建议,取最新非累加)</summary>
    private TimeSpan ConsumeLatestBackpressureDelay() {
        var last = TimeSpan.Zero;
        while (_bpDelayQueue.Reader.TryRead(out var delay)) last = delay;
        return last;
    }
}
