
namespace Core.Telemetry;

/// <summary>
/// 遥测计数器实现 — 包装 Counter&lt;double&gt; 并可选写入分析文件下沉
/// </summary>
public sealed class TelemetryCounter : ITelemetryCounter {
    private readonly Counter<double> _counter;
    private readonly IAnalyticsFileSink? _analyticsSink;

    /// <summary>获取计数器名称</summary>
    public string Name { get; }

    /// <summary>
    /// 内部构造 — 由 TelemetryService 工厂调用
    /// </summary>
    /// <param name="name">计数器名称</param>
    /// <param name="counter">底层计数器指标</param>
    /// <param name="analyticsSink">可选分析文件下沉</param>
    internal TelemetryCounter(string name, Counter<double> counter, IAnalyticsFileSink? analyticsSink = null) {
        Name = name;
        _counter = counter;
        _analyticsSink = analyticsSink;
    }

    /// <summary>
    /// 累加计数 — 同时更新底层指标与分析文件下沉
    /// </summary>
    /// <param name="value">本次累加值</param>
    /// <param name="tags">可选标签键值对</param>
    public void Add(double value, Dictionary<string, string>? tags = null) {
        if (tags != null && tags.Count > 0) {
            var tagList = new TagList();
            foreach (var (key, val) in tags) {
                tagList.Add(new KeyValuePair<string, object?>(key, val));
            }
            _counter.Add(value, tagList);
        } else {
            _counter.Add(value);
        }

        _analyticsSink?.LogEvent(Name, tags, value);
    }
}