namespace JoinCode.Abstractions.Interfaces;

public enum TriggerAction { [EnumValue("list")] List, Get, Create, Update, Run }

public sealed class TriggerResult {
    /// <summary>获取状态码。</summary>
    public required int Status { get; init; }
    /// <summary>获取 JSON 响应体。</summary>
    public required string Json { get; init; }
}

public interface IRemoteTriggerService {
    /// <summary>异步执行远程触发器操作。</summary>
    Task<TriggerResult> ExecuteAsync(TriggerAction action, string? triggerId = null, string? body = null, CancellationToken ct = default);
}