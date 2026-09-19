namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理引擎配置 — 控制推理结构大小、预算和裁决行为
/// </summary>
public sealed class ReasoningOptions {
    /// <summary>
    /// DAG 最大节点数（包含假定、证据、裁决），超过后拒绝添加新节点
    /// </summary>
    public int MaxNodes { get; init; } = 100;

    /// <summary>
    /// 每个假定最大证据数（控方 + 辩方合计），超过后拒绝添加新证据
    /// </summary>
    public int MaxEvidencePerClaim { get; init; } = 20;

    /// <summary>
    /// DAG 最大深度（从根节点到叶节点的最长路径），超过后拒绝添加更深层节点
    /// </summary>
    public int MaxDepth { get; init; } = 10;

    /// <summary>
    /// 有限视锥窗口大小 — 每个角色同时可见的最大片段数
    /// </summary>
    public int ConeWindowSize { get; init; } = 5;

    /// <summary>
    /// 对抗流程最大轮次预算，谁先触底谁停止
    /// </summary>
    public int MaxAdversarialRounds { get; init; } = 5;

    /// <summary>
    /// Token 预算上限，谁先触底谁停止
    /// </summary>
    public int MaxTokens { get; init; } = 10000;

    /// <summary>
    /// 续费时默认增加的轮次数
    /// </summary>
    public int DefaultRefillRounds { get; init; } = 3;

    /// <summary>
    /// 续费时默认增加的 token 数
    /// </summary>
    public int DefaultRefillTokens { get; init; } = 5000;

    /// <summary>
    /// 续费方式默认值
    /// </summary>
    public BudgetRefillMode DefaultRefillMode { get; init; } = BudgetRefillMode.Both;

    /// <summary>
    /// 法官裁决阈值 — 控方权重达到此值且超过辩方指定倍数时接受
    /// </summary>
    public double AcceptThreshold { get; init; } = 3.0;

    /// <summary>
    /// 法官裁决倍率 — 控方权重需超过辩方权重的此倍数才接受
    /// </summary>
    public double AcceptMultiplier { get; init; } = 1.5;

    /// <summary>
    /// 法官裁决倍率 — 辩方权重超过控方权重的此倍数时驳回
    /// </summary>
    public double RejectMultiplier { get; init; } = 1.2;

    /// <summary>
    /// 控辩双方权重差值小于此值时判定为势均力敌，需补充证据
    /// </summary>
    public double PendingWeightDelta { get; init; } = 0.5;

    /// <summary>
    /// 辩方质疑阈值 — 控方证据数低于此值时辩方提出质疑
    /// </summary>
    public int DefenderDoubtThreshold { get; init; } = 2;

    /// <summary>
    /// 证据默认权重
    /// </summary>
    public double DefaultEvidenceWeight { get; init; } = 1.0;

    /// <summary>
    /// 控方LLM调用温度
    /// </summary>
    public float ProsecutorTemperature { get; init; } = 0.3f;

    /// <summary>
    /// 辩方LLM调用温度
    /// </summary>
    public float DefenderTemperature { get; init; } = 0.4f;

    /// <summary>
    /// 法官LLM调用温度
    /// </summary>
    public float JudgeTemperature { get; init; } = 0.2f;

    /// <summary>
    /// Agent单次LLM调用最大token数
    /// </summary>
    public int DefaultLlmMaxTokens { get; init; } = 2000;

    /// <summary>
    /// Agent Prompt 最大 token 估算值（超过此值触发压缩）
    /// </summary>
    public int MaxPromptTokens { get; init; } = 4000;

    /// <summary>
    /// DAG 节点摘要触发阈值（节点数超过此值时触发摘要）
    /// </summary>
    public int DagSummarizationThreshold { get; init; } = 30;

    /// <summary>
    /// 每轮对抗流程固定token开销（不含LLM调用）
    /// </summary>
    public int RoundOverheadTokens { get; init; } = 100;

    /// <summary>
    /// 降级/待补充时默认置信度
    /// </summary>
    public int DowngradedConfidence { get; init; } = 50;

    /// <summary>
    /// 驳回时默认置信度
    /// </summary>
    public int RejectedConfidence { get; init; } = 10;

    /// <summary>
    /// 裁决边权重
    /// </summary>
    public double VerdictEdgeWeight { get; init; } = 1.0;

    /// <summary>
    /// 杀人罪 — 证据阈值最高，闭环锁死，排除合理怀疑
    /// </summary>
    public static readonly ReasoningOptions Murder = new() {
        MaxNodes = 50,
        MaxEvidencePerClaim = 10,
        MaxDepth = 5,
        MaxAdversarialRounds = 3,
        MaxTokens = 5000,
        DefaultRefillRounds = 2,
        DefaultRefillTokens = 3000,
        AcceptThreshold = 5.0,
        AcceptMultiplier = 2.0,
        RejectMultiplier = 1.5,
        PendingWeightDelta = 1.0,
        DefenderDoubtThreshold = 3,
    };

