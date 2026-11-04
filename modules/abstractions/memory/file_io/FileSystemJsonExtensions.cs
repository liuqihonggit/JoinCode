namespace JoinCode.Abstractions.Interfaces;

public static class FileSystemJsonExtensions {
    /// <summary>异步读取文件并反序列化为指定类型。</summary>
    public static async Task<T?> ReadAndDeserializeAsync<T>(
        this IFileSystem fs,
        string path,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default) {
        var json = await fs.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return RelaxedJsonSerializer.Deserialize(json, jsonTypeInfo);
    }
}