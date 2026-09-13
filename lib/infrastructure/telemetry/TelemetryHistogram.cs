
namespace Core.Telemetry;

public sealed class TelemetryHistogram : ITelemetryHistogram
{
    private readonly Histogram<double> _histogram;

    public string Name { get; }

    internal TelemetryHistogram(string name, Histogram<double> histogram)
    {
        Name = name;
        _histogram = histogram;
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
            _histogram.Record(value, tagList);
        }
        else
        {
            _histogram.Record(value);
        }
    }
}
