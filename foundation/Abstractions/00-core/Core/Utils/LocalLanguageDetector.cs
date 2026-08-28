namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 本地语言检测器 — 读取当前电脑配置的 UI 语言，AOT安全，InvariantGlobalization安全
/// 检测链: JCC_LANGUAGE环境变量 → Windows GetUserDefaultUILanguage → CultureInfo → fallback "en"
/// </summary>
public static class LocalLanguageDetector
{
    private static readonly FrozenDictionary<ushort, string> WindowsLangIdToIso = CreateWindowsLangMap();

    /// <summary>
    /// 检测当前本地语言，返回两字母ISO代码（如 "zh", "en", "ja"）
    /// </summary>
    public static string Detect()
    {
        var envLang = Environment.GetEnvironmentVariable("JCC_LANGUAGE");
        if (!string.IsNullOrWhiteSpace(envLang))
            return NormalizeIsoCode(envLang);

        var windowsLang = TryDetectWindowsLanguage();
        if (windowsLang is not null)
            return windowsLang;

        var cultureLang = TryDetectCultureLanguage();
        if (cultureLang is not null)
            return cultureLang;

        return "en";
    }

    /// <summary>
    /// 获取当前本地语言的母语名称（如 "中文", "English", "日本語"）— 供 LLM 提示词使用
    /// </summary>
    public static string GetNativeLanguageName(string isoCode) => isoCode switch
    {
        "zh" => "中文",
        "en" => "English",
        "ja" => "日本語",
        "ko" => "한국어",
        "fr" => "Français",
        "de" => "Deutsch",
        "es" => "Español",
        "ru" => "Русский",
        "it" => "Italiano",
        "pt" => "Português",
        "nl" => "Nederlands",
        "sv" => "Svenska",
        "tr" => "Türkçe",
        "pl" => "Polski",
        "ar" => "العربية",
        "th" => "ไทย",
        "vi" => "Tiếng Việt",
        "id" => "Bahasa Indonesia",
        "hi" => "हिन्दी",
        _ => "English"
    };

    /// <summary>
    /// 获取当前本地语言的母语名称（便捷方法，自动检测语言）
    /// </summary>
    public static string DetectNativeLanguageName() => GetNativeLanguageName(Detect());

    private static string? TryDetectWindowsLanguage()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var langId = GetUserDefaultUILanguage();
            if (langId == 0)
                return null;
            var primaryLangId = (ushort)(langId & 0x3FF);
            return WindowsLangIdToIso.TryGetValue(primaryLangId, out var iso) ? iso : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryDetectCultureLanguage()
    {
        try
        {
            var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return string.IsNullOrEmpty(name) || name == "iv" ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeIsoCode(string code)
    {
        var trimmed = code.Trim().ToLowerInvariant();
        if (trimmed.Length >= 2)
            return trimmed.Substring(0, 2);
        return "en";
    }

    private static FrozenDictionary<ushort, string> CreateWindowsLangMap() => new Dictionary<ushort, string>
    {
        [0x04] = "zh",
        [0x09] = "en",
        [0x11] = "ja",
        [0x12] = "ko",
        [0x0C] = "fr",
        [0x07] = "de",
        [0x0A] = "es",
        [0x19] = "ru",
        [0x10] = "it",
        [0x16] = "pt",
        [0x13] = "nl",
        [0x1D] = "sv",
        [0x1F] = "tr",
        [0x15] = "pl",
        [0x01] = "ar",
        [0x1E] = "th",
        [0x2A] = "vi",
        [0x21] = "id",
        [0x04] = "zh",
    }.ToFrozenDictionary();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern ushort GetUserDefaultUILanguage();
}
