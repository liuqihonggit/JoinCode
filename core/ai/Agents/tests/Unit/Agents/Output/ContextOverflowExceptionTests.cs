namespace Core.Agents;


public sealed class ContextOverflowExceptionTests
{
    [Fact]
    public void Constructor_SetsTokenFields()
    {
        var ex = new ContextOverflowException("上下文溢出", 128_000, 130_000);

        ex.ContextMaxTokens.Should().Be(128_000);
        ex.CurrentTokens.Should().Be(130_000);
    }

    [Fact]
    public void ErrorCode_IsCtx001()
    {
        var ex = new ContextOverflowException("溢出", 100, 200);

        ex.ErrorCode.Should().Be("CTX001");
    }

    [Fact]
    public void Category_IsResource()
    {
        var ex = new ContextOverflowException("溢出", 100, 200);

        ex.Category.Should().Be(ErrorCategory.Resource);
    }

    [Fact]
    public void IsRetryable_IsFalse()
    {
        var ex = new ContextOverflowException("溢出", 100, 200);

        ex.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void ConstructorWithInner_PreservesInner()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new ContextOverflowException("溢出", inner, 100, 200);

        ex.InnerException.Should().BeSameAs(inner);
        ex.ContextMaxTokens.Should().Be(100);
        ex.CurrentTokens.Should().Be(200);
    }
}
