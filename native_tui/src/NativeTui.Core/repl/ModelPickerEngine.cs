namespace NativeTui.Core.Repl;

/// <summary>
/// 模型选择引擎装饰器（M7-6，全部自研）— Submit 拦截 "/model"（精确匹配无参形态）打开
/// ReplShell 模型选择器，其余输入透传内层引擎；选中后回调宿主 applyModel 执行切换
/// （宿主直调引擎服务 — IFastModeService/IConfigurationService，不经 slash 命令系统）+ PostInfo 反馈。
/// 组装：宿主先构造装饰器 → new ReplShell(装饰器) → 装饰器.Attach(shell)（延迟绑定，
/// 对齐 Host controller ??= 先例避免构造循环依赖）。
/// </summary>
public sealed class ModelPickerEngine(
    IReplEngine inner,
    Func<IReadOnlyList<ModelPickerItem>> listProvider,
    Action<string> applyModel) : IReplEngine
{
    /// <summary>拦截的命令输入（基准 /model 无参形态打开选择器；带参形态透传引擎按未知输入处理）。</summary>
    private const string ModelCommand = "/model";

    /// <summary>外壳（延迟绑定 — Attach 注入）。</summary>
    private ReplShell? _shell;

    /// <summary>绑定外壳（new ReplShell(装饰器) 之后调用一次）。</summary>
    public void Attach(ReplShell shell) => _shell = shell;

    /// <summary>提交 — "/model" 拦截打开选择器（清单经 listProvider 从引擎投影）；其余透传。</summary>
    public void Submit(string text)
    {
        if (_shell is not null && text.Trim() == ModelCommand)
        {
            var items = listProvider();
            _shell.BeginModelPicker(items, id => OnSelected(id, items));
            return;
        }

        inner.Submit(text);
    }

    /// <summary>打断 — 透传内层。</summary>
    public void Interrupt() => inner.Interrupt();

    /// <summary>权限模式 — 透传内层。</summary>
    public void SetPermissionMode(string mode) => inner.SetPermissionMode(mode);

    /// <summary>排空事件 — 透传内层。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() => inner.DrainEvents();

    /// <summary>存活 — 透传内层。</summary>
    public bool IsAlive => inner.IsAlive;

    /// <summary>选中回调 — Esc 取消静默（基准同）；确认执行宿主切换 + 反馈消息
    ///（displayName 取自自持清单闭包 — 回调触发时 coordinator 状态已清空）。</summary>
    private void OnSelected(string? id, IReadOnlyList<ModelPickerItem> items)
    {
        if (id is null)
        {
            return;
        }

        applyModel(id);
        var displayName = items.FirstOrDefault(m => m.Id == id)?.DisplayName ?? id;
        _shell?.PostInfo($"已切换模型: {displayName}");
    }
}
