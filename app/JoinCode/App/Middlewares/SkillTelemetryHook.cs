namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<SkillContext>), ServiceLifetime.Singleton)]
internal sealed partial class SkillTelemetryHook : TelemetryPostHook<SkillContext>
{
    public SkillTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "skill.execute.count", "Skill pipeline count") { }
}
