
namespace Core.Telemetry;

public sealed class TelemetryGauge : ITelemetryGauge
{
    private readonly Gauge<double> _gauge;

    public string Name { get; }

    internal TelemetryGauge(string name, Gauge<double> gauge)
    {
        Name = name;
        _gauge = gauge;
    }

    public void Record(double value, Dictionary<string, string>? tags = null)
    {
        if (tags != null && tags.Count > 0)
        {
            var tagList = new TagList();
            foreach (var (key, val) in tags)
            {
                tagList.Add(new KeyValuePair<string, object?>(key, val));
            }
            _gauge.Record(value, tagList);
        }
        else
        {
            _gauge.Record(value);
        }
    }
}
