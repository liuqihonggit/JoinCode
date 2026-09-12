namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 元素类型 — 多模态 LLM 识别的界面元素分类
/// </summary>
public enum UiElementType
{
    /// <summary>未知类型</summary>
    Unknown,

    /// <summary>按钮</summary>
    Button,

    /// <summary>文本输入框</summary>
    TextBox,

    /// <summary>菜单栏</summary>
    Menu,

    /// <summary>菜单项</summary>
    MenuItem,

    /// <summary>对话框</summary>
    Dialog,

    /// <summary>进度条</summary>
    ProgressBar,

    /// <summary>复选框</summary>
    CheckBox,

    /// <summary>单选按钮</summary>
    RadioButton,

    /// <summary>图标</summary>
    Icon,

    /// <summary>纯文本标签</summary>
    Text,

    /// <summary>图片</summary>
    Image,

    /// <summary>超链接</summary>
    Link,

    /// <summary>下拉选择框</summary>
    ComboBox,

    /// <summary>列表项</summary>
    ListItem,

    /// <summary>窗口标题栏</summary>
    TitleBar,

    /// <summary>滚动条</summary>
    ScrollBar,
}
