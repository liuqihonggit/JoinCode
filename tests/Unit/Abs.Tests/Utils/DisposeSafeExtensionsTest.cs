namespace Abs.Tests.Utils;

/// <summary>
/// DisposeSafeExtensions 单元测试 — 验证安全释放扩展的幂等性、异常吞咽与日志记录行为
/// </summary>
public sealed class DisposeSafeExtensionsTest
{
    // === DisposeSafe ===

    [Fact]
    public void DisposeSafe_Null_DoesNothing()
    {
        ((IDisposable?)null).DisposeSafe();
    }

    [Fact]
    public void DisposeSafe_NormalObject_Disposes()
    {
        var obj = new TrackableDisposable();

        obj.DisposeSafe();

        obj.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void DisposeSafe_AlreadyDisposed_SwallowsObjectDisposedException()
    {
        var obj = new TrackableDisposable();

        obj.DisposeSafe();
        var act = () => obj.DisposeSafe();

        act.Should().NotThrow();
        obj.DisposeCallCount.Should().Be(2);
    }

    [Fact]
    public void DisposeSafe_NonObjectDisposedException_LogsWarning()
    {
        var obj = new ThrowingDisposable();
        var logger = new CaptureLogger();

        var act = () => obj.DisposeSafe(logger);

        act.Should().NotThrow();
        logger.WarningCount.Should().Be(1);
        logger.LastException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void DisposeSafe_NonObjectDisposedException_NoLogger_DoesNotThrow()
    {
        var obj = new ThrowingDisposable();

        var act = () => obj.DisposeSafe();

        act.Should().NotThrow();
    }

    // === CancelAndDisposeSafe ===

    [Fact]
    public void CancelAndDisposeSafe_Null_DoesNothing()
    {
        ((CancellationTokenSource?)null).CancelAndDisposeSafe();
    }

    [Fact]
    public void CancelAndDisposeSafe_Normal_CancelsAndDisposes()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        cts.CancelAndDisposeSafe();

        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void CancelAndDisposeSafe_AlreadyDisposed_Swallows()
    {
        var cts = new CancellationTokenSource();
        cts.Dispose();

        var act = () => cts.CancelAndDisposeSafe();

        act.Should().NotThrow();
    }

    // === DisposeSafeAsync ===

    [Fact]
    public async Task DisposeSafeAsync_Null_DoesNothing()
    {
        await ((IAsyncDisposable?)null).DisposeSafeAsync();
    }

    [Fact]
    public async Task DisposeSafeAsync_Normal_DisposesAsync()
    {
        var obj = new TrackableAsyncDisposable();

        await obj.DisposeSafeAsync();

        obj.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeSafeAsync_AlreadyDisposed_Swallows()
    {
        var obj = new TrackableAsyncDisposable();
        await obj.DisposeAsync();

        var act = async () => await obj.DisposeSafeAsync();

        await act.Should().NotThrowAsync();
    }

    // === 测试桩 ===

    private sealed class TrackableDisposable : IDisposable
    {
        public int DisposeCallCount;
        public bool IsDisposed;
        public void Dispose()
        {
            DisposeCallCount++;
            IsDisposed = true;
        }
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("boom");
    }

    private sealed class TrackableAsyncDisposable : IAsyncDisposable
    {
        public bool IsDisposed;
        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public int WarningCount;
        public Exception? LastException;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
                LastException = exception;
            }
        }
    }
}
