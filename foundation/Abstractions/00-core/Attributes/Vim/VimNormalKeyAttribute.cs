namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class VimNormalKeyAttribute : Attribute
{
    public char Key { get; }

    public VimNormalKeyAttribute(char key)
    {
        Key = key;
    }
}
