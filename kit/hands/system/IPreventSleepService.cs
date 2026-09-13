
namespace Services.SystemPower;

public interface IPreventSleepService : IDisposable
{
    Task<bool> PreventSleepAsync(SleepPreventionType type = SleepPreventionType.Continuous, CancellationToken cancellationToken = default);
    Task<bool> AllowSleepAsync(CancellationToken cancellationToken = default);
    bool IsSleepPrevented { get; }
}
