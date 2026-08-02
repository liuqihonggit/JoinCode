
namespace Core.Tests.Skills;

/// <summary>
/// VariableResolver 单元测试类
/// 测试变量解析功能，包括简单变量、嵌套变量、默认值和表达式
/// </summary>
public class VariableResolverTests
{
    private readonly VariableResolver _resolver;

    public VariableResolverTests()
    {
        _resolver = new VariableResolver();
    }

    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);

    private static Dictionary<string, JsonElement> Vars(params (string key, object value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in pairs)
        {
            dict[key] = J(value);
        }
        return dict;
    }

    #region 简单变量替换测试

    /// <summary>
    /// 测试简单变量应该被正确替换
    /// </summary>
    [Fact]
    public void Resolve_WithSimpleVariable_ShouldReplace()
    {
        var input = "Hello, {{name}}!";
        var variables = Vars(("{{name}}", "Alice"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Hello, Alice!");
    }

    /// <summary>
    /// 测试多个变量应该被正确替换
    /// </summary>
    [Fact]
    public void Resolve_WithMultipleVariables_ShouldReplaceAll()
    {
        var input = "{{greeting}}, {{name}}! You are {{age}} years old.";
        var variables = Vars(("{{greeting}}", "Hello"), ("{{name}}", "Bob"), ("{{age}}", "25"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Hello, Bob! You are 25 years old.");
    }

    /// <summary>
    /// 测试无变量字符串应该保持不变
    /// </summary>
    [Fact]
    public void Resolve_WithNoVariables_ShouldReturnOriginal()
    {
        var input = "Hello, World!";
        var variables = Vars(("{{name}}", "Alice"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Hello, World!");
    }

    #endregion

    #region 嵌套变量测试

    /// <summary>
    /// 测试嵌套变量应该被正确替换
    /// </summary>
    [Fact]
    public void Resolve_WithNestedVariable_ShouldReplace()
    {
        var input = "User: {{user.{{field}}}}";
        var variables = Vars(("field", "name"), ("user.name", "Charlie"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be(input);
    }

    /// <summary>
    /// 测试属性访问语法应该被正确解析
    /// </summary>
    [Fact]
    public void Resolve_WithPropertyAccess_ShouldReplace()
    {
        var input = "Name: {{user.name}}, Age: {{user.age}}";
        var variables = new Dictionary<string, JsonElement>
        {
            { "user", J(new Dictionary<string, string> { { "name", "David" }, { "age", "30" } }) }
        };

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Name: David, Age: 30");
    }

    #endregion

    #region 默认值测试

    /// <summary>
    /// 测试带默认值的变量应该使用默认值当变量不存在时
    /// </summary>
    [Fact]
    public void Resolve_WithDefaultValue_ShouldUseDefaultWhenMissing()
    {
        var input = "Hello, {{name:Guest}}!";
        var variables = new Dictionary<string, JsonElement>();

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Hello, Guest!");
    }

    /// <summary>
    /// 测试带默认值的变量应该使用实际值当变量存在时
    /// </summary>
    [Fact]
    public void Resolve_WithDefaultValue_ShouldUseActualValueWhenPresent()
    {
        var input = "Hello, {{name:Guest}}!";
        var variables = Vars(("{{name}}", "Eve"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Hello, Eve!");
    }

    #endregion

    #region 表达式测试

    /// <summary>
    /// 测试简单算术表达式应该被正确求值
    /// </summary>
    [Fact]
    public void Resolve_WithArithmeticExpression_ShouldEvaluate()
    {
        var input = "Result: {{5 + 3}}";
        var variables = new Dictionary<string, JsonElement>();

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Result: 8");
    }

    /// <summary>
    /// 测试包含变量的算术表达式应该被正确求值
    /// </summary>
    [Fact]
    public void Resolve_WithVariableArithmetic_ShouldEvaluate()
    {
        var input = "Total: {{price * quantity}}";
        var variables = Vars(("{{price}}", "10"), ("{{quantity}}", "5"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Total: 50");
    }

    /// <summary>
    /// 测试字符串方法应该被正确调用
    /// </summary>
    [Fact]
    public void Resolve_WithStringMethod_ShouldCallMethod()
    {
        var input = "Upper: {{name.toUpper()}}";
        var variables = Vars(("{{name}}", "alice"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Upper: ALICE");
    }

    /// <summary>
    /// 测试多个字符串方法应该被正确调用
    /// </summary>
    [Fact]
    public void Resolve_WithMultipleStringMethods_ShouldCallAll()
    {
        var input = "Trimmed: {{text.trim()}}, Upper: {{text.toUpper()}}";
        var variables = Vars(("{{text}}", "  hello  "));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Trimmed: hello, Upper:   HELLO  ");
    }

    #endregion

    #region 变量验证测试

    /// <summary>
    /// 测试验证应该返回所有缺失的变量
    /// </summary>
    [Fact]
    public void Validate_WithMissingVariables_ShouldReturnMissingList()
    {
        var input = "{{var1}}, {{var2}}, {{var3}}";
        var variables = Vars(("var1", "value1"));

        var result = _resolver.Validate(input, variables);

        result.IsValid.Should().BeFalse();
        result.MissingVariables.Should().Contain("var2");
        result.MissingVariables.Should().Contain("var3");
    }

    /// <summary>
    /// 测试验证应该通过当所有变量都存在
    /// </summary>
    [Fact]
    public void Validate_WithAllVariablesPresent_ShouldPass()
    {
        var input = "{{var1}}, {{var2}}";
        var variables = Vars(("{{var1}}", "value1"), ("{{var2}}", "value2"));

        var result = _resolver.Validate(input, variables);

        result.IsValid.Should().BeTrue();
        result.MissingVariables.Should().BeEmpty();
    }

    /// <summary>
    /// 测试验证应该忽略带默认值的变量
    /// </summary>
    [Fact]
    public void Validate_WithDefaultValues_ShouldIgnoreDefaults()
    {
        var input = "{{var1}}, {{var2:default}}";
        var variables = Vars(("{{var1}}", "value1"));

        var result = _resolver.Validate(input, variables);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region 边界情况测试

    /// <summary>
    /// 测试空字符串应该返回空字符串
    /// </summary>
    [Fact]
    public void Resolve_WithEmptyString_ShouldReturnEmpty()
    {
        var input = "";
        var variables = Vars(("{{name}}", "Alice"));

        var result = _resolver.Resolve(input, variables);

        result.Should().BeEmpty();
    }

    /// <summary>
    /// 测试只有变量的字符串应该被替换
    /// </summary>
    [Fact]
    public void Resolve_WithOnlyVariable_ShouldReplace()
    {
        var input = "{{name}}";
        var variables = Vars(("{{name}}", "Alice"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Alice");
    }

    /// <summary>
    /// 测试重复变量应该都被替换
    /// </summary>
    [Fact]
    public void Resolve_WithRepeatedVariable_ShouldReplaceAll()
    {
        var input = "{{name}} and {{name}}";
        var variables = Vars(("{{name}}", "Bob"));

        var result = _resolver.Resolve(input, variables);

        result.Should().Be("Bob and Bob");
    }

    #endregion
}
