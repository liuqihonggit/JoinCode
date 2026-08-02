
namespace Core.Tests.State;

/// <summary>
/// StoreSelector&lt;TState, TSelected&gt; 单元测试 — 验证派生状态订阅、去重和释放
/// </summary>
public sealed class StoreSelectorTests : IDisposable
{
    private readonly Store<int> _store;

    public StoreSelectorTests()
    {
        _store = new Store<int>(0);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public void Constructor_InitializesCurrentValue()
    {
        var selector = new StoreSelector<int, int>(_store, x => x * 2);

        selector.CurrentValue.Should().Be(0);
        selector.Dispose();
    }

    [Fact]
    public void CurrentValue_AfterStateChange_UpdatesValue()
    {
        var selector = new StoreSelector<int, int>(_store, x => x * 2);

        _store.SetState(x => x + 3);

        selector.CurrentValue.Should().Be(6);
        selector.Dispose();
    }

    [Fact]
    public void Subscribe_ImmediatelyEmitsCurrentValue()
    {
        var selector = new StoreSelector<int, int>(_store, x => x + 10);
        var received = new List<int>();

        using var subscription = selector.Subscribe(received.Add);

        received.Should().ContainSingle().Which.Should().Be(10);
    }

    [Fact]
    public void Subscribe_StateChange_EmitsNewValue()
    {
        var selector = new StoreSelector<int, int>(_store, x => x + 10);
        var received = new List<int>();

        using var subscription = selector.Subscribe(received.Add);
        _store.SetState(x => x + 5);

        received.Should().HaveCount(2);
        received[1].Should().Be(15);
    }

    [Fact]
    public void Subscribe_DuplicateValue_DoesNotEmit()
    {
        var selector = new StoreSelector<int, int>(_store, x => x % 2);
        var received = new List<int>();

        using var subscription = selector.Subscribe(received.Add);
        _store.SetState(x => x + 2);

        received.Should().ContainSingle();
    }

    [Fact]
    public void Subscribe_MultipleSubscribers_AllReceiveUpdates()
    {
        var selector = new StoreSelector<int, int>(_store, x => x * 2);
        var received1 = new List<int>();
        var received2 = new List<int>();

        using var sub1 = selector.Subscribe(received1.Add);
        using var sub2 = selector.Subscribe(received2.Add);

        _store.SetState(x => x + 1);

        received1.Should().HaveCount(2);
        received2.Should().HaveCount(2);
        received1[1].Should().Be(2);
        received2[1].Should().Be(2);
    }

    [Fact]
    public void Subscribe_AfterDispose_ThrowsObjectDisposedException()
    {
        var selector = new StoreSelector<int, int>(_store, x => x * 2);
        selector.Dispose();

        Action act = () => selector.Subscribe(_ => { });

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_SubscriptionDoesNotReceiveFurtherUpdates()
    {
        var selector = new StoreSelector<int, int>(_store, x => x * 2);
        var received = new List<int>();

        var subscription = selector.Subscribe(received.Add);
        selector.Dispose();

        _store.SetState(x => x + 1);

        received.Should().ContainSingle();
    }

    [Fact]
    public void SubscriptionDispose_StopsReceivingUpdates()
    {
        var selector = new StoreSelector<int, int>(_store, x => x * 2);
        var received = new List<int>();

        var subscription = selector.Subscribe(received.Add);
        subscription.Dispose();

        _store.SetState(x => x + 1);

        received.Should().ContainSingle();
        selector.Dispose();
    }

    [Fact]
    public void Constructor_NullStore_ThrowsArgumentNullException()
    {
        Action act = () => new StoreSelector<int, int>(null!, x => x);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("store");
    }

    [Fact]
    public void Constructor_NullSelector_ThrowsArgumentNullException()
    {
        Action act = () => new StoreSelector<int, int>(_store, null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("selector");
    }

    [Fact]
    public void Subscribe_HandlerThrows_DoesNotBreakOtherSubscribers()
    {
        var selector = new StoreSelector<int, int>(_store, x => x * 2);
        var received = new List<int>();

        using var sub1 = selector.Subscribe(received.Add);
        using var sub2 = selector.Subscribe(value =>
        {
            if (value != 0)
            {
                throw new InvalidOperationException("boom");
            }
        });

        _store.SetState(x => x + 1);

        received.Should().HaveCount(2);
    }

    [Fact]
    public void SelectorProperty_ReturnsSelectorFunction()
    {
        Func<int, int> selectorFunc = x => x + 1;
        var selector = new StoreSelector<int, int>(_store, selectorFunc);

        selector.Selector.Should().BeSameAs(selectorFunc);
        selector.Dispose();
    }

    [Fact]
    public void CurrentValue_WithCustomComparer_UsesComparer()
    {
        var selector = new StoreSelector<int, string>(_store, x => x.ToString(), StringComparer.OrdinalIgnoreCase);

        _store.SetState(x => x + 1);

        selector.CurrentValue.Should().Be("1");
        selector.Dispose();
    }

    [Fact]
    public void Subscribe_DifferentReferenceButEqualByComparer_DoesNotEmit()
    {
        var selector = new StoreSelector<int, string>(_store, x => (x % 2).ToString(), StringComparer.OrdinalIgnoreCase);
        var received = new List<string>();

        using var subscription = selector.Subscribe(received.Add);
        _store.SetState(x => x + 2);

        received.Should().ContainSingle();
    }
}
