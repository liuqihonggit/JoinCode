
namespace Core.Telemetry;

/// <summary>
/// 遥测仪表盘实现 — 包装 Gauge&lt;double&gt; 记录瞬时值
/// </summary>
public sealed class TelemetryGauge : ITelemetryGauge {
    private readonly Gauge<double> _gauge;

    /// <summary>获取仪表盘名称</summary>
    public string Name { get; }

    /// <summary>
    /// 内部构造 — 由 TelemetryService 工厂调用
    /// </summary>
    /// <param name="name">仪表盘名称</param>
    /// <param name="gauge">底层仪表盘指标</param>
    internal TelemetryGauge(string name, Gauge<double> gauge) {
        Name = name;
        _gauge = gauge;
    }

    /// <summary>
    /// 记录瞬时值到仪表盘
    /// </summary>
    /// <param name="value">本次记录值</param>
    /// <param name="tags">可选标签键值对</param>
    public void Record(double value, Dictionary<string, string>? tags = null) {
        if (tags != null && tags.Count > 0) {
            var tagList = new TagList();
            foreach (var (key, val) in tags) {
                tagList.Add(new KeyValuePair<string, object?>(key, val));
            }
            _gauge.Record(value, tagList);
        } else {
            _gauge.Record(value);
        }
    }
}