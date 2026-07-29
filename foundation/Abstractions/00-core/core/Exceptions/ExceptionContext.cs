
namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 异常上下文信息
/// </summary>
public sealed class ExceptionContext
{
    private readonly Dictionary<string, JsonElement> _data = new();

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
    /// 附加数据
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Data => _data.ToImmutableDictionary();

    /// <summary>
    /// 添加上下文数据
    /// </summary>
    public ExceptionContext WithData(string key, JsonElement value)
    {
        _data[key] = value;
        return this;
    }

    /// <summary>
    /// 添加上下文数据（字符串便捷方法）
    /// </summary>
    public ExceptionContext WithData(string key, string? value)
    {
        _data[key] = JsonElementHelper.FromString(value);
        return this;
    }

    /// <summary>
    /// 批量添加上下文数据
    /// </summary>
    public ExceptionContext WithData(IEnumerable<KeyValuePair<string, JsonElement>> data)
    {
        foreach (var (key, value) in data)
        {
            _data[key] = value;
        }
        return this;
    }
}
