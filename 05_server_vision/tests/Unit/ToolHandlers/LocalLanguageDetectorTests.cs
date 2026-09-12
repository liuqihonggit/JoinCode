namespace Vision.Tests.ToolHandlers;

/// <summary>
/// LocalLanguageDetector 单元测试 — 验证电脑配置语言检测
/// </summary>
public sealed class LocalLanguageDetectorTests
{
    [Fact]
    public void GetNativeLanguageName_Zh_ShouldReturnChinese()
    {
        LocalLanguageDetector.GetNativeLanguageName("zh").Should().Be("中文");
    }

    [Fact]
    public void GetNativeLanguageName_En_ShouldReturnEnglish()
    {
        LocalLanguageDetector.GetNativeLanguageName("en").Should().Be("English");
    }

    [Fact]
    public void GetNativeLanguageName_Ja_ShouldReturnJapanese()
    {
        LocalLanguageDetector.GetNativeLanguageName("ja").Should().Be("日本語");
    }

    [Fact]
    public void GetNativeLanguageName_Ko_ShouldReturnKorean()
    {
        LocalLanguageDetector.GetNativeLanguageName("ko").Should().Be("한국어");
    }

    [Fact]
    public void GetNativeLanguageName_Unknown_ShouldReturnEnglish()
    {
        LocalLanguageDetector.GetNativeLanguageName("xx").Should().Be("English");
    }

    [Fact]
    public void GetNativeLanguageName_Fr_ShouldReturnFrench()
    {
        LocalLanguageDetector.GetNativeLanguageName("fr").Should().Be("Français");
    }

    [Fact]
    public void GetNativeLanguageName_De_ShouldReturnGerman()
    {
        LocalLanguageDetector.GetNativeLanguageName("de").Should().Be("Deutsch");
    }

    [Fact]
    public void Detect_WithJccLanguageEn_ShouldReturnEn()
    {
        var original = Environment.GetEnvironmentVariable("JCC_LANGUAGE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", "en");
            LocalLanguageDetector.Detect().Should().Be("en");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", original);
        }
    }

    [Fact]
    public void Detect_WithJccLanguageZh_ShouldReturnZh()
    {
        var original = Environment.GetEnvironmentVariable("JCC_LANGUAGE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", "zh");
            LocalLanguageDetector.Detect().Should().Be("zh");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", original);
        }
    }

    [Fact]
    public void Detect_WithJccLanguageJa_ShouldReturnJa()
    {
        var original = Environment.GetEnvironmentVariable("JCC_LANGUAGE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", "ja");
            LocalLanguageDetector.Detect().Should().Be("ja");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", original);
        }
    }

    [Fact]
    public void Detect_WithJccLanguageUpperCase_ShouldNormalizeToLower()
    {
        var original = Environment.GetEnvironmentVariable("JCC_LANGUAGE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", "EN");
            LocalLanguageDetector.Detect().Should().Be("en");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", original);
        }
    }

    [Fact]
    public void Detect_WithJccLanguageLongCode_ShouldTruncateToTwoLetters()
    {
        var original = Environment.GetEnvironmentVariable("JCC_LANGUAGE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", "en-US");
            LocalLanguageDetector.Detect().Should().Be("en");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", original);
        }
    }

    [Fact]
    public void DetectNativeLanguageName_ShouldReturnNonEmpty()
    {
        var name = LocalLanguageDetector.DetectNativeLanguageName();
        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Detect_WithoutEnvVar_ShouldReturnNonEmptyCode()
    {
        var original = Environment.GetEnvironmentVariable("JCC_LANGUAGE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", null);
            var result = LocalLanguageDetector.Detect();
            result.Should().NotBeNullOrEmpty();
            result.Should().HaveLength(2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_LANGUAGE", original);
        }
    }
}
