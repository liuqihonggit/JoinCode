namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限判定轻量客户端 — 只做一次结构化判定，不执行工具、不进入 QueryEngine 循环。
/// <para>用于 <c>LlmAutoModeClassifier</c> 的 Stage 2；禁止实现为 <c>IQueryEngine.ExecuteQueryAsync</c>，
/// 否则会形成 Query → Tool → Permission → Judge → Query 的递归。</para>
/// </summary>
public interface IPermissionJudgeClient {
    /// <summary>
    /// 发送一次判定请求，返回模型原始文本（由调用方按 DTO 解析）。
    /// </summary>
    /// <param name="systemPrompt">固定安全系统提示词</param>
    /// <param name="userPrompt">结构化判定请求（工具/参数/上下文摘要）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>模型文本；不可用时返回 null</returns>
    Task<string?> JudgeAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
