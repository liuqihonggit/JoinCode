namespace Gh.Tests;

/// <summary>
/// 简易 jq 解释器测试 — 覆盖 gh api --jq 最常用模式（缺陷1b）
/// </summary>
public sealed class SimpleJqEvaluatorTests {
    /// <summary>.field 取顶层字段</summary>
    [Fact]
    public void Evaluate_SingleField_ShouldExtractValue() {
        var json = """{"name":"test","id":42}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".name");
        result.Should().Be("\"test\"");
    }

    /// <summary>.field.sub 嵌套字段</summary>
    [Fact]
    public void Evaluate_NestedField_ShouldExtractNestedValue() {
        var json = """{"user":{"login":"alice","id":1}}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".user.login");
        result.Should().Be("\"alice\"");
    }

    /// <summary>.field[] 数组展开</summary>
    [Fact]
    public void Evaluate_ArrayExpand_ShouldReturnArray() {
        var json = """{"check_runs":[{"name":"a"},{"name":"b"}]}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".check_runs[]");
        result.Should().Be("""[{"name":"a"},{"name":"b"}]""");
    }

    /// <summary>.field[] | .sub 数组展开 + 取字段</summary>
    [Fact]
    public void Evaluate_ArrayExpandAndField_ShouldExtractFieldFromEach() {
        var json = """{"items":[{"name":"a"},{"name":"b"}]}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".items[] | .name");
        result.Should().Be("""["a","b"]""");
    }

    /// <summary>select(.field=="value") 过滤数组</summary>
    [Fact]
    public void Evaluate_SelectEqual_ShouldFilterArray() {
        var json = """[{"conclusion":"failure","name":"a"},{"conclusion":"success","name":"b"}]""";
        var result = SimpleJqEvaluator.Evaluate(json, """select(.conclusion=="failure")""");
        result.Should().Be("""[{"conclusion":"failure","name":"a"}]""");
    }

    /// <summary>select(.field=="value" or .field2=="value2") 多条件过滤</summary>
    [Fact]
    public void Evaluate_SelectOrCondition_ShouldFilterByMultipleConditions() {
        var json = """[{"state":"FAILURE"},{"state":"SUCCESS"},{"conclusion":"FAILURE"}]""";
        var result = SimpleJqEvaluator.Evaluate(json, """select(.state=="FAILURE" or .conclusion=="FAILURE")""");
        result.Should().Contain("\"state\":\"FAILURE\"");
        result.Should().Contain("\"conclusion\":\"FAILURE\"");
        result.Should().NotContain("\"state\":\"SUCCESS\"");
    }

    /// <summary>{key: .field} 对象构造</summary>
    [Fact]
    public void Evaluate_ObjectConstruct_ShouldBuildNewObject() {
        var json = """{"name":"a","id":1,"extra":"x"}""";
        var result = SimpleJqEvaluator.Evaluate(json, "{name: .name, id: .id}");
        result.Should().Be("""{"name":"a","id":1}""");
    }

    /// <summary>组合: .field[] | select(.sub=="value") | {key: .field2}</summary>
    [Fact]
    public void Evaluate_FullPipeline_ShouldFilterAndConstruct() {
        var json = """{"check_runs":[{"name":"build","conclusion":"failure","id":100,"html_url":"http://x"},{"name":"test","conclusion":"success","id":200,"html_url":"http://y"}]}""";
        var result = SimpleJqEvaluator.Evaluate(json, """.check_runs[] | select(.conclusion=="failure") | {name: .name, id: .id, url: .html_url}""");

        result.Should().NotBeNull();
        result.Should().Contain("\"name\":\"build\"");
        result.Should().Contain("\"id\":100");
        result.Should().Contain("\"url\":\"http://x\"");
        result.Should().NotContain("\"name\":\"test\"");
    }

    /// <summary>select(.field!="value") 不等于过滤</summary>
    [Fact]
    public void Evaluate_SelectNotEqual_ShouldFilterByNegation() {
        var json = """[{"conclusion":"success"},{"conclusion":"failure"},{"conclusion":null}]""";
        var result = SimpleJqEvaluator.Evaluate(json, """select(.conclusion!="success")""");
        result.Should().Contain("\"failure\"");
        result.Should().NotContain("\"success\"");
    }

    /// <summary>报告案例: .jobs[] 过滤非 success</summary>
    [Fact]
    public void Evaluate_JobsFilterNonSuccess_ShouldMatchReportScenario() {
        var json = """{"jobs":[{"name":"build","conclusion":"success","id":1},{"name":"test","conclusion":"failure","id":2}]}""";
        var result = SimpleJqEvaluator.Evaluate(json, """.jobs[] | select(.conclusion!="success") | {name: .name, id: .id, conclusion: .conclusion}""");

        result.Should().Contain("\"name\":\"test\"");
        result.Should().Contain("\"id\":2");
        result.Should().NotContain("\"name\":\"build\"");
    }

    /// <summary>不支持的阶段(如 + 拼接)应宽容跳过,保留前一阶段结果</summary>
    [Fact]
    public void Evaluate_UnsupportedStage_ShouldSkipAndKeepPreviousResult() {
        var json = """[{"name":"a","conclusion":"failure"},{"name":"b","conclusion":"success"}]""";
        var result = SimpleJqEvaluator.Evaluate(json, """select(.conclusion=="failure") | .name + " suffix" """);

        result.Should().Contain("\"name\":\"a\"");
        result.Should().NotContain("\"name\":\"b\"");
    }

    /// <summary>.field[N][M] 嵌套数组索引访问 — 修复前静默丢失第二索引</summary>
    [Fact]
    public void Evaluate_NestedArrayIndex_ShouldExtractNestedElement() {
        var json = """{"matrix":[[1,2,3],[4,5,6]]}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".matrix[0][1]");
        result.Should().Be("2");
    }

    /// <summary>length 函数 — 数组返回元素数</summary>
    [Fact]
    public void Evaluate_Length_OnArray_ShouldReturnCount() {
        var json = """{"check_runs":[{"name":"a"},{"name":"b"},{"name":"c"}]}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".check_runs | length");
        result.Should().Be("3");
    }

    /// <summary>length 函数 — 对象返回字段数</summary>
    [Fact]
    public void Evaluate_Length_OnObject_ShouldReturnFieldCount() {
        var json = """{"name":"a","id":1,"conclusion":"success"}""";
        var result = SimpleJqEvaluator.Evaluate(json, "length");
        result.Should().Be("3");
    }

    /// <summary>select(.field=="value" and .field2=="value2") — and 逻辑过滤</summary>
    [Fact]
    public void Evaluate_SelectAndCondition_ShouldFilterByBothConditions() {
        var json = """[{"conclusion":"failure","name":"a"},{"conclusion":"success","name":"b"},{"conclusion":"failure","name":"c"}]""";
        var result = SimpleJqEvaluator.Evaluate(json, """select(.conclusion=="failure" and .name=="c")""");
        result.Should().Contain("\"name\":\"c\"");
        result.Should().NotContain("\"name\":\"a\"");
        result.Should().NotContain("\"name\":\"b\"");
    }

    /// <summary>select with and + or 混合逻辑</summary>
    [Fact]
    public void Evaluate_SelectMixedAndOr_ShouldRespectPrecedence() {
        var json = """[{"a":"1","b":"1"},{"a":"1","b":"2"},{"a":"2","b":"1"}]""";
        // .a=="1" and .b=="1" or .a=="2" → 第1个和第3个匹配
        var result = SimpleJqEvaluator.Evaluate(json, """select(.a=="1" and .b=="1" or .a=="2")""");
        result.Should().Contain("\"a\":\"1\",\"b\":\"1\"");
        result.Should().Contain("\"a\":\"2\",\"b\":\"1\"");
        result.Should().NotContain("\"a\":\"1\",\"b\":\"2\"");
    }

    /// <summary>路径字段不存在时应返回 null 而非完整原始对象 — 修复前 continue 跳过导致返回完整对象</summary>
    [Fact]
    public void Evaluate_NonExistentField_ShouldReturnNull_NotOriginalObject() {
        var json = """{"name":"test","id":42}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".jobs");
        result.Should().Be("null");
    }

    /// <summary>嵌套路径中间字段不存在时应返回 null — 修复前返回完整原始对象</summary>
    [Fact]
    public void Evaluate_NestedNonExistentField_ShouldReturnNull_NotOriginalObject() {
        var json = """{"name":"test","id":42}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".jobs[0].conclusion");
        result.Should().Be("null");
    }

    /// <summary>管道中路径不存在应传播 null — 修复前返回前一阶段结果</summary>
    [Fact]
    public void Evaluate_PipelineNonExistentField_ShouldPropagateNull() {
        var json = """{"items":[{"name":"a"}]}""";
        var result = SimpleJqEvaluator.Evaluate(json, ".nonexistent | .sub");
        result.Should().Be("null");
    }
}
