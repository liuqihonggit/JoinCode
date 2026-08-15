
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型模态能力标志 — [Flags] 位标志枚举，通过位运算组合表示多模态能力
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 ModelModalityKindConstants + ModelModalityKindExtensions
/// 用法: model.Modalities.HasFlag(ModelModalityKind.ReadImage) 或 model.Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage
/// </summary>
[Flags]
public enum ModelModalityKind
{
    /// <summary>无能力</summary>
    [EnumValue("none")] None = 0,

    /// <summary>文本输入输出 — 所有模型的基础能力</summary>
    [EnumValue("text")] Text = 1,

    /// <summary>读取静态图片（png/jpg/webp 等）</summary>
    [EnumValue("readImage")] ReadImage = 2,

    /// <summary>读取动图（gif 等）</summary>
    [EnumValue("readGif")] ReadGif = 4,

    /// <summary>读取视频（mp4/webm 等）</summary>
    [EnumValue("readVideo")] ReadVideo = 8,

    /// <summary>读取音频（mp3/wav/m4a 等）</summary>
    [EnumValue("readAudio")] ReadAudio = 16,

    /// <summary>读取 PDF 文档</summary>
    [EnumValue("readPdf")] ReadPdf = 32,

    /// <summary>生成图片（DALL-E / Stable Diffusion 等）</summary>
    [EnumValue("generateImage")] GenerateImage = 64,

    /// <summary>生成视频（Sora / Runway 等）</summary>
    [EnumValue("generateVideo")] GenerateVideo = 128,

    /// <summary>生成音频（TTS / 音乐生成等）</summary>
    [EnumValue("generateAudio")] GenerateAudio = 256,

    /// <summary>扩展思考/推理（Chain-of-Thought）</summary>
    [EnumValue("thinking")] Thinking = 512,

    /// <summary>代码执行（Interpreter / Code Execution 等）</summary>
    [EnumValue("codeExecution")] CodeExecution = 1024,

    /// <summary>网页搜索（内置搜索能力）</summary>
    [EnumValue("webSearch")] WebSearch = 2048,

    /// <summary>函数调用/工具使用</summary>
    [EnumValue("toolUse")] ToolUse = 4096,

    /// <summary>多模态输入组合：图片 + 动图 + 视频 + 音频 + PDF</summary>
    AllInput = ReadImage | ReadGif | ReadVideo | ReadAudio | ReadPdf,

    /// <summary>多模态输出组合：生图 + 生视频 + 生音频</summary>
    AllOutput = GenerateImage | GenerateVideo | GenerateAudio,

    /// <summary>全部能力</summary>
    All = Text | AllInput | AllOutput | Thinking | CodeExecution | WebSearch | ToolUse
}
