namespace Infrastructure.Network.Downloader;

/// <summary>
/// 下载器 AOT JSON 序列化上下文 — 源码生成器为 DownloadMetadata 及其嵌套类型生成序列化代码
/// <para>AOT 兼容:禁 dynamic/反射 emit,用 JsonSourceGeneration 提前生成</para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DownloadMetadata))]
internal sealed partial class DownloaderJsonContext : JsonSerializerContext;
