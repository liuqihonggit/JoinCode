namespace NativeTui.Core.Repl;

/// <summary>
/// TUI 问答协调器 — ask_user_question / confirm_action 挂起态的唯一状态源：
/// 问题序列、当前题焦点与多选集合、答案累积与 TCS 完结点；Begin 取代旧等待者，
/// Complete/Defer 幂等。渲染由 AskQuestionDialogItem 投影，按键路由由 ReplShell 驱动。
/// </summary>
public sealed class AskQuestionCoordinator
{
    private readonly List<TuiAskQuestion> _questions = [];
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly HashSet<int> _selected = [];
    private TaskCompletionSource<TuiAskResult>? _pending;
    private int _questionIndex;
    private int _focusIndex;

    /// <summary>无挂起时的已完成取消占位（Wait 安全值）。</summary>
    private static readonly Task<TuiAskResult> NoPendingTask = Task.FromResult(TuiAskResult.CancelledResult);

    /// <summary>是否挂起中（模态路由依据）。</summary>
    public bool IsPending => _pending is not null;

    /// <summary>本题集。</summary>
    public IReadOnlyList<TuiAskQuestion> Questions => _questions;

    /// <summary>当前题索引（0 基）。</summary>
    public int QuestionIndex => _questionIndex;

    /// <summary>当前题（无挂起/越界为 null）。</summary>
    public TuiAskQuestion? Current =>
        _questionIndex >= 0 && _questionIndex < _questions.Count ? _questions[_questionIndex] : null;

    /// <summary>当前题焦点选项索引（0 基）。</summary>
    public int FocusIndex => _focusIndex;

    /// <summary>当前题已选选项索引（多选集合；单选最多一项）。</summary>
    public IReadOnlySet<int> SelectedIndices => _selected;

    /// <summary>已收集答案（问题文本 → 选中标签连接串）。</summary>
    public IReadOnlyDictionary<string, string> Answers => _answers;

    /// <summary>挂起一批问题 — 取代旧等待者，状态复位到第一题。</summary>
    public void Begin(IReadOnlyList<TuiAskQuestion> questions)
    {
        CancelPending();
        _questions.Clear();
        _questions.AddRange(questions);
        _answers.Clear();
        _selected.Clear();
        _questionIndex = 0;
        _focusIndex = 0;
        _pending = new TaskCompletionSource<TuiAskResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>等待用户作答（无挂起时为已完成取消占位）。</summary>
    public Task<TuiAskResult> Wait() => _pending?.Task ?? NoPendingTask;

    /// <summary>移动焦点（上下环绕；无当前题为空操作）。</summary>
    public void MoveFocus(int delta)
    {
        if (Current is not { Options.Count: > 0 } current)
            return;

        var count = current.Options.Count;
        _focusIndex = ((_focusIndex + delta) % count + count) % count;
    }

    /// <summary>切换焦点选项（仅多选；单选不响应空格）。</summary>
    public void ToggleFocus()
    {
        if (Current is not { MultiSelect: true })
            return;

        if (!_selected.Remove(_focusIndex))
            _selected.Add(_focusIndex);
    }

    /// <summary>数字直选（1 基）— 单选立即提交，多选切换该项。</summary>
    /// <returns>本次操作是否使对话前进（提交成功）</returns>
    public bool SelectNumber(int oneBasedIndex)
    {
        if (Current is not { } current || oneBasedIndex < 1 || oneBasedIndex > current.Options.Count)
            return false;

        var index = oneBasedIndex - 1;
        _focusIndex = index;
        if (current.MultiSelect)
        {
            if (!_selected.Remove(index))
                _selected.Add(index);
            return false;
        }

        _selected.Clear();
        _selected.Add(index);
        return Submit();
    }

    /// <summary>提交当前题：单选以焦点项为答案，多选要求至少一项；最后一题完成整批。</summary>
    /// <returns>是否提交成功（未选择时为 false，对话停留原题）</returns>
    public bool Submit()
    {
        if (Current is not { } current)
            return false;

        if (!current.MultiSelect)
        {
            _selected.Clear();
            _selected.Add(_focusIndex);
        }

        if (_selected.Count == 0)
            return false;

        _answers[current.Question] = string.Join(", ", _selected.Order().Select(i => current.Options[i].Label));
        _selected.Clear();
        _focusIndex = 0;

        if (++_questionIndex >= _questions.Count)
        {
            Complete(TuiAskResult.Success(new Dictionary<string, string>(_answers, StringComparer.Ordinal)));
        }

        return true;
    }

    /// <summary>用户取消（Esc）— 返回取消结果。</summary>
    public void Cancel() => Complete(TuiAskResult.CancelledResult);

    /// <summary>后台回调到达 — 提前结束并携带已答部分。</summary>
    public void Defer() => Complete(TuiAskResult.DeferredResult(
        _answers.Count > 0 ? new Dictionary<string, string>(_answers, StringComparer.Ordinal) : null));

    private void Complete(TuiAskResult result)
    {
        var pending = _pending;
        _pending = null;
        _questions.Clear();
        _selected.Clear();
        _questionIndex = 0;
        _focusIndex = 0;
        pending?.TrySetResult(result);
    }

    /// <summary>取代旧等待者 — Begin 时取消未完成的旧挂起（对齐 CommandDialogCoordinator）。</summary>
    private void CancelPending()
    {
        var pending = _pending;
        _pending = null;
        pending?.TrySetCanceled();
    }
}
