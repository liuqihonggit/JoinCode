namespace Core.Utils;

/// <summary>
/// 背压协议消息 — 带流水号和重试计数的业务消息,收发双方环形通信的载体
/// </summary>
/// <typeparam name="T">业务数据类型</typeparam>
/// <param name="Payload">业务数据</param>
/// <param name="SequenceId">消息流水号(每次重试换新号,单调递增)</param>
/// <param name="RetryCount">重试次数(0=首次,15=最后一次)</param>
/// <param name="SourceId">发送方标识</param>
/// <param name="TargetId">接收方标识</param>
public sealed record BackpressureMessage<T>(
    T Payload,
    long SequenceId,
    int RetryCount,
    string SourceId,
    string TargetId) where T : notnull;

/// <summary>
/// 背压信号 — 接收方反向通知发送方(射后不理),包含水位等级和建议延迟
/// </summary>
/// <param name="SequenceId">对应被背压的消息流水号</param>
/// <param name="SourceId">背压来源(接收方标识)</param>
/// <param name="TargetId">被背压方(发送方标识)</param>
/// <param name="Level">水位等级:High=降速,Critical=即将满,Normal=恢复正常</param>
/// <param name="SuggestedDelay">建议延迟时间</param>
/// <param name="RetryCount">当前重试次数</param>
public sealed record BackpressureSignal(
    long SequenceId,
    string SourceId,
    string TargetId,
    WatermarkLevel Level,
    TimeSpan SuggestedDelay,
    int RetryCount);
