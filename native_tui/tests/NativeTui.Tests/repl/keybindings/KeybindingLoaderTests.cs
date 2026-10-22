namespace NativeTui.Tests.Repl;

/// <summary>
/// 键位加载器测试 — 对齐基准 loadUserBindings.ts + validate.ts 加载校验链：
/// 合法 json 覆盖生效（替换语义 — 用户键列表整体替换默认）、未知 context/action/键名 → 四要素错误
/// （含正确写法引导）且跳过该条、保留键（ctrl+c/ctrl+d/ctrl+m）不可重绑、坏 json → 语法错误含行列。
/// </summary>
public sealed class KeybindingLoaderTests
{
    /// <summary>解析 json 并断言无错误（合法路径测试用）。</summary>
    private static KeybindingProfile ParseClean(string json)
    {
        var result = KeybindingLoader.Parse(json);
        Assert.False(result.HasErrors);
        return result.Profile;
    }

    /// <summary>Resolve 结果的键位串集合（断言用 — 不依赖声明序）。</summary>
    private static string[] KeysOf(KeybindingProfile profile, KeybindingContext context, KeybindingAction action) =>
        profile.Resolve(context, action).Select(chord => chord.ToString()).ToArray();

    /// <summary>构造按键辅助（测试可读性）。</summary>
    private static KeyPress Ctrl(char letter) => new(KeyKind.Char, letter.ToString(), Ctrl: true);

    /// <summary>空对象 = 无覆盖：默认表原样返回且无错误。</summary>
    [Fact]
    public void Parse_EmptyObjectKeepsDefaults()
    {
        var profile = ParseClean("{}");
        Assert.Equal(2, KeysOf(profile, KeybindingContext.Chat, KeybindingAction.ChatExternalEditor).Length);
        Assert.Contains("ctrl+c", KeysOf(profile, KeybindingContext.Global, KeybindingAction.AppInterrupt));
    }

    /// <summary>合法覆盖生效：用户键列表整体替换默认（ctrl+g 单键替换 ctrl+g+ctrl+x ctrl+e 双键）。</summary>
    [Fact]
    public void Parse_ValidOverrideReplacesDefaultChords()
    {
        var profile = ParseClean("""{"chat": {"chat:externalEditor": ["ctrl+g"]}}""");
        Assert.Equal("ctrl+g", KeysOf(profile, KeybindingContext.Chat, KeybindingAction.ChatExternalEditor).Single());
        // 序列键被替换后不再可触发
        Assert.Null(profile.MatchSequence(KeybindingContext.Chat, [Ctrl('x'), Ctrl('e')]));
    }

    /// <summary>覆盖是局部的：未涉及的默认绑定原样保留。</summary>
    [Fact]
    public void Parse_OverrideKeepsUntouchedDefaults()
    {
        var profile = ParseClean("""{"chat": {"chat:externalEditor": ["ctrl+g"]}}""");
        Assert.Contains("ctrl+c", KeysOf(profile, KeybindingContext.Global, KeybindingAction.AppInterrupt));
        Assert.Contains("escape", KeysOf(profile, KeybindingContext.Chat, KeybindingAction.ChatCancel));
    }

    /// <summary>多键覆盖：数组多元素 = 一 action 多键位。</summary>
    [Fact]
    public void Parse_MultiKeyOverride()
    {
        var profile = ParseClean("""{"chat": {"chat:submit": ["enter", "ctrl+enter"]}}""");
        var keys = KeysOf(profile, KeybindingContext.Chat, KeybindingAction.ChatSubmit);
        Assert.Equal(2, keys.Length);
        Assert.Contains("enter", keys);
        Assert.Contains("ctrl+enter", keys);
    }

    /// <summary>空数组 = 解除绑定（对齐基准 null-unbind 语义）。</summary>
    [Fact]
    public void Parse_EmptyArrayUnbinds()
    {
        var profile = ParseClean("""{"chat": {"chat:submit": []}}""");
        Assert.Empty(KeysOf(profile, KeybindingContext.Chat, KeybindingAction.ChatSubmit));
        Assert.Null(profile.MatchAction(KeybindingContext.Chat, KeyPress.Named(KeyKind.Return)));
    }

    /// <summary>大小写宽容：context "CHAT"/action "CHAT:SUBMIT" 均命中（用户手打容错）。</summary>
    [Fact]
    public void Parse_CaseInsensitiveNames()
    {
        var profile = ParseClean("""{"CHAT": {"CHAT:SUBMIT": ["f9"]}}""");
        Assert.Equal("f9", KeysOf(profile, KeybindingContext.Chat, KeybindingAction.ChatSubmit).Single());
    }

    /// <summary>宽容解析：// 注释 + 尾逗号不判死（对齐主仓库 RelaxedJsonSerializer 理念）。</summary>
    [Fact]
    public void Parse_ToleratesCommentsAndTrailingCommas()
    {
        var json = """
            {
              // 主键位覆盖
              "chat": {"chat:submit": ["f9",],},
            }
            """;
        var profile = ParseClean(json);
        Assert.Equal("f9", KeysOf(profile, KeybindingContext.Chat, KeybindingAction.ChatSubmit).Single());
    }

    /// <summary>未知 action：错误含同前缀正确写法建议（chat:externalEditor 原名）且该条跳过。</summary>
    [Fact]
    public void Parse_UnknownActionReportsGuidanceAndSkips()
    {
        var result = KeybindingLoader.Parse("""{"chat": {"chat:externlEditor": ["ctrl+g"]}}""");
        var error = Assert.Single(result.Errors);
        Assert.Equal(KeybindingErrorKind.UnknownAction, error.Kind);
        Assert.Contains("chat:externalEditor", error.Message);
        Assert.Contains("chat:cancel", error.Message);
        // 错误条目跳过 — 默认双键位保留
        Assert.Equal(2, KeysOf(result.Profile, KeybindingContext.Chat, KeybindingAction.ChatExternalEditor).Length);
    }

