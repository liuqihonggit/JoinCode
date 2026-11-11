namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class VimNormalKeyAttribute : Attribute {
    /// <summary>获取按键字符。</summary>
    public char Key { get; }

    /// <summary>构造 Vim 普通模式按键特性。</summary>
    public VimNormalKeyAttribute(char key) {
        Key = key;
    }
}