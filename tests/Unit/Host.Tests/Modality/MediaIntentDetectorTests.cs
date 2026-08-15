namespace Host.Tests.Modality;

public sealed class MediaIntentDetectorTests
{
    private readonly MediaIntentDetector _detector = new();

    [Fact]
    public void Detect_EmptyMessage_ReturnsNone()
    {
        var result = _detector.Detect("");
        Assert.Equal(ModelModalityKind.None, result.DetectedModalities);
        Assert.Empty(result.MatchedKeywords);
    }

    [Fact]
    public void Detect_NullMessage_ReturnsNone()
    {
        var result = _detector.Detect(null!);
        Assert.Equal(ModelModalityKind.None, result.DetectedModalities);
    }

    [Fact]
    public void Detect_PlainText_ReturnsNone()
    {
        var result = _detector.Detect("帮我写一个斐波那契函数");
        Assert.Equal(ModelModalityKind.None, result.DetectedModalities);
    }

    [Fact]
    public void Detect_ImageKeyword_ReturnsReadImage()
    {
        var result = _detector.Detect("看这张图片有什么问题");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadImage));
        Assert.Contains("图片", result.MatchedKeywords);
    }

    [Fact]
    public void Detect_EnglishImageKeyword_ReturnsReadImage()
    {
        var result = _detector.Detect("look at this image");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadImage));
    }

    [Fact]
    public void Detect_VideoKeyword_ReturnsReadVideo()
    {
        var result = _detector.Detect("帮我分析这个视频");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadVideo));
        Assert.Contains("视频", result.MatchedKeywords);
    }

    [Fact]
    public void Detect_AudioKeyword_ReturnsReadAudio()
    {
        var result = _detector.Detect("听这段音频");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadAudio));
    }

    [Fact]
    public void Detect_PdfKeyword_ReturnsReadPdf()
    {
        var result = _detector.Detect("帮我读取这个PDF文件");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadPdf));
    }

    [Fact]
    public void Detect_GenerateImageKeyword_ReturnsGenerateImage()
    {
        var result = _detector.Detect("帮我画一张猫的图");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.GenerateImage));
        Assert.Contains("画一张", result.MatchedKeywords);
    }

    [Fact]
    public void Detect_GenerateVideoKeyword_ReturnsGenerateVideo()
    {
        var result = _detector.Detect("帮我生成视频");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.GenerateVideo));
    }

    [Fact]
    public void Detect_GenerateAudioKeyword_ReturnsGenerateAudio()
    {
        var result = _detector.Detect("用TTS朗读这段文字");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.GenerateAudio));
    }

    [Fact]
    public void Detect_MultipleKeywords_CombinesModalities()
    {
        var result = _detector.Detect("看这张图片并生成视频");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadImage));
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.GenerateVideo));
        Assert.True(result.MatchedKeywords.Count >= 2);
    }

    [Fact]
    public void Detect_GifKeyword_ReturnsReadGif()
    {
        var result = _detector.Detect("这个动图很有趣");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadGif));
    }

    [Fact]
    public void Detect_CaseInsensitive()
    {
        var result = _detector.Detect("LOOK AT THIS PHOTO");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.ReadImage));
    }

    [Fact]
    public void Detect_GenerateImageEnglish_ReturnsGenerateImage()
    {
        var result = _detector.Detect("draw a cat for me");
        Assert.True(result.DetectedModalities.HasFlag(ModelModalityKind.GenerateImage));
    }
}
