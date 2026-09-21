namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class VimKeyAttribute : Attribute {
    /// <summary>获取快捷键。</summary>
    public char Key { get; }

    /// <summary>构造 Vim 快捷键特性。</summary>
    public VimKeyAttribute(char key) {
        Key = key;
    }
}