namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class DisplayTextAttribute : Attribute
{
    public string Text { get; }

    public DisplayTextAttribute(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }
}
