#!/usr/bin/env python3
"""Remove PanelView and InputBar from MessageAreaView — they are now Dock tools.

Simplifies the layout from:
  Grid[*,Auto,Auto] > Grid[Auto,*,Auto|Auto,*,Auto] > PanelView*4 + content + sash*4 + SlashPalette + InputBar
to:
  Grid[*,Auto] > content + SlashPalette
"""
import re

SRC = r"D:\project\w1\app\gui\views\MessageAreaView.axaml"

with open(SRC, encoding="utf-8") as f:
    lines = f.readlines()

# Build the new content:
# 1. Keep lines 1-15 (UserControl header) but change RowDefinitions
# 2. Replace lines 16-19 (outer Grid + inner Grid with panels) with simple Grid
# 3. Keep lines 25-304 (main content Panel with message list + editor)
# 4. Skip lines 305-339 (PanelView right/bottom + sashes + closing Grid)
# 5. Keep lines 340-344 (slash palette)
# 6. Skip lines 345-348 (InputBar + closing Grid)
# 7. Keep line 349-350 (closing UserControl)

header = lines[:15]  # lines 1-15 (UserControl tag)

# New outer Grid with simplified RowDefinitions
grid_open = '      <Grid RowDefinitions="*,Auto">\n'

# Main content: lines 25-304 (the Panel with message list + editor)
# But we need to adjust indentation — remove the Grid.Row="0" wrapper
# Actually, let's keep the Panel but put it directly in the Grid
content = "".join(lines[24:304])  # lines 25-304 (0-indexed 24-303)

# Slash palette: lines 341-344
slash = "".join(lines[340:344])  # lines 341-344 (0-indexed 340-343)

grid_close = '      </Grid>\n'
usercontrol_close = '</UserControl>\n'

result = "".join(header) + grid_open + content + slash + grid_close + "\n" + usercontrol_close

with open(SRC, "w", encoding="utf-8") as f:
    f.write(result)

print(f"Updated {SRC}")
print(f"Old lines: {len(lines)}")
print(f"New lines: {result.count(chr(10))}")
