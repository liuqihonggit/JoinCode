namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Onboarding 流程服务接口 - 管理首次启动引导流程的状态和导航
/// </summary>
public interface IOnboardingService
{
    /// <summary>
    /// 是否已完成 Onboarding 流程
    /// </summary>
    bool IsOnboardingComplete { get; }

    /// <summary>
    /// 当前 Onboarding 状态
    /// </summary>
    OnboardingState CurrentState { get; }

    /// <summary>
    /// 初始化服务 - 从持久化存储加载完成状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动 Onboarding 流程
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 前进到下一步
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task NextStepAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 返回上一步
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task PreviousStepAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 完成 Onboarding 流程并持久化完成状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 跳过 Onboarding 流程
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task SkipAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置 API Key
    /// </summary>
    /// <param name="apiKey">API Key 值</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 选择终端设置选项
    /// </summary>
    /// <param name="optionIndex">选项索引</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SelectTerminalSetupOptionAsync(int optionIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// 状态变更事件
    /// </summary>
    event EventHandler<OnboardingStateChangedEventArgs>? StateChanged;
}
