namespace Core.Tests.Hooks.Configuration;

/// <summary>
/// HookConfigurationGroup 确定性测试 — GetMatcherPriority 空集合守卫 / GetSortedMatchers 排序。
/// </summary>
public sealed class HookConfigurationGroupTests {

    private static SourcedHookConfig CreateConfig(
        HookEvent evt = HookEvent.PreToolUse,
        string? matcher = null,
        HookSource source = HookSource.UserSettings,
        string command = "echo test") {
        return new SourcedHookConfig {
            Event = evt,
            Matcher = matcher,
            Command = new BashCommandHook { Command = command },
            Source = source
        };
    }

    #region GetMatcherPriority — 空集合守卫

    [Fact]
    public void GetMatcherPriority_不存在的匹配器_应返回_MaxValue_而非抛异常() {
        var group = new HookConfigurationGroup();

        // 空集合不应抛 InvalidOperationException
        var priority = group.GetMatcherPriority(HookEvent.PreToolUse, "Bash");
        priority.Should().Be(int.MaxValue);
    }

    [Fact]
    public void GetMatcherPriority_不存在的事件_应返回_MaxValue_而非抛异常() {
        var group = new HookConfigurationGroup();
        group.Add(CreateConfig(evt: HookEvent.PreToolUse, matcher: "Bash"));

        // 事件不匹配,GetHooks 返回空,应返回 int.MaxValue
        var priority = group.GetMatcherPriority(HookEvent.PostToolUse, "Bash");
        priority.Should().Be(int.MaxValue);
    }

    [Fact]
    public void GetMatcherPriority_单个来源_应返回该来源优先级() {
        var group = new HookConfigurationGroup();
        group.Add(CreateConfig(evt: HookEvent.PreToolUse, matcher: "Bash", source: HookSource.UserSettings));

        var priority = group.GetMatcherPriority(HookEvent.PreToolUse, "Bash");
        priority.Should().Be(HookSource.UserSettings.GetPriority());
    }

    [Fact]
    public void GetMatcherPriority_多来源_应返回最低优先级值() {
        var group = new HookConfigurationGroup();
        group.Add(CreateConfig(evt: HookEvent.PreToolUse, matcher: "Bash", source: HookSource.UserSettings));
        group.Add(CreateConfig(evt: HookEvent.PreToolUse, matcher: "Bash", source: HookSource.ProjectSettings));

        var priority = group.GetMatcherPriority(HookEvent.PreToolUse, "Bash");
        // UserSettings=0, ProjectSettings=1, Min=0
        priority.Should().Be(0);
    }

    #endregion

    #region GetSortedMatchers — 排序验证

    [Fact]
    public void GetSortedMatchers_空组_应返回空列表() {
        var group = new HookConfigurationGroup();
        group.GetSortedMatchers(HookEvent.PreToolUse).Should().BeEmpty();
    }

    [Fact]
    public void GetSortedMatchers_不存在的匹配器_应返回空列表() {
        var group = new HookConfigurationGroup();
        group.Add(CreateConfig(evt: HookEvent.PreToolUse, matcher: "Bash"));

        group.GetSortedMatchers(HookEvent.PostToolUse).Should().BeEmpty();
    }

    [Fact]
    public void GetSortedMatchers_多匹配器_应按优先级排序() {
        var group = new HookConfigurationGroup();
        // ProjectSettings(1) 优先级低于 UserSettings(0),所以 "Bash" 应排在 "Grep" 前面
        group.Add(CreateConfig(evt: HookEvent.PreToolUse, matcher: "Grep", source: HookSource.ProjectSettings));
        group.Add(CreateConfig(evt: HookEvent.PreToolUse, matcher: "Bash", source: HookSource.UserSettings));

        var sorted = group.GetSortedMatchers(HookEvent.PreToolUse);
        sorted.Should().HaveCount(2);
        sorted[0].Should().Be("Bash");  // UserSettings=0,优先级更高
        sorted[1].Should().Be("Grep");  // ProjectSettings=1
    }

    #endregion
}
