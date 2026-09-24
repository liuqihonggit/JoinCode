namespace JoinCode.Abstractions.Interfaces;

public enum EditorMode {
    [EnumValue("normal")] Normal,
    [EnumValue("vim")] Vim
}

public interface IEditorModeService : IAsyncDisposable {
    /// <summary>获取当前编辑模式。</summary>
    EditorMode CurrentMode { get; }
    /// <summary>设置编辑模式。</summary>
    void SetMode(EditorMode mode);
    /// <summary>切换编辑模式。</summary>
    EditorMode Toggle();
}