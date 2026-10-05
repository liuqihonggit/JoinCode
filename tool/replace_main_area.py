#!/usr/bin/env python3
"""Replace MainAreaCol content in MainWindow.axaml with MessageAreaView.

Replaces lines 210-565 (MainAreaCol + nested RightSashCol/SecondarySideBarCol)
with a clean structure where MessageAreaView is in Column 3, and
RightSashCol/SecondarySideBarCol are direct children of the Column grid.
"""
import sys

SRC = r"D:\project\w1\app\gui\views\MainWindow.axaml"

with open(SRC, encoding="utf-8") as f:
    lines = f.readlines()

# Lines 210-565 (1-indexed) = indices 209-564 (0-indexed)
# Verify boundaries
assert "Column 3" in lines[209], f"Line 210 mismatch: {lines[209]!r}"
assert lines[564].strip() == "</Grid>", f"Line 565 mismatch: {lines[564]!r}"

# New content for lines 210-565
new_content = """    <!-- ===== Column 3：主区（MessageAreaView — 消息列表+编辑器+面板+输入栏） ===== -->
    <Grid x:Name="MainAreaCol" Grid.Column="3">
      <local:MessageAreaView x:Name="MessageArea" />
    </Grid>

    <!-- ===== Column 4：Secondary Side Bar 拖拽手柄 ===== -->
    <Border x:Name="RightSashCol" Grid.Column="4" Width="6" HorizontalAlignment="Stretch"
            Background="{DynamicResource GuiDivider}"
            Cursor="SizeWestEast"
            IsVisible="{Binding IsSecondarySideBarOpen}"
            ToolTip.Tip="拖拽调整面板宽度" />

    <!-- ===== Column 5：Secondary Side Bar (右侧边栏) ===== -->
    <Border x:Name="SecondarySideBarCol" Grid.Column="5" Width="{Binding SecondarySideBarWidth}" ClipToBounds="True"
            IsVisible="{Binding IsSecondarySideBarOpen}"
            Background="{DynamicResource GuiSidebarBackground}"
            BorderBrush="{DynamicResource GuiDivider}" BorderThickness="1,0,0,0"
            DragDrop.DragOver="OnPanelDragOver" DragDrop.Drop="OnPanelDropRight">
      <Panel>
        <local:FileTreePanelView IsVisible="{Binding IsSecondaryFileTree}" />
        <local:SidebarView IsVisible="{Binding IsSecondarySessions}" />
      </Panel>
    </Border>
"""

# Replace lines 210-565 (indices 209-564)
new_lines = lines[:209] + [new_content] + lines[565:]

with open(SRC, "w", encoding="utf-8") as f:
    f.writelines(new_lines)

print(f"Updated {SRC}")
print(f"Old line count: {len(lines)}")
print(f"New line count: {len(new_lines)}")
