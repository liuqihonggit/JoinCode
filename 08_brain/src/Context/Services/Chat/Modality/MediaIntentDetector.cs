namespace Core.Context.Modality;

/// <summary>
/// 媒介意图检测器 — 从用户文本消息中识别多模态意图关键词。
/// 阶段1：基于关键词匹配；阶段2可扩展为 NLP/LLM 分类。
/// </summary>
public sealed class MediaIntentDetector
{
    /// <summary>检测结果 — 检测到的媒介类型和匹配的关键词</summary>
    public sealed record DetectionResult(
        ModelModalityKind DetectedModalities,
        IReadOnlyList<string> MatchedKeywords);

    private static readonly FrozenDictionary<string, ModelModalityKind> KeywordMap = BuildKeywordMap();

    private static FrozenDictionary<string, ModelModalityKind> BuildKeywordMap()
    {
        var dict = new Dictionary<string, ModelModalityKind>(StringComparer.OrdinalIgnoreCase);

        var entries = new (ModelModalityKind Modality, string[] Keywords)[]
        {
            (ModelModalityKind.ReadImage, ["图片", "照片", "截图", "图像", "看图", "看这张", "image", "photo", "screenshot", "picture", "look at this"]),
            (ModelModalityKind.ReadGif, ["动图", "GIF", "gif", "animated image"]),
            (ModelModalityKind.ReadVideo, ["视频", "录像", "影片", "看视频", "video", "clip", "footage", "watch this"]),
            (ModelModalityKind.ReadAudio, ["音频", "语音", "录音", "听", "audio", "voice recording", "listen to"]),
            (ModelModalityKind.ReadPdf, ["PDF", "pdf", "文档", "文件", "document", "file"]),
            (ModelModalityKind.GenerateImage, ["画图", "生成图片", "绘图", "作画", "画一个", "画一张", "draw", "generate image", "create image", "paint"]),
            (ModelModalityKind.GenerateVideo, ["生成视频", "制作视频", "创建视频", "generate video", "create video", "make video"]),
            (ModelModalityKind.GenerateAudio, ["生成音频", "朗读", "TTS", "text to speech", "generate audio", "read aloud"]),
        };

        foreach (var (modality, keywords) in entries)
        {
            foreach (var keyword in keywords)
            {
                dict[keyword] = modality;
            }
        }

        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从消息文本中检测媒介意图
    /// </summary>
    public DetectionResult Detect(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new DetectionResult(ModelModalityKind.None, []);

        var detected = ModelModalityKind.None;
        var matchedKeywords = new List<string>();

        foreach (var kvp in KeywordMap)
        {
            if (message.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                detected |= kvp.Value;
                matchedKeywords.Add(kvp.Key);
            }
        }

        return new DetectionResult(detected, matchedKeywords);
    }
}
