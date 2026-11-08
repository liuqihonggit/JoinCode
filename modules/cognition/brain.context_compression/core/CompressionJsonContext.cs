namespace Core.Context.Compression;

/// <summary>
/// 压缩模块 JSON 序列化上下文 — 为基本类型提供源码生成的 JSON 序列化支持
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
public partial class CompressionJsonContext : JsonSerializerContext;
