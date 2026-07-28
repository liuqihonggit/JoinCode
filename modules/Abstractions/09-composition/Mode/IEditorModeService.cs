namespace JoinCode.Abstractions.Interfaces;

public enum EditorMode
{
    [EnumValue("normal")] Normal,
    [EnumValue("vim")] Vim
}

public interface IEditorModeService : IDisposable
{
    EditorMode CurrentMode { get; }
    void SetMode(EditorMode mode);
    EditorMode Toggle();
}
