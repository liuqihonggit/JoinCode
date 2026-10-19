namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH <c>directoryPicker/pick</c> 的宿主实现（ADR 0143）：把前端请求转发给 UI 线程上的原生目录选择对话框。
/// <para>注册方式：窗口创建时 <see cref="Register"/> 一个处理器（Avalonia StorageProvider）。</para>
/// <para>E2E 注入：环境变量 <c>JCC_DIRECTORY_PICK</c> 非空时直接返回该路径，跳过对话框（自动化测试用）。</para>
/// <para>两者都不可用时返回 null（等价用户取消），不阻塞前端。</para>
/// </summary>
internal static class DshDirectoryPicker {
    private static readonly Lock Gate = new();
    private static Func<CancellationToken, Task<string?>>? _handler;

    /// <summary>注册宿主处理器（窗口创建时调用；传 null 表示注销）</summary>
    internal static void Register(Func<CancellationToken, Task<string?>>? handler) {
        lock (Gate) {
            _handler = handler;
        }
    }

    /// <summary>当前是否具备处理器（未注册时前端会得到 null 结果，不影响其它流程）</summary>
    internal static bool IsAvailable {
        get {
            lock (Gate) {
                return _handler is not null;
            }
        }
    }

    /// <summary>请求选择目录：返回绝对路径，未选择/未注册返回 null</summary>
    internal static async Task<string?> PickAsync(CancellationToken cancellationToken) {
        if (Environment.GetEnvironmentVariable("JCC_DIRECTORY_PICK") is { Length: > 0 } injected) {
            App.LogDiag($"[DSH-WEB] directory pick injected={injected}");
            return injected;
        }
        if (ResolveHandler() is not { } handler) {
            App.LogDiag("[DSH-WEB] directory pick cancelled: no handler registered");
            return null;
        }
        var picked = await handler(cancellationToken);
        App.LogDiag($"[DSH-WEB] directory pick result={(picked is null ? "<cancelled>" : picked)}");
        return picked;
    }

    /// <summary>取当前处理器快照（锁只在同步方法内短持，避免在 async 方法里加锁）</summary>
    private static Func<CancellationToken, Task<string?>>? ResolveHandler() {
        lock (Gate) {
            return _handler;
        }
    }
}
