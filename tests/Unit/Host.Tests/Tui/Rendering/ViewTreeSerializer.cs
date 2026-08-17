namespace Host.Tests.Tui.Rendering;

/// <summary>
/// View 树序列化器 — 递归将 Terminal.Gui View 树转为文本，用于快照比对。
/// 不依赖 Application.MainLoop，纯属性断言，捕获布局结构/文本/层级错误。
/// </summary>
public static class ViewTreeSerializer
{
    /// <summary>
    /// 序列化 View 树为文本。
    /// </summary>
    /// <param name="view">根视图。</param>
    /// <param name="indent">缩进层级（内部递归用）。</param>
    /// <returns>序列化文本。</returns>
    public static string Serialize(View view, int indent = 0)
    {
        var sb = new StringBuilder(256);
        SerializeInto(view, indent, sb);
        return sb.ToString();
    }

    private static void SerializeInto(View view, int indent, StringBuilder sb)
    {
        var pad = new string(' ', indent * 2);
        var typeName = view.GetType().Name;
        var visible = SafeGet(view, static v => v.Visible, true);
        var canFocus = SafeGet(view, static v => v.CanFocus, false);
        sb.Append(pad).Append(typeName)
          .Append(" [Visible=").Append(visible)
          .Append(", CanFocus=").Append(canFocus).Append(']');

        var text = SafeGet(view, static v => v.Text, string.Empty);
        if (!string.IsNullOrEmpty(text))
        {
            sb.Append(" Text=\"")
              .Append(text.Replace("\n", "\\n").Replace("\r", "\\r"))
              .Append('"');
        }
        sb.AppendLine();

        var subs = SafeGet(view, static v => v.SubViews, Array.Empty<View>());
        foreach (var sub in subs)
        {
            SerializeInto(sub, indent + 1, sb);
        }
    }

    private static T SafeGet<T>(View view, Func<View, T> getter, T fallback)
    {
        try
        {
            return getter(view);
        }
        catch
        {
            return fallback;
        }
    }
}
