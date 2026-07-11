using JoinCode.Abstractions.Interfaces;
using Infrastructure.IO.Services.FileOps;

namespace Infrastructure.Tests.Services;

public sealed class FileReadListenerRegistryTests
{
    [Fact]
    public void Register_And_Notify_CallsListener()
    {
        var registry = new FileReadListenerRegistry();
        var listener = new TestListener();
        using var token = registry.Register(listener);

        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };
        registry.Notify(args);

        listener.ReceivedCalls.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(args);
    }

    [Fact]
    public void Notify_MultipleListeners_AllCalled()
    {
        var registry = new FileReadListenerRegistry();
        var listener1 = new TestListener();
        var listener2 = new TestListener();
        using var token1 = registry.Register(listener1);
        using var token2 = registry.Register(listener2);

        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };
        registry.Notify(args);

        listener1.ReceivedCalls.Should().ContainSingle();
        listener2.ReceivedCalls.Should().ContainSingle();
    }

    [Fact]
    public void Unsubscribe_NoLongerNotified()
    {
        var registry = new FileReadListenerRegistry();
        var listener = new TestListener();
        var token = registry.Register(listener);

        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };
        registry.Notify(args);
        listener.ReceivedCalls.Should().ContainSingle();

        token.Dispose();

        registry.Notify(args);
        listener.ReceivedCalls.Should().ContainSingle(); // 仍然是1，不再收到通知
    }

    [Fact]
    public void Unsubscribe_InCallback_DoesNotSkipOtherListeners()
    {
        // 对齐 TS: fileReadListeners.slice() — 快照遍历，回调中取消订阅不影响后续监听器
        var registry = new FileReadListenerRegistry();
        var listener2 = new TestListener();
        var listener1 = new SelfUnsubscribingListener();

        var token1 = registry.Register(listener1);
        listener1.SetToken(token1); // 注册后设置 token，以便回调中取消
        using var token2 = registry.Register(listener2);

        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };
        registry.Notify(args);

        listener1.CallCount.Should().Be(1);
        listener2.ReceivedCalls.Should().ContainSingle(); // listener2 不应被跳过
    }

    [Fact]
    public void Notify_ListenerThrows_OtherListenersStillCalled()
    {
        // 对齐 TS: 监听器异常不影响其他监听器
        var registry = new FileReadListenerRegistry();
        var throwingListener = new ThrowingListener();
        var normalListener = new TestListener();
        using var token1 = registry.Register(throwingListener);
        using var token2 = registry.Register(normalListener);

        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };
        registry.Notify(args);

        normalListener.ReceivedCalls.Should().ContainSingle(); // 异常监听器不影响后续
    }

    [Fact]
    public void Notify_NoListeners_DoesNotThrow()
    {
        var registry = new FileReadListenerRegistry();
        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };

        var act = () => registry.Notify(args);
        act.Should().NotThrow();
    }

    [Fact]
    public void Register_NullListener_ThrowsArgumentNullException()
    {
        var registry = new FileReadListenerRegistry();
        var act = () => registry.Register(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Notify_NullArgs_ThrowsArgumentNullException()
    {
        var registry = new FileReadListenerRegistry();
        var act = () => registry.Notify(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Dispose_TokenTwice_OnlyUnsubscribesOnce()
    {
        var registry = new FileReadListenerRegistry();
        var listener = new TestListener();
        var token = registry.Register(listener);

        token.Dispose();
        token.Dispose(); // 第二次 Dispose 应该是空操作

        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };
        registry.Notify(args);
        listener.ReceivedCalls.Should().BeEmpty();
    }

    [Fact]
    public void Register_SameListenerTwice_ReceivesNotificationsTwice()
    {
        var registry = new FileReadListenerRegistry();
        var listener = new TestListener();
        using var token1 = registry.Register(listener);
        using var token2 = registry.Register(listener);

        var args = new FileReadEventArgs { FilePath = "/test/file.txt", Content = "hello" };
        registry.Notify(args);

        listener.ReceivedCalls.Should().HaveCount(2); // 同一监听器注册两次，收到两次通知
    }

    /// <summary>
    /// 测试用监听器，记录所有收到的通知。
    /// </summary>
    private sealed class TestListener : IFileReadListener
    {
        public List<FileReadEventArgs> ReceivedCalls { get; } = [];

        public void OnFileRead(FileReadEventArgs e) => ReceivedCalls.Add(e);
    }

    /// <summary>
    /// 自取消订阅监听器，在回调中取消自身订阅。
    /// 用于验证快照遍历机制。
    /// </summary>
    private sealed class SelfUnsubscribingListener : IFileReadListener
    {
        public int CallCount { get; private set; }
        private IDisposable? _token;

        public void SetToken(IDisposable token) => _token = token;

        public void OnFileRead(FileReadEventArgs e)
        {
            CallCount++;
            _token?.Dispose();
        }
    }

    /// <summary>
    /// 抛出异常的监听器，用于验证异常隔离。
    /// </summary>
    private sealed class ThrowingListener : IFileReadListener
    {
        public void OnFileRead(FileReadEventArgs e) => throw new InvalidOperationException("测试异常");
    }
}
