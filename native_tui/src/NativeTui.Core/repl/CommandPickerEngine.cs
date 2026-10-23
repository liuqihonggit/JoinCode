namespace NativeTui.Core.Repl;

/// <summary>
/// 命令选择器装饰引擎（M7-6 ModelPickerEngine 泛化，全部自研）— Submit 拦截指定命令
/// （精确匹配无参形态，如 "/model" / "/vendor"）打开 ReplShell 选择器模态，其余输入透传内层引擎；
/// 选中后回调宿主 applyItem 执行切换（宿主直调引擎服务，不经 slash 命令系统）+ PostInfo 反馈。
/// 组装：宿主先构造装饰器 → new ReplShell(装饰器) → 装饰器.Attach(shell)（延迟绑定，
/// 对齐 Host controller ??= 先例避免构造循环依赖）。可叠层装饰（vendor 外层包 model 内层）。
/// TASK066 D5 — effortConfigProvider/applyEffort 可选注入（/model 专属，vendor 默认 null 不启用）：
/// 打开选择器时投影会话 effort 配置（引擎 IExecutionSettingsProvider.EffortLevel 种子），
/// 确认时切换档位回调宿主直调引擎执行设置 + 持久化（对齐 ModelCommand.PersistPickerEffortAsync 先例）。
/// </summary>
public sealed class CommandPickerEngine(
    IReplEngine inner,
    string command,
    string switchedNoun,
    Func<IReadOnlyList<ModelPickerItem>> listProvider,
    Action<string> applyItem,
    Func<PickerEffortConfig?>? effortConfigProvider = null,
    Action<PickerEffortLevel>? applyEffort = null) : IReplEngine
{
    /// <summary>外壳（延迟绑定 — Attach 注入）。</summary>
    private ReplShell? _shell;

    /// <summary>选择器标题（"Select {noun}" — Dialog 标题）。</summary>
    private string Title => $"Select {switchedNoun}";

    /// <summary>选择器副标题（Dialog 副标题 — 会话级生效语义）。</summary>
    private string Subtitle => $"Switch between {switchedNoun}s. Applies to this session and future sessions.";

    /// <summary>绑定外壳（new ReplShell(装饰器) 之后调用一次）— 叠层装饰时递归传播内层同族
    ///（组装方只 Attach 最外层，内层拦截依赖 shell 引用；同文件同族互知，类型耦合可接受）。</summary>
    public void Attach(ReplShell shell) {
        _shell = shell;
        if (inner is CommandPickerEngine nested) {
            nested.Attach(shell);
        }
    }

    /// <summary>提交 — 目标命令拦截打开选择器（清单经 listProvider 从引擎投影）；其余透传。
    /// TASK066 D5 — effort 配置/应用回调同步注入（provider null 时选择器不启用 effort 区）。</summary>
    public void Submit(string text)
    {
        if (_shell is not null && text.Trim() == command)
        {
            var items = listProvider();
            Action<PickerEffortLevel?>? onEffortApplied = applyEffort is null
                ? null
                : effort =>
                {
                    // null = Esc 取消/未切换/模型不支持 — 不回调（对齐基准 onSelect 第二参非 null 才传）
                    if (effort is { } level)
                    {
                        applyEffort(level);
                    }
                };
            _shell.BeginModelPicker(items, id => OnSelected(id, items), Title, Subtitle, effortConfigProvider?.Invoke(), onEffortApplied);
            return;
        }

        inner.Submit(text);
    }

    /// <summary>打断 — 透传内层。</summary>
    public void Interrupt() => inner.Interrupt();

    /// <summary>权限模式 — 透传内层。</summary>
    public void SetPermissionMode(string mode) => inner.SetPermissionMode(mode);

    /// <summary>停止后台任务（M8-8 — 透传内层；装饰器完整转发防吞停止请求）。</summary>
    public void StopBackgroundTask(string kind, string taskId) => inner.StopBackgroundTask(kind, taskId);

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

        applyItem(id);
        var displayName = items.FirstOrDefault(m => m.Id == id)?.DisplayName ?? id;
        _shell?.PostInfo($"已切换{switchedNoun}: {displayName}");
    }
}
