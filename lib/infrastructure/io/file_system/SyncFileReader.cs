namespace IO.FileSystem;

/// <summary>
/// 同步文件读取适配器 — 局限 .GetAwaiter().GetResult() 到此 class，防止异步阻塞无限扩展。
/// 仅供属性 getter / 构造函数等无法 async 的调用方使用。
/// </summary>
public static class SyncFileReader {
    /// <summary>同步读取文件全部文本（阻塞调用，仅用于无法 async 的上下文）</summary>
    public static string ReadAllText(IFileSystem fs, string path) {
        return fs.ReadAllText(path).GetAwaiter().GetResult();
    }

    /// <summary>同步读取文件全部字节（阻塞调用，仅用于无法 async 的上下文）</summary>
    public static byte[] ReadAllBytes(IFileSystem fs, string path) {
        return fs.ReadAllBytes(path).GetAwaiter().GetResult();
    }

    /// <summary>同步执行返回字符串的异步操作（阻塞调用，仅用于无法 async 的上下文）</summary>
    public static string RunString(Func<Task<string>> func) {
        return func().GetAwaiter().GetResult();
    }

    /// <summary>同步执行返回可空字符串的异步操作（阻塞调用，仅用于无法 async 的上下文）</summary>
    public static string? RunStringNullable(Func<Task<string?>> func) {
        return func().GetAwaiter().GetResult();
    }

    /// <summary>同步等待 ValueTask&lt;string&gt; 完成（阻塞调用，仅用于无法 async 的上下文）</summary>
    public static string RunValueTask(ValueTask<string> vt) {
        return vt.GetAwaiter().GetResult();
    }
}
