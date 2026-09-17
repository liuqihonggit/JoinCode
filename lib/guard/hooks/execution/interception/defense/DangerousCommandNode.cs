namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// 危险命令检测 node — 独立公共对象，委托 <see cref="ICommandDangerClassifier"/> 分类命令危险等级。
/// <para>
/// MTP 扰动纵深防御约束第4条：AC 分位置作用，Dangerous 级直接拒绝。
/// 补 CommandInterceptionDispatcher 缺失：Dispatcher 内唯一用分类器的 CmdIndirectCallGuard 只处理
/// cmd /c / powershell -Command 间接调用形式，直接 git -c 等命令在 CanHandle 阶段被跳过。
/// 本 node 在 BashDefense 链首位拦截所有 Dangerous 级直接命令。
/// </para>
/// </summary>
[Register(typeof(DangerousCommandNode), ServiceLifetime.Singleton)]
public sealed class DangerousCommandNode
{
    private readonly ICommandDangerClassifier _classifier;

    /// <summary>
    /// 构造危险命令检测 node
    /// </summary>
    /// <param name="classifier">命令危险分类器（唯一数据源）</param>
    public DangerousCommandNode(ICommandDangerClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    /// <summary>
    /// 分类命令危险等级，返回 Dangerous 级分类结果。
    /// </summary>
    /// <param name="command">待检测命令</param>
    /// <returns>Dangerous 级时返回分类结果（含风险类型+详情）；非 Dangerous 返回 null</returns>
    public DangerClassificationResult? ClassifyDangerous(string command)
    {
        var result = _classifier.Classify(command);
        return result.IsDangerous ? result : null;
    }
}