    /// <summary>
    /// 吃熊猫罪 — 证据阈值动态，视情节浮动，中间层
    /// </summary>
    public static readonly ReasoningOptions Panda = new();

    /// <summary>
    /// 离婚官司 — 证据阈值最低，高度盖然性即可
    /// </summary>
    public static readonly ReasoningOptions Divorce = new() {
        MaxNodes = 500,
        MaxEvidencePerClaim = 50,
        MaxDepth = 20,
        MaxAdversarialRounds = 10,
        MaxTokens = 50000,
        DefaultRefillRounds = 5,
        DefaultRefillTokens = 10000,
        AcceptThreshold = 1.5,
        AcceptMultiplier = 1.2,
        RejectMultiplier = 1.5,
        PendingWeightDelta = 0.3,
        DefenderDoubtThreshold = 1,
    };

    /// <summary>
    /// 根据预设枚举获取配置
    /// </summary>
    public static ReasoningOptions FromPreset(ReasoningPreset preset) => preset switch {
        ReasoningPreset.Murder => Murder,
        ReasoningPreset.Panda => Panda,
        ReasoningPreset.Divorce => Divorce,
        _ => Panda,
    };

    /// <summary>
    /// 检查是否已达到节点数上限
    /// </summary>
    public bool IsNodeLimitReached(int currentCount) => currentCount >= MaxNodes;

    /// <summary>
    /// 检查是否已达到单假定证据数上限
    /// </summary>
    public bool IsEvidenceLimitReached(int currentEvidenceCount) => currentEvidenceCount >= MaxEvidencePerClaim;
}

/// <summary>
/// 推理引擎配置构建器 — 支持链式配置
/// </summary>
public sealed class ReasoningOptionsBuilder {
    private int _maxNodes = 100;
    private int _maxEvidencePerClaim = 20;
    private int _maxDepth = 10;
    private int _maxAdversarialRounds = 5;
    private int _maxTokens = 10000;
    private int _defaultRefillRounds = 3;
    private int _defaultRefillTokens = 5000;
    private BudgetRefillMode _defaultRefillMode = BudgetRefillMode.Both;
    private double _acceptThreshold = 3.0;
    private double _acceptMultiplier = 1.5;
    private double _rejectMultiplier = 1.2;
    private double _pendingWeightDelta = 0.5;
    private int _defenderDoubtThreshold = 2;
    private double _defaultEvidenceWeight = 1.0;

    private ReasoningOptionsBuilder() { }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static ReasoningOptionsBuilder Create() => new();

    /// <summary>
    /// 从吃熊猫罪预设开始（中间层）
    /// </summary>
    public static ReasoningOptionsBuilder CreatePanda() => Create();

    /// <summary>
    /// 从杀人罪预设开始（最严格）
    /// </summary>
    public static ReasoningOptionsBuilder CreateMurder() => Create()
        .WithMaxNodes(50)
        .WithMaxEvidencePerClaim(10)
        .WithMaxDepth(5)
        .WithMaxAdversarialRounds(3)
        .WithMaxTokens(5000)
        .WithDefaultRefillRounds(2)
        .WithDefaultRefillTokens(3000)
        .WithAcceptThreshold(5.0)
        .WithAcceptMultiplier(2.0)
        .WithRejectMultiplier(1.5)
        .WithPendingWeightDelta(1.0)
        .WithDefenderDoubtThreshold(3);

    /// <summary>
    /// 从离婚官司预设开始（最宽松）
    /// </summary>
    public static ReasoningOptionsBuilder CreateDivorce() => Create()
        .WithMaxNodes(500)
        .WithMaxEvidencePerClaim(50)
        .WithMaxDepth(20)
        .WithMaxAdversarialRounds(10)
        .WithMaxTokens(50000)
        .WithDefaultRefillRounds(5)
        .WithDefaultRefillTokens(10000)
        .WithAcceptThreshold(1.5)
        .WithAcceptMultiplier(1.2)
        .WithRejectMultiplier(1.5)
        .WithPendingWeightDelta(0.3)
        .WithDefenderDoubtThreshold(1);

    /// <summary>
    /// 从预设枚举开始
    /// </summary>
    public static ReasoningOptionsBuilder FromPreset(ReasoningPreset preset) => preset switch {
        ReasoningPreset.Murder => CreateMurder(),
        ReasoningPreset.Panda => CreatePanda(),
        ReasoningPreset.Divorce => CreateDivorce(),
        _ => CreatePanda(),
    };

