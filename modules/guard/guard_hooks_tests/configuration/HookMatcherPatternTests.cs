namespace Core.Hooks.Tests.Configuration;

/// <summary>
/// TASK071 hooks-② — 钩子 matcher 模式匹配测试（对齐 TS matchesPattern hooks.ts:1346-1378）：
/// "*" 全匹配、"Bash|Edit" 管道精确、正则模式、非法正则安全回退；
/// 旧实现精确字典查找使通配/管道/正则配置静默失效。
/// </summary>
public sealed class HookMatcherPatternTests {
    private static HookConfigurationGroup CreateGroup(params string[] matchers) {
        var group = new HookConfigurationGroup();
        foreach (var m in matchers) {
            group.Add(new SourcedHookConfig {
                Event = HookEvent.PreToolUse,
                Command = new BashCommandHook { Command = "echo test" },
                Matcher = m,
                Source = HookSource.UserSettings,
            });
        }

        return group;
    }

    [Fact]
    public void GetHooks_WildcardPattern_MatchesAnyTool() {
        var group = CreateGroup("*");

        var hooks = group.GetHooks(HookEvent.PreToolUse, "AnyToolName");

        hooks.Should().ContainSingle("* 应匹配任意工具名（旧精确查找返回空）");
    }

    [Fact]
    public void GetHooks_PipePattern_MatchesEitherTool() {
        var group = CreateGroup("Bash|Edit");

        group.GetHooks(HookEvent.PreToolUse, "Bash").Should().ContainSingle("管道左支命中");
        group.GetHooks(HookEvent.PreToolUse, "Edit").Should().ContainSingle("管道右支命中");
        group.GetHooks(HookEvent.PreToolUse, "Grep").Should().BeEmpty("管道外工具不命中");
    }

    [Fact]
    public void GetHooks_RegexPattern_MatchesByRegex() {
        var group = CreateGroup("^Web.*");

        group.GetHooks(HookEvent.PreToolUse, "WebFetch").Should().ContainSingle("正则命中");
        group.GetHooks(HookEvent.PreToolUse, "Bash").Should().BeEmpty("正则不命中");
    }

    [Fact]
    public void GetHooks_ExactPattern_StillWorks() {
        var group = CreateGroup("Bash");

        group.GetHooks(HookEvent.PreToolUse, "Bash").Should().ContainSingle("精确名命中（兼容旧行为）");
        group.GetHooks(HookEvent.PreToolUse, "Edit").Should().BeEmpty();
    }

    [Fact]
    public void GetHooks_NullMatcher_MatchesEmptyAndWildcardOnly() {
        var group = CreateGroup("*", "Bash", "");

        // 无工具名事件（SessionStart 类）—— null query 匹配 ""（空键）与 "*"（通配），不匹配 "Bash"
        var hooks = group.GetHooks(HookEvent.PreToolUse, null);

        hooks.Should().HaveCount(2, "null query 应命中空键与通配，不命中精确名");
    }

    [Fact]
    public void GetHooks_InvalidRegex_SafeFallback() {
        var group = CreateGroup("([invalid");

        group.GetHooks(HookEvent.PreToolUse, "Bash").Should().BeEmpty("非法正则安全回退（对齐 TS log+false）");
    }

    [Fact]
    public void MatchesPattern_SemanticLock() {
        HookConfigurationGroup.MatchesPattern("AnyTool", "*").Should().BeTrue();
        HookConfigurationGroup.MatchesPattern("Bash", "Bash|Edit").Should().BeTrue();
        HookConfigurationGroup.MatchesPattern("Grep", "Bash|Edit").Should().BeFalse();
        HookConfigurationGroup.MatchesPattern("WebFetch", "^Web.*").Should().BeTrue();
        HookConfigurationGroup.MatchesPattern("Bash", "([invalid").Should().BeFalse();
        HookConfigurationGroup.MatchesPattern("Bash", "").Should().BeTrue("空模式全匹配（无工具名事件）");
    }
}