    /// <summary>未知 context：错误含有效 context 清单提示且整块跳过。</summary>
    [Fact]
    public void Parse_UnknownContextReportsGuidanceAndSkips()
    {
        var result = KeybindingLoader.Parse("""{"chatx": {"chat:submit": ["enter"]}}""");
        var error = Assert.Single(result.Errors);
        Assert.Equal(KeybindingErrorKind.UnknownContext, error.Kind);
        Assert.Contains("chat", error.Message);
        Assert.Contains("global", error.Message);
    }

    /// <summary>未知键名：错误透传解析层四要素文本且该键跳过、其余键生效。</summary>
    [Fact]
    public void Parse_UnknownKeyReportsGuidanceAndSkips()
    {
        var result = KeybindingLoader.Parse("""{"chat": {"chat:submit": ["F13", "f9"]}}""");
        var error = Assert.Single(result.Errors);
        Assert.Equal(KeybindingErrorKind.UnknownKey, error.Kind);
        Assert.Contains("F13", error.Message);
        Assert.Contains("正确写法", error.Message);
        // 坏键跳过、好键生效（尽力合并）
        Assert.Equal("f9", KeysOf(result.Profile, KeybindingContext.Chat, KeybindingAction.ChatSubmit).Single());
    }

    /// <summary>保留键 ctrl+c/ctrl+d/ctrl+m 不可重绑（对齐基准 reservedShortcuts.ts NON_REBINDABLE）。</summary>
    [Theory]
    [InlineData("ctrl+c")]
    [InlineData("ctrl+d")]
    [InlineData("ctrl+m")]
    public void Parse_ReservedKeysRejected(string reservedKey)
    {
        var result = KeybindingLoader.Parse($$$"""{"global": {"app:redraw": ["{{{reservedKey}}}"]}}""");
        var error = Assert.Single(result.Errors);
        Assert.Equal(KeybindingErrorKind.ReservedKey, error.Kind);
        Assert.Contains(reservedKey, error.Message);
        Assert.Contains("不可重绑", error.Message);
        // 保留键条目跳过 — 默认 ctrl+l 保留
        Assert.Contains("ctrl+l", KeysOf(result.Profile, KeybindingContext.Global, KeybindingAction.AppRedraw));
    }

    /// <summary>序列含保留段合法：ctrl+x ctrl+c 整串 ≠ 保留键（对齐基准整串比较语义）。</summary>
    [Fact]
    public void Parse_SequenceContainingReservedStepIsAllowed()
    {
        var result = KeybindingLoader.Parse("""{"chat": {"chat:externalEditor": ["ctrl+x ctrl+c"]}}""");
        Assert.False(result.HasErrors);
        Assert.Equal("ctrl+x ctrl+c", KeysOf(result.Profile, KeybindingContext.Chat, KeybindingAction.ChatExternalEditor).Single());
    }

    /// <summary>坏 json：语法错误含 1-based 行列与正确格式示例。</summary>
    [Fact]
    public void Parse_BrokenJsonReportsSyntaxErrorWithPosition()
    {
        var result = KeybindingLoader.Parse("""{"chat": """);
        var error = Assert.Single(result.Errors);
        Assert.Equal(KeybindingErrorKind.JsonSyntax, error.Kind);
        Assert.NotNull(error.Line);
        Assert.Equal(1, error.Line);
        Assert.Contains("语法错误", error.Message);
        Assert.Contains("chat:externalEditor", error.Message);
    }

    /// <summary>顶层非对象（数组）：语法错误（根即 context 映射）。</summary>
    [Fact]
    public void Parse_TopLevelArrayRejected()
    {
        var result = KeybindingLoader.Parse("""["chat"]""");
        var error = Assert.Single(result.Errors);
        Assert.Equal(KeybindingErrorKind.JsonSyntax, error.Kind);
    }

    /// <summary>错与对并存：合法条目生效、错误条目跳过（把通过和错误都报告）。</summary>
    [Fact]
    public void Parse_MixedValidAndInvalidEntries()
    {
        var result = KeybindingLoader.Parse("""{"chat": {"chat:submit": ["f9"], "bogus:action": ["up"]}}""");
        Assert.Single(result.Errors);
        Assert.Equal("f9", KeysOf(result.Profile, KeybindingContext.Chat, KeybindingAction.ChatSubmit).Single());
    }

    /// <summary>文件不存在 → 纯默认表 + 无错误（对齐基准 ENOENT 静默回退）。</summary>
    [Fact]
    public void LoadFromFile_MissingFileFallsBackToDefaults()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"keybindings-missing-{Guid.NewGuid():N}.json");
        var result = KeybindingLoader.LoadFromFile(missing);
        Assert.False(result.HasErrors);
        Assert.Equal(2, KeysOf(result.Profile, KeybindingContext.Chat, KeybindingAction.ChatExternalEditor).Length);
    }

    /// <summary>真实文件 IO：写入临时 json → 加载覆盖生效（非 mock，ADR 0080 手动验收理念下沉到单测层；
    /// 临时文件交还 OS 清理 — 遵守禁删红线不主动删除）。</summary>
    [Fact]
    public void LoadFromFile_ReadsRealFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keybindings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"chat": {"chat:cancel": ["ctrl+q"]}}""");
        var result = KeybindingLoader.LoadFromFile(path);
        Assert.False(result.HasErrors);
        Assert.Equal("ctrl+q", KeysOf(result.Profile, KeybindingContext.Chat, KeybindingAction.ChatCancel).Single());
    }
}