    /// <summary>
    /// 设置 DAG 最大节点数
    /// </summary>
    /// <param name="maxNodes">DAG 最大节点数</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithMaxNodes(int maxNodes) { _maxNodes = maxNodes; return this; }

    /// <summary>
    /// 设置每个假定最大证据数
    /// </summary>
    /// <param name="max">每个假定最大证据数（控方 + 辩方合计）</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithMaxEvidencePerClaim(int max) { _maxEvidencePerClaim = max; return this; }

    /// <summary>
    /// 设置 DAG 最大深度
    /// </summary>
    /// <param name="maxDepth">DAG 最大深度（从根节点到叶节点的最长路径）</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithMaxDepth(int maxDepth) { _maxDepth = maxDepth; return this; }

    /// <summary>
    /// 设置对抗流程最大轮次预算
    /// </summary>
    /// <param name="rounds">对抗流程最大轮次</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithMaxAdversarialRounds(int rounds) { _maxAdversarialRounds = rounds; return this; }

    /// <summary>
    /// 设置 Token 预算上限
    /// </summary>
    /// <param name="maxTokens">Token 预算上限</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithMaxTokens(int maxTokens) { _maxTokens = maxTokens; return this; }

    /// <summary>
    /// 设置续费时默认增加的轮次数
    /// </summary>
    /// <param name="rounds">续费时默认增加的轮次数</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithDefaultRefillRounds(int rounds) { _defaultRefillRounds = rounds; return this; }

    /// <summary>
    /// 设置续费时默认增加的 token 数
    /// </summary>
    /// <param name="tokens">续费时默认增加的 token 数</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithDefaultRefillTokens(int tokens) { _defaultRefillTokens = tokens; return this; }

    /// <summary>
    /// 设置续费方式默认值
    /// </summary>
    /// <param name="mode">续费方式</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithDefaultRefillMode(BudgetRefillMode mode) { _defaultRefillMode = mode; return this; }

    /// <summary>
    /// 设置法官裁决阈值 — 控方权重达到此值且超过辩方指定倍数时接受
    /// </summary>
    /// <param name="threshold">法官裁决阈值</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithAcceptThreshold(double threshold) { _acceptThreshold = threshold; return this; }

    /// <summary>
    /// 设置法官裁决倍率 — 控方权重需超过辩方权重的此倍数才接受
    /// </summary>
    /// <param name="multiplier">法官裁决接受倍率</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithAcceptMultiplier(double multiplier) { _acceptMultiplier = multiplier; return this; }

    /// <summary>
    /// 设置法官裁决倍率 — 辩方权重超过控方权重的此倍数时驳回
    /// </summary>
    /// <param name="multiplier">法官裁决驳回倍率</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithRejectMultiplier(double multiplier) { _rejectMultiplier = multiplier; return this; }

    /// <summary>
    /// 设置势均力敌判定阈值 — 控辩双方权重差值小于此值时判定为势均力敌
    /// </summary>
    /// <param name="delta">权重差值阈值</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithPendingWeightDelta(double delta) { _pendingWeightDelta = delta; return this; }

    /// <summary>
    /// 设置辩方质疑阈值 — 控方证据数低于此值时辩方提出质疑
    /// </summary>
    /// <param name="threshold">辩方质疑阈值</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithDefenderDoubtThreshold(int threshold) { _defenderDoubtThreshold = threshold; return this; }

    /// <summary>
    /// 设置证据默认权重
    /// </summary>
    /// <param name="weight">证据默认权重</param>
    /// <returns>当前构建器实例（支持链式调用）</returns>
    public ReasoningOptionsBuilder WithDefaultEvidenceWeight(double weight) { _defaultEvidenceWeight = weight; return this; }

    /// <summary>
    /// 构建推理引擎配置
    /// </summary>
    public ReasoningOptions Build() => new() {
        MaxNodes = _maxNodes,
        MaxEvidencePerClaim = _maxEvidencePerClaim,
        MaxDepth = _maxDepth,
        MaxAdversarialRounds = _maxAdversarialRounds,
        MaxTokens = _maxTokens,
        DefaultRefillRounds = _defaultRefillRounds,
        DefaultRefillTokens = _defaultRefillTokens,
        DefaultRefillMode = _defaultRefillMode,
        AcceptThreshold = _acceptThreshold,
        AcceptMultiplier = _acceptMultiplier,
        RejectMultiplier = _rejectMultiplier,
        PendingWeightDelta = _pendingWeightDelta,
        DefenderDoubtThreshold = _defenderDoubtThreshold,
        DefaultEvidenceWeight = _defaultEvidenceWeight,
    };
}