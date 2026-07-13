namespace JoinCode.CodeIndex.Ast;

public sealed class TreeSitterParser : IDisposable
{
    private readonly Language _language;
    private readonly Parser _parser;
    private int _disposed;

    public TreeSitterParser(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);

        _language = new Language(languageId);
        _parser = new Parser(_language);
    }

    public Tree Parse(string sourceCode)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var tree = _parser.Parse(sourceCode);
        return tree ?? throw new InvalidOperationException($"TreeSitter 解析失败，语言: {_language.Name}");
    }

    public Tree Parse(string sourceCode, Tree oldTree)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(oldTree);

        var tree = _parser.Parse(sourceCode, oldTree);
        return tree ?? throw new InvalidOperationException($"TreeSitter 增量解析失败，语言: {_language.Name}");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _parser.Dispose();
        _language.Dispose();
    }
}
