namespace Host.Tests.Cli;

/// <summary>
/// slash_call 参数解析测试 — 验证 BuildArgsJsonFromKeyValue 把 key=value 组装成 JSON 字符串。
/// <para>对接 ADR 0069: slash_call 适配 mcp_call 的多种参数传递格式(key=value/JSON/--args-file/--args-stdin)。</para>
/// <para>关联任务: docs/tasks/覆盖全部功能总测试任务清单.md 前置1。</para>
/// </summary>
public sealed class SlashCallArgsParserTests
{
    [Fact]
    public void SingleString_ShouldReturnJsonObject()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "name=test" });
        result.Should().Be("""{"name":"test"}""");
    }

    [Fact]
    public void Integer_ShouldInferNumber()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "level=2" });
        result.Should().Be("""{"level":2}""");
    }

    [Fact]
    public void Double_ShouldInferNumber()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "rate=2.5" });
        result.Should().Be("""{"rate":2.5}""");
    }

    [Fact]
    public void Boolean_ShouldInferBool()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "flag=true" });
        result.Should().Be("""{"flag":true}""");
    }

    [Fact]
    public void NullValue_ShouldInferNull()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "v=null" });
        result.Should().Be("""{"v":null}""");
    }

    [Fact]
    public void JsonArray_ShouldParseAsArray()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "arr=[1,2,3]" });
        result.Should().Be("""{"arr":[1,2,3]}""");
    }

    [Fact]
    public void JsonObject_ShouldParseAsObject()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { """obj={"k":"v"}""" });
        result.Should().Be("""{"obj":{"k":"v"}}""");
    }

    [Fact]
    public void MultipleKeyValue_ShouldReturnCombinedJson()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "level=2", "name=test" });
        result.Should().Be("""{"level":2,"name":"test"}""");
    }

    [Fact]
    public void NoEqualsSign_ShouldReturnNull()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "noequals" });
        result.Should().BeNull();
    }

    [Fact]
    public void EmptyValue_ShouldReturnNull()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(new[] { "key=" });
        result.Should().BeNull();
    }

    [Fact]
    public void EmptyArray_ShouldReturnEmptyObject()
    {
        var result = SlashCallExecutor.BuildArgsJsonFromKeyValue(Array.Empty<string>());
        result.Should().Be("{}");
    }
}
