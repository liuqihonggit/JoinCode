
namespace Core.Hooks.Lifecycle;

/// <summary>
/// Post-sampling 回调管理器实现 — 管理和触发 IPostSamplingCallback 集合
/// </summary>
[Register]
public sealed partial class PostSamplingCallbackManager : IPostSamplingCallbackManager
{
    private readonly List<IPostSamplingCallback> _callbacks = [];
    private readonly ILogger<PostSamplingCallbackManager>? _logger;

    public PostSamplingCallbackManager(ILogger<PostSamplingCallbackManager>? logger = null)
    {
        _logger = logger;
    }

    public void Register(IPostSamplingCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(callback);
    }

    public async Task FireAsync(PostSamplingContext context)
    {
        if (_callbacks.Count == 0) return;

        var tasks = _callbacks.Select(async callback =>
        {
            try
            {
                await callback.OnPostSamplingAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "PostSampling 回调 {CallbackType} 执行失败", callback.GetType().Name);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
