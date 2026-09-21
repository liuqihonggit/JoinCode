namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// TreeSitter 解析器封装 — 提供源码 AST 解析能力
/// </summary>
public sealed class TreeSitterParser : IDisposable {
    private readonly Language _language;
    private readonly Parser _parser;
    private int _disposed;

    /// <summary>
    /// 构造 TreeSitter 解析器
    /// </summary>
    /// <param name="languageId">语言标识符（如 "csharp"、"python"）</param>
    public TreeSitterParser(string languageId) {
        ArgumentNullException.ThrowIfNull(languageId);

        _language = new Language(languageId);
        _parser = new Parser(_language);
    }

    /// <summary>
    /// 解析源码生成 AST
    /// </summary>
    /// <param name="sourceCode">源码文本</param>
    /// <returns>解析得到的语法树</returns>
    /// <exception cref="ObjectDisposedException">对象已释放</exception>
    /// <exception cref="InvalidOperationException">解析失败</exception>
    public Tree Parse(string sourceCode) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var tree = _parser.Parse(sourceCode);
        return tree ?? throw new InvalidOperationException($"[SRH001] TreeSitter 解析失败，语言: {_language.Name}");
    }

    /// <summary>
    /// 增量解析源码 — 利用旧树复用未变更部分的 AST 节点
    /// </summary>
    /// <param name="sourceCode">新源码文本</param>
    /// <param name="oldTree">旧语法树</param>
    /// <returns>解析得到的新语法树</returns>
    /// <exception cref="ObjectDisposedException">对象已释放</exception>
    /// <exception cref="InvalidOperationException">增量解析失败</exception>
    public Tree Parse(string sourceCode, Tree oldTree) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(oldTree);

        var tree = _parser.Parse(sourceCode, oldTree);
        return tree ?? throw new InvalidOperationException($"[SRH002] TreeSitter 增量解析失败，语言: {_language.Name}");
    }

    /// <summary>
    /// 释放解析器和语言资源
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        _parser.Dispose();
        _language.Dispose();
    }
}