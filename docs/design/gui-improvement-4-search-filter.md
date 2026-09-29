# 设计：后台代理搜索过滤

## 需求

后台代理面板加搜索框按名称/状态过滤，子代理多了以后（如 10+）快速定位。

## 现状分析

- `BackgroundAgentsPanelViewModel.Items` 是 `ObservableCollection<BackgroundAgentItemVm>`
- `ApplySnapshot` 清空重建 Items，无过滤
- `BackgroundAgentItemVm` 有 `Name`/`Description`/`State` 属性可搜索

## 设计方案

### 方案：VM 加 SearchText + 过滤 View

**核心思路**：`BackgroundAgentsPanelViewModel` 加 `SearchText` 属性，用 `CollectionViewSource` 或手动过滤 `Items`。

### 改动清单

#### 1. BackgroundAgentsPanelViewModel 加搜索属性
```csharp
[ObservableProperty]
private string _searchText = string.Empty;

partial void OnSearchTextChanged(string value) => ApplyFilter();
```

#### 2. 加过滤方法
```csharp
private void ApplyFilter() {
    if (string.IsNullOrEmpty(SearchText)) {
        // 显示全部
        foreach (var item in Items) item.IsVisible = true;
        return;
    }
    var keyword = SearchText.Trim();
    foreach (var item in Items) {
        item.IsVisible = item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                      || item.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                      || item.State.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
```

#### 3. BackgroundAgentItemVm 加 IsVisible 属性
```csharp
public bool IsVisible { get; set; } = true;
```

#### 4. ApplySnapshot 后调用 ApplyFilter
```csharp
public void ApplySnapshot(IReadOnlyList<BackgroundAgentInfo> snapshot) {
    Items.Clear();
    foreach (var info in snapshot) Items.Add(new BackgroundAgentItemVm(info));
    CountText = ...;
    ApplyFilter();  // 新增
    SnapshotApplied?.Invoke(snapshot.Count);
}
```

#### 5. XAML 加搜索框
```xml
<TextBox Text="{Binding BackgroundPanel.SearchText}" Watermark="搜索子代理..." />
```

#### 6. ItemsControl 加 IsVisible 绑定
```xml
<ItemsControl ItemsSource="{Binding BackgroundPanel.Items}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <Border IsVisible="{Binding IsVisible}" ...>
```

## 涉及文件

- `app/gui/view_models/a_to_c/BackgroundAgentsPanelViewModel.cs` — 加 SearchText + ApplyFilter
- `app/gui/view_models/a_to_c/BackgroundAgentsPanelViewModel.cs` — BackgroundAgentItemVm 加 IsVisible
- `app/gui/views/MainWindow.axaml` — 加搜索框 + IsVisible 绑定
