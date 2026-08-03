using Core.Prompts.Utils;
using FluentAssertions;

namespace Core.Tests.Prompts;

public class UserPromptKeywordAnalyzerTests
{
    [Fact]
    public void AnalyzeInput_GCPressure_ReturnsPerformanceAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("检查一下GC压力问题");

        result.Type.Should().Be(UserPromptKeywordType.PerformanceAudit);
        result.MatchedKeyword.Should().Be("GC压力");
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_SpanOptimization_ReturnsPerformanceAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("用Span优化这段代码");

        result.Type.Should().Be(UserPromptKeywordType.PerformanceAudit);
        result.MatchedKeyword.Should().Be("Span");
    }

    [Fact]
    public void AnalyzeInput_AsParallel_ReturnsPerformanceAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("这里可以用AsParallel吗");

        result.Type.Should().Be(UserPromptKeywordType.PerformanceAudit);
        result.MatchedKeyword.Should().Be("AsParallel");
    }

    [Fact]
    public void AnalyzeInput_TaskWhenAll_ReturnsPerformanceAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("改用Task.WhenAll并行");

        result.Type.Should().Be(UserPromptKeywordType.PerformanceAudit);
        result.MatchedKeyword.Should().Be("Task.WhenAll");
    }

    [Fact]
    public void AnalyzeInput_Deadlock_ReturnsDeadlockAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("这里可能有死锁问题");

        result.Type.Should().Be(UserPromptKeywordType.DeadlockAudit);
        result.MatchedKeyword.Should().Be("死锁");
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_Hung_ReturnsDeadlockAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("程序卡死了");

        result.Type.Should().Be(UserPromptKeywordType.DeadlockAudit);
        result.MatchedKeyword.Should().Be("卡死");
    }

    [Fact]
    public void AnalyzeInput_RaceCondition_ReturnsDeadlockAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("这是竞态条件吗");

        result.Type.Should().Be(UserPromptKeywordType.DeadlockAudit);
        result.MatchedKeyword.Should().Be("竞态条件");
    }

    [Fact]
    public void AnalyzeInput_Elite_ReturnsCompetitiveEdge()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("elite mode");

        result.Type.Should().Be(UserPromptKeywordType.CompetitiveEdge);
        result.MatchedKeyword.Should().Be("elite");
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_Codex_ReturnsCompetitiveEdge()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("codex benchmark");

        result.Type.Should().Be(UserPromptKeywordType.CompetitiveEdge);
        result.MatchedKeyword.Should().Be("codex");
    }

    [Fact]
    public void AnalyzeInput_Serious_ReturnsCompetitiveEdge()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("严肃点做这个");

        result.Type.Should().Be(UserPromptKeywordType.CompetitiveEdge);
        result.MatchedKeyword.Should().Be("严肃");
    }

    [Fact]
    public void AnalyzeInput_Replace_ReturnsReplacementMethodology()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("替换所有的旧方法名");

        result.Type.Should().Be(UserPromptKeywordType.ReplacementMethodology);
        result.MatchedKeyword.Should().Be("替换");
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_BatchReplace_ReturnsReplacementMethodology()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("批量替换命名空间");

        result.Type.Should().Be(UserPromptKeywordType.ReplacementMethodology);
        result.MatchedKeyword.Should().Be("批量替换");
    }

    [Fact]
    public void AnalyzeInput_Consolidate_ReturnsConsolidation()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("归纳整理这些代码");

        result.Type.Should().Be(UserPromptKeywordType.Consolidation);
        result.MatchedKeyword.Should().Be("归纳");
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_Modify_ReturnsStructuredTaskWorkflow()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("帮我修改这个函数");

        result.Type.Should().Be(UserPromptKeywordType.StructuredTaskWorkflow);
        result.MatchedKeyword.Should().Be("修改");
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_Refactor_ReturnsStructuredTaskWorkflow()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("重构这段代码");

        result.Type.Should().Be(UserPromptKeywordType.StructuredTaskWorkflow);
        result.MatchedKeyword.Should().Be("重构");
    }

    [Fact]
    public void AnalyzeInput_FuckingBroken_ReturnsNegative()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("this is fucking broken");

        result.Type.Should().Be(UserPromptKeywordType.Negative);
        result.MatchedKeyword.Should().NotBeNullOrEmpty();
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_Continue_ReturnsKeepGoing()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("continue");

        result.Type.Should().Be(UserPromptKeywordType.KeepGoing);
        result.MatchedKeyword.Should().Be("continue");
        result.HasPromptInjection.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInput_NoMatch_ReturnsNone()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("今天天气怎么样");

        result.Type.Should().Be(UserPromptKeywordType.None);
        result.HasPromptInjection.Should().BeFalse();
    }

    [Fact]
    public void AnalyzeInput_Empty_ReturnsNone()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("");

        result.Type.Should().Be(UserPromptKeywordType.None);
    }

    [Fact]
    public void AnalyzeInput_Null_ReturnsNone()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput(null!);

        result.Type.Should().Be(UserPromptKeywordType.None);
    }

    [Fact]
    public void AnalyzeInput_Whitespace_ReturnsNone()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("   ");

        result.Type.Should().Be(UserPromptKeywordType.None);
    }

    [Fact]
    public void AnalyzeInput_CompetitiveEdgeHasPriorityOverPerformanceAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("认真做性能优化");

        result.Type.Should().Be(UserPromptKeywordType.CompetitiveEdge);
    }

    [Fact]
    public void AnalyzeInput_PerformanceAuditHasPriorityOverDeadlockAudit()
    {
        var result = UserPromptKeywordAnalyzer.AnalyzeInput("GC压力导致死锁");

        result.Type.Should().Be(UserPromptKeywordType.PerformanceAudit);
    }

    [Fact]
    public void MatchesCompetitiveEdgeKeyword_Elite_ReturnsTrue()
    {
        UserPromptKeywordAnalyzer.MatchesCompetitiveEdgeKeyword("elite mode").Should().BeTrue();
    }

    [Fact]
    public void MatchesCompetitiveEdgeKeyword_HelloWorld_ReturnsFalse()
    {
        UserPromptKeywordAnalyzer.MatchesCompetitiveEdgeKeyword("hello world").Should().BeFalse();
    }
}
