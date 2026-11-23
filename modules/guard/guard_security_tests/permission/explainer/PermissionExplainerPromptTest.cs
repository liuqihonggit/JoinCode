namespace Core.Tests.Permission;

/// <summary>
/// 权限解释提示词与响应解析（TASK069-P4）— 纯函数逐条钉死：
/// ①提示词四段齐全且带输出契约；②上下文只取 assistant 最后 3 条、≤1000 字符、旧→新；
/// ③解析容错到「能救就救」，但四字段缺一或等级非法一律 null（宁可说不可用，不给半截结论）。
/// </summary>
public sealed class PermissionExplainerPromptTest {
    private static ApiMessageRecord Assistant(string content) =>
        new() { Role = "assistant", Content = content };

    private static ApiMessageRecord User(string content) =>
        new() { Role = "user", Content = content };

    // ---- 提示词 ----

    /// <summary>用户提示含工具名、描述、输入三段，并明确要求只返回 JSON。</summary>
    [Fact]
    public void BuildUserPrompt_ContainsAllSections() {
        var prompt = PermissionExplainerPrompt.BuildUserPrompt(
            new PermissionExplainRequest("Bash", "需要确认: rm -rf /tmp/x", "执行 shell 命令"),
            "我先看看目录结构");

        prompt.Should().Contain("工具: Bash");
        prompt.Should().Contain("描述: 执行 shell 命令");
        prompt.Should().Contain("需要确认: rm -rf /tmp/x");
        prompt.Should().Contain("最近对话上下文:");
        prompt.Should().Contain("我先看看目录结构");
        prompt.Should().Contain("只返回一个 JSON 对象");
    }

    /// <summary>无描述/无上下文时不出现空段落，输入为空也不留空洞。</summary>
    [Fact]
    public void BuildUserPrompt_OmitsEmptySections() {
        var prompt = PermissionExplainerPrompt.BuildUserPrompt(
            new PermissionExplainRequest("Read", string.Empty),
            string.Empty);

        prompt.Should().Contain("输入:");
        prompt.Should().Contain("(无)");
        prompt.Should().NotContain("描述:");
        prompt.Should().NotContain("最近对话上下文");
    }

    /// <summary>输出格式示例与解析端同源 —— 字段名必须是解析器认识的四个。</summary>
    [Fact]
    public void BuildUserPrompt_ExampleMatchesParseContract() {
        var prompt = PermissionExplainerPrompt.BuildUserPrompt(new PermissionExplainRequest("Bash", "ls"), string.Empty);

        prompt.Should().Contain("\"explanation\"");
        prompt.Should().Contain("\"reasoning\"");
        prompt.Should().Contain("\"risk\"");
        prompt.Should().Contain("\"riskLevel\"");
    }

    // ---- 上下文抽取 ----

    /// <summary>只取 assistant 的文本，用户消息不算「我为什么要跑」的动机。</summary>
    [Fact]
    public void ExtractConversationContext_IgnoresNonAssistant() {
        var context = PermissionExplainerPrompt.ExtractConversationContext([
            User("帮我删掉构建产物"),
            Assistant("我先确认一下目录"),
        ]);

        context.Should().Be("我先确认一下目录");
    }

    /// <summary>最多 3 条，取最近的，且按旧→新拼接（反了就变成因果倒置）。</summary>
    [Fact]
    public void ExtractConversationContext_KeepsLastThreeInOrder() {
        var context = PermissionExplainerPrompt.ExtractConversationContext([
            Assistant("第一条"),
            Assistant("第二条"),
            Assistant("第三条"),
            Assistant("第四条"),
        ]);

        context.Should().Be("第二条\n\n第三条\n\n第四条");
    }

    /// <summary>总量上限 1000 字符（超出部分截断并打省略号）。</summary>
    [Fact]
    public void ExtractConversationContext_TruncatesToBudget() {
        var longText = new string('a', PermissionExplainerPrompt.ContextMaxChars + 500);

        var context = PermissionExplainerPrompt.ExtractConversationContext([Assistant(longText)]);

        context.Length.Should().Be(PermissionExplainerPrompt.ContextMaxChars + 3);
        context.Should().EndWith("...");
    }

    // ---- 响应解析 ----

    /// <summary>标准 JSON → 四段齐全，等级按基准字符串反查。</summary>
    [Fact]
    public void TryParse_ValidJson_ReturnsExplanation() {
        var result = PermissionExplainerPrompt.TryParse(
            """{"explanation":"删除构建目录","reasoning":"我需要清理旧产物","risk":"可能误删未提交的改动","riskLevel":"HIGH"}""");

        var explanation = result.Should().BeOfType<PermissionExplanation>().Subject;
        explanation.RiskLevel.Should().Be(PermissionRiskLevel.High);
        explanation.Explanation.Should().Be("删除构建目录");
        explanation.Reasoning.Should().Be("我需要清理旧产物");
        explanation.Risk.Should().Be("可能误删未提交的改动");
    }

    /// <summary>带 ```json 围栏 + 前后闲话照样能解析（模型最常见的两种「不听话」）。</summary>
    [Fact]
    public void TryParse_FencedWithChatter_StillParses() {
        var result = PermissionExplainerPrompt.TryParse(
            "好的，分析如下：\n```json\n{\"explanation\":\"列出文件\",\"reasoning\":\"我需要确认目录\",\"risk\":\"无\",\"riskLevel\":\"LOW\"}\n```\n希望有帮助。");

        result.Should().BeOfType<PermissionExplanation>().Subject.RiskLevel.Should().Be(PermissionRiskLevel.Low);
    }

    /// <summary>等级字符串大小写不敏感（模型常写 low）。</summary>
    [Fact]
    public void TryParse_LowercaseRiskLevel_StillParses() {
        var result = PermissionExplainerPrompt.TryParse(
            """{"explanation":"a","reasoning":"b","risk":"c","riskLevel":"medium"}""");

        result.Should().BeOfType<PermissionExplanation>().Subject.RiskLevel.Should().Be(PermissionRiskLevel.Medium);
    }

    /// <summary>四字段缺一 → null（半截解释比没有更危险：用户会以为已经看全了）。</summary>
    [Theory]
    [InlineData("""{"reasoning":"b","risk":"c","riskLevel":"LOW"}""")]
    [InlineData("""{"explanation":"a","risk":"c","riskLevel":"LOW"}""")]
    [InlineData("""{"explanation":"a","reasoning":"b","riskLevel":"LOW"}""")]
    [InlineData("""{"explanation":"a","reasoning":"b","risk":"c"}""")]
    [InlineData("""{"explanation":"  ","reasoning":"b","risk":"c","riskLevel":"LOW"}""")]
    public void TryParse_MissingOrBlankField_ReturnsNull(string json) {
        PermissionExplainerPrompt.TryParse(json).Should().BeNull();
    }

    /// <summary>等级不是三档之一 → null（不猜、不降级成中性）。</summary>
    [Theory]
    [InlineData("""{"explanation":"a","reasoning":"b","risk":"c","riskLevel":"CRITICAL"}""")]
    [InlineData("""{"explanation":"a","reasoning":"b","risk":"c","riskLevel":""}""")]
    public void TryParse_UnknownRiskLevel_ReturnsNull(string json) {
        PermissionExplainerPrompt.TryParse(json).Should().BeNull();
    }

    /// <summary>非 JSON / 空响应 → null，且不抛异常。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("抱歉，我无法分析这条命令。")]
    [InlineData("{不是合法 JSON}")]
    public void TryParse_Garbage_ReturnsNull(string? response) {
        PermissionExplainerPrompt.TryParse(response).Should().BeNull();
    }
}
