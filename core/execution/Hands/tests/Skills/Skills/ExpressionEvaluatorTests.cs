
namespace Core.Tests.Skills;

/// <summary>
/// ExpressionEvaluator 单元测试类
/// 测试表达式求值功能，包括算术运算、属性访问和字符串方法
/// </summary>
public class ExpressionEvaluatorTests
{
    private readonly ExpressionEvaluator _evaluator;

    public ExpressionEvaluatorTests()
    {
        _evaluator = new ExpressionEvaluator();
    }

    private static JsonElement J(string value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement J(int value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement J(double value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement J(bool value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement J(Dictionary<string, JsonElement> value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement J(Dictionary<string, string> value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement J(string[] value) => JsonSerializer.SerializeToElement(value);

    private static Dictionary<string, JsonElement> Vars(params (string key, JsonElement value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in pairs)
        {
            dict[key] = value;
        }
        return dict;
    }

    #region 算术运算测试

    /// <summary>
    /// 测试加法运算应该正确求值
    /// </summary>
    [Theory]
    [InlineData("5 + 3", "8")]
    [InlineData("10 + 20", "30")]
    [InlineData("0 + 0", "0")]
    public void Evaluate_Addition_ShouldReturnCorrectResult(string expression, string expected)
    {
        var variables = new Dictionary<string, JsonElement>();

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be(expected);
    }

    /// <summary>
    /// 测试减法运算应该正确求值
    /// </summary>
    [Theory]
    [InlineData("10 - 3", "7")]
    [InlineData("5 - 10", "-5")]
    [InlineData("0 - 0", "0")]
    public void Evaluate_Subtraction_ShouldReturnCorrectResult(string expression, string expected)
    {
        var variables = new Dictionary<string, JsonElement>();

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be(expected);
    }

    /// <summary>
    /// 测试乘法运算应该正确求值
    /// </summary>
    [Theory]
    [InlineData("4 * 5", "20")]
    [InlineData("0 * 100", "0")]
    [InlineData("-3 * 4", "-12")]
    public void Evaluate_Multiplication_ShouldReturnCorrectResult(string expression, string expected)
    {
        var variables = new Dictionary<string, JsonElement>();

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be(expected);
    }

    /// <summary>
    /// 测试除法运算应该正确求值
    /// </summary>
    [Theory]
    [InlineData("20 / 4", "5")]
    [InlineData("10 / 3", "3.33")]
    [InlineData("0 / 5", "0")]
    public void Evaluate_Division_ShouldReturnCorrectResult(string expression, string expected)
    {
        var variables = new Dictionary<string, JsonElement>();

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().StartWith(expected);
    }

    /// <summary>
    /// 测试变量替换和简单算术表达式求值 - 分步计算
    /// </summary>
    [Fact]
    public void Evaluate_WithVariableArithmetic_ShouldSubstituteAndEvaluate()
    {
        var variables = Vars(("price", J("10")), ("quantity", J("5")), ("tax", J("2")));

        var step1 = _evaluator.Evaluate("price * quantity", variables);
        var tempVars = new Dictionary<string, JsonElement>(variables) { { "subtotal", J(step1) } };
        var result = _evaluator.Evaluate("subtotal + tax", tempVars);

        result.Should().Be("52");
    }

    #endregion

    #region 字符串方法测试

    /// <summary>
    /// 测试 toUpper 方法应该将字符串转为大写
    /// </summary>
    [Fact]
    public void Evaluate_ToUpper_ShouldReturnUpperCase()
    {
        var expression = "name.toUpper()";
        var variables = Vars(("name", J("alice")));

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("ALICE");
    }

    /// <summary>
    /// 测试 toLower 方法应该将字符串转为小写
    /// </summary>
    [Fact]
    public void Evaluate_ToLower_ShouldReturnLowerCase()
    {
        var expression = "name.toLower()";
        var variables = Vars(("name", J("BOB")));

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("bob");
    }

    /// <summary>
    /// 测试 trim 方法应该去除字符串两端空格
    /// </summary>
    [Fact]
    public void Evaluate_Trim_ShouldReturnTrimmedString()
    {
        var expression = "text.trim()";
        var variables = Vars(("text", J("  hello world  ")));

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("hello world");
    }

    /// <summary>
    /// 测试 length 属性应该返回字符串长度
    /// </summary>
    [Fact]
    public void Evaluate_Length_ShouldReturnStringLength()
    {
        var expression = "text.length";
        var variables = Vars(("text", J("hello")));

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("5");
    }

    /// <summary>
    /// 测试 substring 方法应该返回子字符串
    /// </summary>
    [Fact]
    public void Evaluate_Substring_ShouldReturnSubstring()
    {
        var expression = "text.substring(0, 5)";
        var variables = Vars(("text", J("hello world")));

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("hello");
    }

    /// <summary>
    /// 测试 replace 方法应该替换字符串
    /// </summary>
    [Fact]
    public void Evaluate_Replace_ShouldReplaceSubstring()
    {
        var expression = "text.replace('world', 'universe')";
        var variables = Vars(("text", J("hello world")));

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("hello universe");
    }

    /// <summary>
    /// 测试 contains 方法应该检查包含关系
    /// </summary>
    [Fact]
    public void Evaluate_Contains_ShouldReturnBoolean()
    {
        var expression = "text.contains('world')";
        var variables = Vars(("text", J("hello world")));

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("True");
    }

    #endregion

    #region 属性访问测试

    /// <summary>
    /// 测试简单属性访问应该返回值 - 使用字典对象
    /// </summary>
    [Fact]
    public void Evaluate_PropertyAccess_ShouldReturnValue()
    {
        var expression = "user.name";
        var variables = new Dictionary<string, JsonElement>
        {
            { "user", J(new Dictionary<string, string> { { "name", "Charlie" } }) }
        };

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("Charlie");
    }

    /// <summary>
    /// 测试嵌套属性访问应该返回值 - 使用字典存储嵌套值
    /// </summary>
    [Fact]
    public void Evaluate_NestedPropertyAccess_ShouldReturnValue()
    {
        var expression = "user.address.city";
        var variables = new Dictionary<string, JsonElement>
        {
            {
                "user", J(new Dictionary<string, JsonElement>
                {
                    { "address", J(new Dictionary<string, JsonElement> { { "city", J("New York") } }) }
                })
            }
        };

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("New York");
    }

    /// <summary>
    /// 测试数组长度属性应该返回长度
    /// </summary>
    [Fact]
    public void Evaluate_ArrayLength_ShouldReturnLength()
    {
        var expression = "items.length";
        var variables = new Dictionary<string, JsonElement>
        {
            { "items", J(new[] { "a", "b", "c", "d", "e" }) }
        };

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("5");
    }

    #endregion

    #region 复杂表达式测试

    /// <summary>
    /// 测试链式方法调用 - 分步验证
    /// </summary>
    [Fact]
    public void Evaluate_ChainedMethods_ShouldExecuteInOrder()
    {
        var variables = Vars(("text", J("  hello  ")));

        var trimmedResult = _evaluator.Evaluate("text.trim()", variables);
        var tempVars = new Dictionary<string, JsonElement> { { "trimmed", J(trimmedResult) } };
        var result = _evaluator.Evaluate("trimmed.toUpper()", tempVars);

        result.Should().Be("HELLO");
    }

    /// <summary>
    /// 测试复杂算术表达式应该正确求值 - 分步计算
    /// </summary>
    [Fact]
    public void Evaluate_ComplexArithmetic_ShouldEvaluateCorrectly()
    {
        var variables = Vars(("a", J("2")), ("b", J("3")), ("c", J("4")), ("d", J("10")));

        var step1 = _evaluator.Evaluate("a + b", variables);
        var step2 = _evaluator.Evaluate("d / 2", variables);

        var tempVars = new Dictionary<string, JsonElement>(variables)
        {
            { "sum", J(step1) },
            { "half", J(step2) }
        };

        var step3 = _evaluator.Evaluate("sum * c", tempVars);
        var tempVars2 = new Dictionary<string, JsonElement>(tempVars)
        {
            { "product", J(step3) }
        };

        var result = _evaluator.Evaluate("product - half", tempVars2);

        result.Should().Be("15");
    }

    #endregion

    #region 边界情况测试

    /// <summary>
    /// 测试空表达式应该返回空字符串
    /// </summary>
    [Fact]
    public void Evaluate_EmptyExpression_ShouldReturnEmpty()
    {
        var expression = "";
        var variables = new Dictionary<string, JsonElement>();

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().BeEmpty();
    }

    /// <summary>
    /// 测试纯文本表达式应该返回原值
    /// </summary>
    [Fact]
    public void Evaluate_PlainText_ShouldReturnText()
    {
        var expression = "hello world";
        var variables = new Dictionary<string, JsonElement>();

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("hello world");
    }

    /// <summary>
    /// 测试未定义变量应该返回原始表达式
    /// </summary>
    [Fact]
    public void Evaluate_UndefinedVariable_ShouldReturnOriginal()
    {
        var expression = "undefinedVar";
        var variables = new Dictionary<string, JsonElement>();

        var result = _evaluator.Evaluate(expression, variables);

        result.Should().Be("undefinedVar");
    }

    #endregion
}
