
namespace Core.Telemetry;

/// <summary>
/// 遥测直方图实现 — 包装 Histogram&lt;double&gt; 记录分布统计
/// </summary>
public sealed class TelemetryHistogram : ITelemetryHistogram {
    private readonly Histogram<double> _histogram;

    /// <summary>获取直方图名称</summary>
    public string Name { get; }

    /// <summary>
    /// 内部构造 — 由 TelemetryService 工厂调用
    /// </summary>
    /// <param name="name">直方图名称</param>
    /// <param name="histogram">底层直方图指标</param>
    internal TelemetryHistogram(string name, Histogram<double> histogram) {
        Name = name;
        _histogram = histogram;
    }

    /// <summary>
    /// 记录观测值到直方图
    /// </summary>
    /// <param name="value">本次观测值</param>
    /// <param name="tags">可选标签键值对</param>
    public void Record(double value, Dictionary<string, string>? tags = null) {
        if (tags != null && tags.Count > 0) {
            var tagList = new TagList();
            foreach (var (key, val) in tags) {
                tagList.Add(new KeyValuePair<string, object?>(key, val));
            }
            _histogram.Record(value, tagList);
        } else {
            _histogram.Record(value);
        }
    }
}