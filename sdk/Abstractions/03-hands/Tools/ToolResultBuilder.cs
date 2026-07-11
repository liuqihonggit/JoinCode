namespace JoinCode.Abstractions.Tools;

public sealed class ToolResultBuilder
{
    private readonly List<ToolContent> _content = new();
    private bool _isError;

    public static ToolResultBuilder Success() => new();

    public static ToolResultBuilder Error() => new() { _isError = true };

    public ToolResultBuilder WithText(string text)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = text });
        return this;
    }

    public ToolResultBuilder WithImage(string base64Data, string mediaType)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Image, Data = base64Data, MimeType = mediaType });
        return this;
    }

    public ToolResultBuilder WithError(string errorMessage)
    {
        _isError = true;
        _content.Clear();
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = errorMessage });
        return this;
    }

    public ToolResult Build()
    {
        return new ToolResult
        {
            Content = _content,
            IsError = _isError
        };
    }
}
