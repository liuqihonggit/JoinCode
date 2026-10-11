namespace IO.FileSystem;

/// <summary>
/// 文件编辑 Actor 命令 — EditFileAsync 的 Actor 化封装 — TASK001
/// <para>消除 per-path AsyncLock（EditLockRegistry），改用 Actor 邮箱管道串行化编辑操作。</para>
/// <para>泛型返回值通过 object 装箱传递，调用方拆箱还原为 T。</para>
/// </summary>
public sealed record EditFileCmd(
    string Path,
    Func<byte[], CancellationToken, Task<(byte[]? NewContent, object? Result)>> Transform,
    TaskCompletionSource<object?> Reply);