namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件原子编辑的转换结果 — 新内容(可为 null 表示不变)与调用方自定义结果。
/// <para>EditFileAsync 的 transform 委托返回该类型, 由文件系统 Actor 串行执行读-改-写事务。</para>
/// </summary>
/// <typeparam name="T">调用方自定义结果类型。</typeparam>
/// <param name="NewContent">要写回的新内容; null 表示保持原内容不变。</param>
/// <param name="Result">调用方自定义结果。</param>
public sealed record EditFileTransformResult<T>(byte[]? NewContent, T Result);
