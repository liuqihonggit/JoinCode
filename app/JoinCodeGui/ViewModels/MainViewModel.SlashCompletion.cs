namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 的斜杠补全 partial — 处理 / 命令、@ 子代理、# 文件补全的解析、刷新、回填、导航。
/// 从 MainViewModel.cs 拆出以满足 JCC8001 文件行数限制。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>斜杠命令缓存（懒加载；空命令时回退内置高频命令列表）</summary>
    private IReadOnlyList<SlashCommandItem> _slashCommandCache = [];

    /// <summary>引擎可用工具缓存 — AttachRealSession 时异步加载（保留供 /tools 等命令消费）</summary>
    private IReadOnlyList<ToolSummary> _availableToolsCache = [];

    /// <summary>引擎可用子代理缓存 — AttachRealSession 时异步加载，@子代理补全消费</summary>
    private IReadOnlyList<SubAgentSummary> _availableSubAgentsCache = [];

    /// <summary>当前光标位置（由 View 层同步，用于解析斜杠命令前缀）</summary>
    [ObservableProperty]
    private int _inputCaretIndex;

    /// <summary>最近一次斜杠解析结果（回填替换区间用）</summary>
    private SlashParseResult _slashParseResult;

    /// <summary>当前输入的斜杠命令过滤建议（驱动内联补全下拉）</summary>
    public ObservableCollection<SlashCommandItem> SlashSuggestions { get; } = [];

    /// <summary>斜杠命令补全下拉是否打开（解析触发且有匹配命令时）</summary>
    public bool IsSlashPopupOpen => _slashParseResult.ShouldComplete && SlashSuggestions.Count > 0;

    /// <summary>补全面板头部模式徽章文本（命令/参数/文件/代理模式）</summary>
    public string SlashModeLabel => _slashParseResult.Mode switch
    {
        SlashCompletionMode.Argument => "参数补全",
        SlashCompletionMode.File => "文件补全",
        SlashCompletionMode.Agent => "代理补全",
        _ => "斜杠命令"
    };

    /// <summary>斜杠建议当前选中索引（↑↓ 导航）</summary>
    [ObservableProperty]
    private int _slashSelectedIndex = -1;

    /// <summary>刷新斜杠命令建议 — 由 View 层防抖后调用，用光标解析 + Trie 匹配 + 排序</summary>
    public void RefreshSlashSuggestions()
    {
        if (IsBusy)
        {
            ClearSlashSuggestions();
            return;
        }

        _slashParseResult = SlashCommandParser.Parse(InputText, InputCaretIndex);
        if (!_slashParseResult.ShouldComplete)
        {
            ClearSlashSuggestions();
            return;
        }

        if (_slashParseResult.Mode == SlashCompletionMode.Argument)
        {
            RefreshArgumentSuggestions();
            return;
        }

        if (_slashParseResult.Mode == SlashCompletionMode.File)
        {
            RefreshFileSuggestions();
            return;
        }

        if (_slashParseResult.Mode == SlashCompletionMode.Agent)
        {
            RefreshAgentSuggestions();
            return;
        }

        var cache = _slashCommandCache ??= BuildSlashCommandCache();
        var matched = SlashCommandItem.Filter(_slashParseResult.Prefix, cache);
        var ranked = SlashCommandRanker.Rank(matched, _slashParseResult.Prefix);

        SlashSuggestions.Clear();
        var prefixLen = _slashParseResult.Prefix.Length;
        foreach (var item in ranked)
        {
            item.MatchedPart = item.Name.Length >= prefixLen ? item.Name[..prefixLen] : item.Name;
            item.RemainingPart = item.Name.Length >= prefixLen ? item.Name[prefixLen..] : string.Empty;
            SlashSuggestions.Add(item);
        }
        SlashSelectedIndex = SlashSuggestions.Count > 0 ? 0 : -1;
        NotifySlashPanelChanged();
    }

    /// <summary>刷新命令参数补全候选 — 由 CommandArgumentProvider 按命令名提供参数列表</summary>
    private void RefreshArgumentSuggestions()
    {
        var args = CommandArgumentProvider.GetArguments(
            _slashParseResult.CommandName, _slashParseResult.ArgumentPrefix, _session);

        SlashSuggestions.Clear();
        var prefixLen = _slashParseResult.ArgumentPrefix.Length;
        foreach (var item in args)
        {
            item.MatchedPart = item.Name.Length >= prefixLen ? item.Name[..prefixLen] : item.Name;
            item.RemainingPart = item.Name.Length >= prefixLen ? item.Name[prefixLen..] : string.Empty;
            SlashSuggestions.Add(item);
        }
        SlashSelectedIndex = SlashSuggestions.Count > 0 ? 0 : -1;
        NotifySlashPanelChanged();
    }

    /// <summary>刷新文件补全候选 — 由 FileCompletionProvider 扫描当前工作目录</summary>
    private void RefreshFileSuggestions()
    {
        var files = FileCompletionProvider.GetFiles(_slashParseResult.Prefix);
        PopulateSuggestions(files, _slashParseResult.Prefix);
    }

    /// <summary>刷新子代理补全候选 — 由 AgentCompletionProvider 提供子代理列表</summary>
    private void RefreshAgentSuggestions()
    {
        var agents = AgentCompletionProvider.GetAgents(_slashParseResult.Prefix, _availableSubAgentsCache);
        PopulateSuggestions(agents, _slashParseResult.Prefix);
    }

    /// <summary>填充补全候选列表（高亮匹配前缀）</summary>
    private void PopulateSuggestions(IReadOnlyList<SlashCommandItem> candidates, string prefix)
    {
        SlashSuggestions.Clear();
        var prefixLen = prefix.Length;
        foreach (var item in candidates)
        {
            item.MatchedPart = item.Name.Length >= prefixLen ? item.Name[..prefixLen] : item.Name;
            item.RemainingPart = item.Name.Length >= prefixLen ? item.Name[prefixLen..] : string.Empty;
            SlashSuggestions.Add(item);
        }
        SlashSelectedIndex = SlashSuggestions.Count > 0 ? 0 : -1;
        NotifySlashPanelChanged();
    }

    /// <summary>补全面板状态通知归纳 — 开关与模式徽章同步变更（规则6：消除 4 处重复 OnPropertyChanged）</summary>
    private void NotifySlashPanelChanged()
    {
        OnPropertyChanged(nameof(IsSlashPopupOpen));
        OnPropertyChanged(nameof(SlashModeLabel));
    }

    /// <summary>清空斜杠建议并关闭面板</summary>
    private void ClearSlashSuggestions()
    {
        _slashParseResult = SlashParseResult.None;
        SlashSuggestions.Clear();
        SlashSelectedIndex = -1;
        NotifySlashPanelChanged();
    }

    /// <summary>关闭斜杠补全面板（Esc 调用，不清空输入框文本）</summary>
    public void CloseSlashPopup() => ClearSlashSuggestions();

    /// <summary>构建斜杠命令缓存 — 引擎命令为空时回退内置高频子集</summary>
    private IReadOnlyList<SlashCommandItem> BuildSlashCommandCache()
    {
        var metadata = GetAvailableSlashCommands();
        return metadata.Count > 0 ? SlashCommandItem.FromMetadata(metadata) : SlashCommandItem.BuiltInCommands;
    }

    /// <summary>使斜杠命令缓存失效 — 运行时动态增删命令后调用，下次刷新时重建 Trie</summary>
    public void InvalidateSlashCommandCache() => _slashCommandCache = [];

    /// <summary>完成斜杠命令补全 — 将选中命令回填到光标位置（替换 / 到前缀结束区间），不破坏其他文本</summary>
    public void CompleteSlashSuggestion()
    {
        if (SlashSelectedIndex < 0 || SlashSelectedIndex >= SlashSuggestions.Count)
            return;
        if (!_slashParseResult.ShouldComplete)
            return;

        var item = SlashSuggestions[SlashSelectedIndex];

        if (_slashParseResult.Mode == SlashCompletionMode.Argument)
        {
            var argStart = _slashParseResult.ArgumentStart;
            var prefixEnd = _slashParseResult.PrefixEnd;
            InputText = InputText[..argStart] + item.Name + InputText[prefixEnd..];
            InputCaretIndex = argStart + item.Name.Length;
        }
        else if (_slashParseResult.Mode == SlashCompletionMode.File ||
                 _slashParseResult.Mode == SlashCompletionMode.Agent)
        {
            var triggerEnd = _slashParseResult.SlashIndex + 1;
            var prefixEnd = _slashParseResult.PrefixEnd;
            InputText = InputText[..triggerEnd] + item.Name + " " + InputText[prefixEnd..];
            InputCaretIndex = triggerEnd + item.Name.Length + 1;
        }
        else
        {
            var slashIndex = _slashParseResult.SlashIndex;
            var prefixEnd = _slashParseResult.PrefixEnd;
            InputText = InputText[..slashIndex] + item.Name + " " + InputText[prefixEnd..];
            InputCaretIndex = slashIndex + item.Name.Length + 1;
        }
        ClearSlashSuggestions();
    }

    /// <summary>斜杠建议导航（↑ 传 -1，↓ 传 1），到顶/到底不再移动（不循环回绕）</summary>
    public void SlashNavigate(int delta)
    {
        if (SlashSuggestions.Count == 0)
            return;
        SlashSelectedIndex = Math.Clamp(SlashSelectedIndex + delta, 0, SlashSuggestions.Count - 1);
    }
}
