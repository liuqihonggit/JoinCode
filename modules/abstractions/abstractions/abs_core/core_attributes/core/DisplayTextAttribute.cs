namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class DisplayTextAttribute : Attribute {
    /// <summary>获取显示文本。</summary>
    public string Text { get; }

    /// <summary>构造显示文本特性。</summary>
    public DisplayTextAttribute(string text) {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }
}