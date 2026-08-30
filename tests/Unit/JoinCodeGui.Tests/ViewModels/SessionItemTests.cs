namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// SessionItem 单元测试 — 验证侧边栏会话条目的默认值、属性赋值与 INPC 通知。
/// SessionItem 的 4 个 ObservableProperty（Title/IsSelected/IsRenaming/RenameDraft）
/// 驱动 UI 绑定刷新，INPC 通知断裂会导致界面不更新。
/// </summary>
public sealed class SessionItemTests
{
    [Fact]
    public void Constructor_GeneratesNonEmptyId()
    {
        var item = new SessionItem();

        item.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Constructor_TwoInstances_HaveDistinctIds()
    {
        var a = new SessionItem();
        var b = new SessionItem();

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void Defaults_TitleEmpty_IsSelectedFalse_IsRenamingFalse_RenameDraftEmpty()
    {
        var item = new SessionItem();

        item.Title.Should().BeEmpty();
        item.IsSelected.Should().BeFalse();
        item.IsRenaming.Should().BeFalse();
        item.RenameDraft.Should().BeEmpty();
    }

    [Fact]
    public void Title_SetValue_RaisesPropertyChanged()
    {
        var item = new SessionItem();
        var fired = new List<string?>();
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        item.Title = "会话 1";

        fired.Should().Contain(nameof(SessionItem.Title));
        item.Title.Should().Be("会话 1");
    }

    [Fact]
    public void Title_SameValue_DoesNotRaisePropertyChanged()
    {
        var item = new SessionItem { Title = "会话 1" };
        var fired = new List<string?>();
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        item.Title = "会话 1";

        fired.Should().NotContain(nameof(SessionItem.Title));
    }

    [Fact]
    public void IsSelected_Toggle_RaisesPropertyChanged()
    {
        var item = new SessionItem();
        var fired = new List<string?>();
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        item.IsSelected = true;

        fired.Should().Contain(nameof(SessionItem.IsSelected));
        item.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void IsRenaming_Toggle_RaisesPropertyChanged()
    {
        var item = new SessionItem();
        var fired = new List<string?>();
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        item.IsRenaming = true;

        fired.Should().Contain(nameof(SessionItem.IsRenaming));
        item.IsRenaming.Should().BeTrue();
    }

    [Fact]
    public void RenameDraft_SetValue_RaisesPropertyChanged()
    {
        var item = new SessionItem();
        var fired = new List<string?>();
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        item.RenameDraft = "新标题草稿";

        fired.Should().Contain(nameof(SessionItem.RenameDraft));
        item.RenameDraft.Should().Be("新标题草稿");
    }

    [Fact]
    public void Id_CanBeOverridden_ForPersistenceRestore()
    {
        var item = new SessionItem { Id = "fixed-id-123" };

        item.Id.Should().Be("fixed-id-123");
    }
}
