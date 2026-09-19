namespace Structura.Dag;

/// <summary>
/// DAG 操作结果
/// </summary>
public sealed class DagResult {
    /// <summary>操作是否成功</summary>
    public bool Success { get; init; }
    /// <summary>失败时的错误消息;成功时为 null</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>检测到环时的环路径;无环时为空列表</summary>
    public IReadOnlyList<string> CyclePath { get; init; } = [];

    /// <summary>构造成功结果</summary>
    /// <returns>Success=true 的结果</returns>
    public static DagResult Ok() => new() { Success = true };
    /// <summary>构造失败结果</summary>
    /// <param name="message">错误消息</param>
    /// <returns>Success=false 且携带错误消息的结果</returns>
    public static DagResult Fail(string message) => new() { Success = false, ErrorMessage = message };
    /// <summary>构造检测到环的失败结果</summary>
    /// <param name="path">环路径节点 ID 序列</param>
    /// <returns>Success=false 且携带环路径的结果</returns>
    public static DagResult Cycle(IReadOnlyList<string> path) => new() { Success = false, ErrorMessage = "Cycle detected", CyclePath = path };
}