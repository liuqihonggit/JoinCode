namespace Core.Tests.Plugins;

public sealed class CallableServiceTests
{
    [Fact]
    public void Call_WithArg_InvokesFunc()
    {
        var service = new CallableService<string, int>(s => s.Length);
        Assert.Equal(5, service.Call("hello"));
    }

    [Fact]
    public void Call_NoArg_InvokesFunc()
    {
        var service = new CallableService<int>(() => 42);
        Assert.Equal(42, service.Call());
    }

    [Fact]
    public void Call_PassesArgCorrectly()
    {
        var service = new CallableService<int, int>(x => x * 2);
        Assert.Equal(10, service.Call(5));
    }

    [Fact]
    public void Constructor_NullFunc_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CallableService<string, int>(null!));
    }

    [Fact]
    public void Constructor_NullFuncNoArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CallableService<int>(null!));
    }

    [Fact]
    public void ServiceInvokeAttribute_CanBeAppliedToMethod()
    {
        var method = typeof(TestServiceWithInvoke).GetMethod(nameof(TestServiceWithInvoke.DoWork));
        Assert.NotNull(method);
        var attr = method!.IsDefined(typeof(ServiceInvokeAttribute), false);
        Assert.True(attr);
    }

    private sealed class TestServiceWithInvoke
    {
        [ServiceInvoke]
        public string DoWork(int input) => input.ToString();
    }
}
