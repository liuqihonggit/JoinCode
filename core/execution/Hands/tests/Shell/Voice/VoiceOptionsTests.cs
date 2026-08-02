namespace Core.Tests.Voice;

public sealed class VoiceOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldMatchExpected()
    {
        var options = new VoiceOptions();

        options.Backend.Should().Be(SttBackend.WhisperApi);
        options.WhisperApiEndpoint.Should().Be("https://api.openai.com/v1/audio/transcriptions");
        options.WhisperApiKey.Should().BeNull();
        options.WhisperModel.Should().Be("whisper-1");
        options.WhisperLanguage.Should().Be("zh");
        options.SampleRate.Should().Be(16000);
        options.Channels.Should().Be(1);
        options.MaxRecordingDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.SilenceDetectionInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.SilenceThreshold.Should().Be(0.01);
        options.SilenceTimeout.Should().Be(TimeSpan.FromSeconds(3));
        options.LocalModelPath.Should().BeEmpty();
    }
}
