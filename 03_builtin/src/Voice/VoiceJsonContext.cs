
namespace Services.Voice;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WhisperTranscriptionResponse))]
[JsonSerializable(typeof(WhisperTranscriptionRequest))]
[JsonSerializable(typeof(string))]
internal sealed partial class VoiceJsonContext : JsonSerializerContext;
