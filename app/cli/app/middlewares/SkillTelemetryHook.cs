namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<SkillContext>), ServiceLifetime.Singleton)]
internal sealed partial class SkillTelemetryHook : TelemetryPostHook<SkillContext> {
    /// <summary>初始化技能执行管道遥测后置钩子实例。</summary>
    public SkillTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "skill.execute.count", "Skill pipeline count") { }
}