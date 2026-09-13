namespace Core.Context;

/// <summary>
/// 分析阶段预处理中间件接口 — 关键词/同义词检测与注入
/// </summary>
public interface IAnalyzePreprocessMiddleware : IMiddleware<PreprocessContext> { }
