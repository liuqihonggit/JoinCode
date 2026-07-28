namespace Core.Query.BudgetAnalysis;

public interface IDiminishingReturnsDetector
{
    DiminishingReturnsResult CheckDiminishingReturns(IReadOnlyList<TokenConsumption> recentConsumptions);
    void Reset();
}

public sealed class DiminishingReturnsResult
{
    public bool IsDiminishing { get; init; }
    public double EffectivenessRatio { get; init; }
    public string? Recommendation { get; init; }
    public int ConsecutiveLowValueIterations { get; init; }
}

[Register]
public sealed partial class DiminishingReturnsDetector : IDiminishingReturnsDetector
{
    private const double LowValueThreshold = 0.1;
    private const int ConsecutiveThreshold = 3;
    private const int MinimumSampleSize = 2;

    private int _consecutiveLowValueCount;
    private readonly object _resetLock = new();
    private readonly ITelemetryService? _telemetryService;

    public DiminishingReturnsDetector(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
    }

    public DiminishingReturnsResult CheckDiminishingReturns(IReadOnlyList<TokenConsumption> recentConsumptions)
    {
        ArgumentNullException.ThrowIfNull(recentConsumptions);

        if (recentConsumptions.Count < MinimumSampleSize)
        {
            return new DiminishingReturnsResult
            {
                IsDiminishing = false,
                EffectivenessRatio = 1.0,
                ConsecutiveLowValueIterations = 0
            };
        }

        var ratios = new List<double>(recentConsumptions.Count - 1);
        for (var i = 1; i < recentConsumptions.Count; i++)
        {
            var prevConsumption = recentConsumptions[i - 1];
            var currConsumption = recentConsumptions[i];

            if (prevConsumption.Amount <= 0)
            {
                continue;
            }

            var tokenGrowthRate = (double)currConsumption.Amount / prevConsumption.Amount;
            ratios.Add(tokenGrowthRate);
        }

        if (ratios.Count == 0)
        {
            return new DiminishingReturnsResult
            {
                IsDiminishing = false,
                EffectivenessRatio = 1.0,
                ConsecutiveLowValueIterations = 0
            };
        }

        var averageRatio = ratios.Average();

        lock (_resetLock)
        {
            if (averageRatio < LowValueThreshold)
            {
                _consecutiveLowValueCount++;
            }
            else
            {
                _consecutiveLowValueCount = 0;
            }

            var isDiminishing = _consecutiveLowValueCount >= ConsecutiveThreshold;
            string? recommendation = isDiminishing switch
            {
                true when _consecutiveLowValueCount >= ConsecutiveThreshold + 2 => "Stop iteration - sustained diminishing returns",
                true => "Consider compacting context or switching strategy",
                _ => null
            };

            _telemetryService?.RecordCount("query.diminishing.check.count", new() { ["diminishing"] = isDiminishing.ToString() }, "count", "Diminishing returns check count");
            _telemetryService?.RecordHistogram("query.diminishing.effectiveness", averageRatio, unit: "ratio", description: "Effectiveness ratio");

            return new DiminishingReturnsResult
            {
                IsDiminishing = isDiminishing,
                EffectivenessRatio = averageRatio,
                Recommendation = recommendation,
                ConsecutiveLowValueIterations = _consecutiveLowValueCount
            };
        }
    }

    public void Reset()
    {
        lock (_resetLock)
        {
            _consecutiveLowValueCount = 0;
        }
    }
}
