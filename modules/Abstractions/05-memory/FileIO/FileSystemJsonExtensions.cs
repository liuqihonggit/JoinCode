namespace JoinCode.Abstractions.Interfaces;

public static class FileSystemJsonExtensions
{
    public static async Task<T?> ReadAndDeserializeAsync<T>(
        this IFileSystem fs,
        string path,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        var json = await fs.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, jsonTypeInfo);
    }
}
