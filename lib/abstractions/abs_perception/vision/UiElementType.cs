namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 元素类型 — 多模态 LLM 识别的界面元素分类
/// </summary>
public enum UiElementType
{
    /// <summary>未知类型</summary>
    [EnumValue("unknown")]
    Unknown,

    /// <summary>按钮</summary>
    [EnumValue("button")]
    Button,

    /// <summary>文本输入框</summary>
    [EnumValue("text_box")]
    TextBox,

    /// <summary>菜单栏</summary>
    [EnumValue("menu")]
    Menu,

    /// <summary>菜单项</summary>
    [EnumValue("menu_item")]
    MenuItem,

    /// <summary>对话框</summary>
    [EnumValue("dialog")]
    Dialog,

    /// <summary>进度条</summary>
    [EnumValue("progress_bar")]
    ProgressBar,

    /// <summary>复选框</summary>
    [EnumValue("check_box")]
    CheckBox,

    /// <summary>单选按钮</summary>
    [EnumValue("radio_button")]
    RadioButton,

    /// <summary>图标</summary>
    [EnumValue("icon")]
    Icon,

    /// <summary>纯文本标签</summary>
    [EnumValue("text")]
    Text,

    /// <summary>图片</summary>
    [EnumValue("image")]
    Image,

    /// <summary>超链接</summary>
    [EnumValue("link")]
    Link,

    /// <summary>下拉选择框</summary>
    [EnumValue("combo_box")]
    ComboBox,

    /// <summary>列表项</summary>
    [EnumValue("list_item")]
    ListItem,

    /// <summary>窗口标题栏</summary>
    [EnumValue("title_bar")]
    TitleBar,

    /// <summary>滚动条</summary>
    [EnumValue("scroll_bar")]
    ScrollBar,
}
