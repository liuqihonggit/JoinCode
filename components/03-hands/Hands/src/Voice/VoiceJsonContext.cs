
namespace Services.Voice;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WhisperTranscriptionResponse))]
[JsonSerializable(typeof(WhisperTranscriptionRequest))]
[JsonSerializable(typeof(string))]
internal sealed partial class VoiceJsonContext : JsonSerializerContext;
