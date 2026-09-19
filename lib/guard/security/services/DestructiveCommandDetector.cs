namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 破坏性命令检测器实现 — 委托给 ICommandDangerClassifier,消除重复的硬编码字典
/// <para>保留 IDestructiveCommandDetector 接口供 CommandClassifier/ShellDeleteDetector 消费,</para>
/// <para>内部全部委托 CommandDangerClassifier 统一分级,唯一数据源 DangerousCommandCatalog</para>
/// </summary>
[Register(typeof(IDestructiveCommandDetector), ServiceLifetime.Singleton)]
public sealed partial class DestructiveCommandDetector : ServiceEntity, IDestructiveCommandDetector {
    private readonly ICommandDangerClassifier _classifier;

    /// <summary>
    /// 初始化破坏性命令检测器
    /// </summary>
    /// <param name="classifier">命令危险分类器</param>
    public DestructiveCommandDetector(ICommandDangerClassifier classifier) {
        _classifier = classifier;
    }

    /// <inheritdoc />
    public DestructiveCommandResult Detect(ShellCommand command) {
        var result = _classifier.Classify(command);
        return new DestructiveCommandResult(
            result.RequiresIntervention,
            result.RiskType != CommandRisk.None ? [result.RiskType] : [],
            result.Details);
    }
}