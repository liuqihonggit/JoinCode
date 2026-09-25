
namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 异常上下文信息
/// </summary>
public sealed class ExceptionContext {
    private ImmutableDictionary<string, JsonElement> _data = ImmutableDictionary<string, JsonElement>.Empty;

    /// <summary>
    /// 请求ID，用于追踪请求链路
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// 操作名称
    /// </summary>
    public string? OperationName { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 附加数据 — O(1) 直接返回不可变字典引用，无物化
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Data => _data;

    /// <summary>
    /// 添加上下文数据
    /// </summary>
    public ExceptionContext WithData(string key, JsonElement value) {
        _data = _data.SetItem(key, value);
        return this;
    }

    /// <summary>
    /// 添加上下文数据（字符串便捷方法）
    /// </summary>
    public ExceptionContext WithData(string key, string? value) {
        _data = _data.SetItem(key, JsonElementHelper.FromString(value));
        return this;
    }

    /// <summary>
    /// 批量添加上下文数据
    /// </summary>
    public ExceptionContext WithData(IEnumerable<KeyValuePair<string, JsonElement>> data) {
        var builder = _data.ToBuilder();
        foreach (var (key, value) in data) {
            builder[key] = value;
        }
        _data = builder.ToImmutable();
        return this;
    }
}