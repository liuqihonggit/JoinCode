using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Skill 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<SkillContext>))]
internal sealed class SkillTelemetryHook : IPipelinePostHook<SkillContext>
{
    private readonly ITelemetryService? _telemetryService;

    public SkillTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(SkillContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("skill.execute.count",
                new() { ["source"] = "pipeline" },
                "count", "Skill pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
