#!/usr/bin/env python3
"""Extract MessageAreaView.axaml from MainWindow.axaml (lines 213-545).

Transforms:
  - ElementName=Root bindings → direct bindings (DataContext is MainViewModel)
  - Wraps in UserControl with proper namespaces
"""
import re
import sys

SRC = r"D:\project\w1\app\gui\views\MainWindow.axaml"
DST = r"D:\project\w1\app\gui\views\MessageAreaView.axaml"

with open(SRC, encoding="utf-8") as f:
    lines = f.readlines()

# Lines 213-545 (1-indexed) = indices 212-544 (0-indexed)
inner = "".join(lines[212:545])

# Transform ElementName=Root bindings:
#   {Binding DataContext.Foo, ElementName=Root} → {Binding Foo}
#   {Binding DataContext.Foo, ElementName=Root, ...} → {Binding Foo, ...}
inner = re.sub(
    r"\{Binding\s+DataContext\.(\w+),\s*ElementName=Root\s*,?\s*([^}]*)\}",
    r"{Binding \1\2}",
    inner,
)
# Clean up any trailing ", " left in binding
inner = inner.replace(", }", "}")

# Build the UserControl
header = '''<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="600"
             xmlns:vm="using:JoinCode.Gui.ViewModels"
             xmlns:cnv="using:JoinCode.Gui.Converters"
             xmlns:md="using:JoinCode.Gui.Markdown"
             xmlns:local="using:JoinCode.Gui.Views"
             xmlns:design="using:JoinCode.Gui.Design"
             x:Class="JoinCode.Gui.Views.MessageAreaView"
             d:DataContext="{x:Static design:DesignData.Sample}"
             x:DataType="vm:MainViewModel"
             DragDrop.DragOver="OnPanelDragOver" DragDrop.Drop="OnPanelDropMainArea">
'''

footer = "\n</UserControl>\n"

with open(DST, "w", encoding="utf-8") as f:
    f.write(header)
    f.write(inner)
    f.write(footer)

print(f"Written {DST}")

# Verify no ElementName=Root remains
remaining = re.findall(r"ElementName=Root", inner)
if remaining:
    print(f"WARNING: {len(remaining)} ElementName=Root references remain!", file=sys.stderr)
else:
    print("OK: No ElementName=Root references remain.")
