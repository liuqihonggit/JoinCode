using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<SkillContext>))]
internal sealed partial class SkillTelemetryHook : TelemetryPostHook<SkillContext>
{
    public SkillTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "skill.execute.count", "Skill pipeline count") { }
}
