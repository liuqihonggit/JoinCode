
namespace Core.Hooks.Lifecycle;

/// <summary>
/// Post-sampling 回调管理器实现 — 管理和触发 IPostSamplingCallback 集合
/// </summary>
[Register(typeof(IPostSamplingCallbackManager), ServiceLifetime.Singleton)]
public sealed partial class PostSamplingCallbackManager : ServiceEntity, IPostSamplingCallbackManager {
    private readonly List<IPostSamplingCallback> _callbacks = [];
    private readonly ILogger<PostSamplingCallbackManager>? _logger;

    /// <summary>
    /// 构造 Post-sampling 回调管理器
    /// </summary>
    public PostSamplingCallbackManager(ILogger<PostSamplingCallbackManager>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 注册一个 Post-sampling 回调
    /// </summary>
    public void Register(IPostSamplingCallback callback) {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(callback);
    }

    /// <summary>
    /// 触发所有已注册回调的 Post-sampling 事件 — 单个回调异常不会中断其他回调
    /// </summary>
    public async Task FireAsync(PostSamplingContext context) {
        if (_callbacks.Count == 0) return;

        var tasks = _callbacks.Select(async callback => {
            try {
                await callback.OnPostSamplingAsync(context).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "PostSampling 回调 {CallbackType} 执行失败", callback.GetType().Name);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}