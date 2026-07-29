
namespace Core.Telemetry;

public sealed class TelemetryCounter : ITelemetryCounter
{
    private readonly Counter<double> _counter;

    public string Name { get; }

    internal TelemetryCounter(string name, Counter<double> counter)
    {
        Name = name;
        _counter = counter;
    }

    public void Add(double value, Dictionary<string, string>? tags = null)
    {
        if (tags != null && tags.Count > 0)
        {
            var tagList = new TagList();
            foreach (var (key, val) in tags)
            {
                tagList.Add(new KeyValuePair<string, object?>(key, val));
            }
            _counter.Add(value, tagList);
        }
        else
        {
            _counter.Add(value);
        }
    }
}
