namespace Testing.Common;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class TestConfigurationJsonContext : JsonSerializerContext;

public static class TestConfiguration
{
    private static bool _isFastTestMode;
    private static int _timeAccelerationFactor = 10;
    private static string? _cachedApiKey;
    private static Lazy<IFileSystem> _fileSystem = new(() => new IO.FileSystem.PhysicalFileSystem());

    /// <summary>
    /// 测试文件系统 — 默认 PhysicalFileSystem，测试中可替换为 InMemoryFileSystem 实现零磁盘读写
    /// </summary>
    public static IFileSystem FileSystem
    {
        get => _fileSystem.Value;
        internal set => _fileSystem = new Lazy<IFileSystem>(() => value);
    }

    public static bool IsFastTestMode
    {
        get => Volatile.Read(ref _isFastTestMode);
        set => Volatile.Write(ref _isFastTestMode, value);
    }

    public static int TimeAccelerationFactor
    {
        get => Volatile.Read(ref _timeAccelerationFactor);
        set => Volatile.Write(ref _timeAccelerationFactor, value);
    }

    /// <summary>
    /// 从 ~/.jcc/auth.json 读取真实 API Key
    /// 优先读取当前 Provider 对应的 Key，回退到第一个可用的 Key
    /// </summary>
    public static string GetRealApiKey()
    {
        if (_cachedApiKey is not null)
            return _cachedApiKey;

        var authPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "auth.json");

        if (!FileSystem.FileExists(authPath))
            return _cachedApiKey = MockServer.MockServerOptions.DefaultApiKey;

        try
        {
            var json = FileSystem.ReadAllText(authPath);
            var authData = System.Text.Json.JsonSerializer.Deserialize(json, TestConfigurationJsonContext.Default.DictionaryStringString);
            if (authData is null || authData.Count == 0)
                return _cachedApiKey = MockServer.MockServerOptions.DefaultApiKey;

            // 优先读取当前 Provider 对应的 Key
            var provider = Environment.GetEnvironmentVariable("JCC_PROVIDER") ?? "openai";
            if (authData.TryGetValue(provider, out var providerKey) && !string.IsNullOrWhiteSpace(providerKey))
                return _cachedApiKey = providerKey;

            // 回退到第一个可用的 Key
            foreach (var (_, value) in authData)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return _cachedApiKey = value;
            }

            return _cachedApiKey = MockServer.MockServerOptions.DefaultApiKey;
        }
        catch
        {
            return _cachedApiKey = MockServer.MockServerOptions.DefaultApiKey;
        }
    }

    public static TimeSpan GetWaitTime(TimeSpan originalWait)
    {
        if (!Volatile.Read(ref _isFastTestMode)) return originalWait;
        var factor = Volatile.Read(ref _timeAccelerationFactor);
        var ticks = originalWait.Ticks / factor;
        return ticks > 0 ? TimeSpan.FromTicks(ticks) : TimeSpan.Zero;
    }

    public static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var actualDelay = GetWaitTime(delay);
        if (actualDelay > TimeSpan.Zero)
        {
            await Task.Delay(actualDelay, cancellationToken).ConfigureAwait(true);
        }
    }
}
