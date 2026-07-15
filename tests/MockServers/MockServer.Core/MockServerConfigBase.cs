namespace MockServer.Core;

/// <summary>
/// MockServer 配置基类 — 提供 LoadFromFile / LoadFromFileOrDefault / ResolveConfigPath 通用实现
/// </summary>
/// <typeparam name="TSelf">CRTP 自引用类型，用于静态工厂方法返回具体类型</typeparam>
public abstract class MockServerConfigBase<TSelf> where TSelf : MockServerConfigBase<TSelf>, new()
{
    /// <summary>监听端口（0 表示自动分配）</summary>
    public int Port { get; set; } = 0;

    /// <summary>JSON 序列化元数据（子类必须提供 AOT 兼容的 JsonTypeInfo）</summary>
    protected abstract JsonTypeInfo<TSelf> JsonTypeInfo { get; }

    /// <summary>日志前缀（如 "[MockServer]" 或 "[Mcp.MockServer]"）</summary>
    protected abstract string LogPrefix { get; }

    /// <summary>配置文件不存在时的错误消息模板</summary>
    protected abstract string ConfigNotFoundMessage { get; }

    /// <summary>从 JSON 文件加载配置</summary>
    public static TSelf LoadFromFile(string path, JsonTypeInfo<TSelf> jsonTypeInfo, string configNotFoundMessage)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException(string.Format(configNotFoundMessage, path), path);

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize(json, jsonTypeInfo)
            ?? throw new InvalidOperationException($"配置文件反序列化失败: {path}");
        return config;
    }

    /// <summary>从 JSON 文件加载配置 — 文件不存在时返回默认配置</summary>
    /// <remarks>
    /// 查找顺序：1) 指定路径 2) exe 所在目录下的同名文件（应对工作目录不在 exe 目录的场景）
    /// </remarks>
    public static TSelf LoadFromFileOrDefault(string path, JsonTypeInfo<TSelf> jsonTypeInfo, string logPrefix, string configNotFoundMessage)
    {
        var actualPath = ResolveConfigPath(path);
        if (actualPath is null)
            return new TSelf();
        try
        {
            return LoadFromFile(actualPath, jsonTypeInfo, configNotFoundMessage);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{logPrefix} 加载配置文件失败，使用默认配置: {ex.Message}");
            return new TSelf();
        }
    }

    /// <summary>解析配置文件路径 — 指定路径存在则返回；否则在 exe 所在目录查找同名文件</summary>
    protected static string? ResolveConfigPath(string path)
    {
        if (File.Exists(path))
            return path;
        var fileName = Path.GetFileName(path);
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(fallbackPath) ? fallbackPath : null;
    }
}
