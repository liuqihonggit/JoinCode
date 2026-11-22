namespace Core.Tests.Prompts;

/// <summary>
/// 基准对比调研提示词模板测试 — 四问深度法。
/// 验证模板含四问关键段落、参数插值正确、注册元数据齐全。
/// </summary>
public class BaselineSurveyPromptTemplateTests {
    [Fact]
    public void GetSurveyPrompt_ContainsFourQuestionsMarkers() {
        var prompt = BaselineSurveyPromptTemplate.GetSurveyPrompt(
            domain: "键序列解析",
            problem: "CSI 泛化边界扫描",
            csharpPath: "native_tui/src/Grep.cs",
            baselinePath: "src/useTextInput.ts",
            originalSpec: "ECMA-48 §5.4",
            returnLineLimit: 200);

        prompt.Should().Contain("专家隐性实践");
        prompt.Should().Contain("原始素材清单");
        prompt.Should().Contain("具体方法与工具");
        prompt.Should().Contain("独立验证来源");
    }

    [Fact]
    public void GetSurveyPrompt_InjectsParameters() {
        var prompt = BaselineSurveyPromptTemplate.GetSurveyPrompt(
            domain: "渲染管线",
            problem: "宽字符折行",
            csharpPath: "native_tui/src/RuneWidth.cs",
            baselinePath: "src/ink.tsx",
            originalSpec: "Unicode UAX #11",
            returnLineLimit: 150);

        prompt.Should().Contain("渲染管线");
        prompt.Should().Contain("宽字符折行");
        prompt.Should().Contain("native_tui/src/RuneWidth.cs");
        prompt.Should().Contain("src/ink.tsx");
        prompt.Should().Contain("Unicode UAX #11");
        prompt.Should().Contain("150");
    }

    [Fact]
    public void GetSurveyPrompt_ContainsDepthOverBreadthManifesto() {
        var prompt = BaselineSurveyPromptTemplate.GetSurveyPrompt(
            domain: "x", problem: "y", csharpPath: "a", baselinePath: "b", originalSpec: "s", returnLineLimit: 100);

        prompt.Should().Contain("禁止只看");
        prompt.Should().Contain("已整理好的素材");
        prompt.Should().Contain("原始规范");
    }

    [Fact]
    public void GetSurveyPrompt_ContainsJudgmentDiscipline() {
        var prompt = BaselineSurveyPromptTemplate.GetSurveyPrompt(
            domain: "x", problem: "y", csharpPath: "a", baselinePath: "b", originalSpec: "s", returnLineLimit: 100);

        prompt.Should().Contain("自身注释契约违背才判 bug");
        prompt.Should().Contain("禁止把");
        prompt.Should().Contain("实现不同");
        prompt.Should().Contain("判为 bug");
    }

    [Fact]
    public void GetAllTemplates_ContainsBaselineSurveyRegistration() {
        var templates = BrainPromptTemplates.GetAllTemplates().ToList();

        templates.Should().Contain(t => t.Name == "baseline_survey");
        var survey = templates.Single(t => t.Name == "baseline_survey");
        survey.Category.Should().Be("System");
        survey.HasParameters.Should().BeTrue();
        survey.Description.Should().NotBeEmpty();
    }

    [Fact]
    public void GetContent_ReturnsNullForParameterizedBaselineSurvey() {
        var content = BrainPromptTemplates.GetContent("baseline_survey");

        content.Should().BeNull();
    }
}
