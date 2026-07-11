
namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// Post-sampling 回调管理器 — 管理和触发 IPostSamplingCallback 集合
/// </summary>
public interface IPostSamplingCallbackManager
{
    /// <summary>
    /// 注册回调
    /// </summary>
    void Register(IPostSamplingCallback callback);

    /// <summary>
    /// 触发所有已注册的回调（并行执行，异常隔离）
    /// </summary>
    Task FireAsync(PostSamplingContext context);
}
