#!/usr/bin/env python3
"""Extract MessageAreaView.axaml from MainWindow.axaml (lines 213-545).

Keeps ElementName=Root bindings (UserControl is named Root).
Adds x:Name="Root" to UserControl so ElementName=Root resolves to it.
"""
import re
import sys

SRC = r"D:\project\w1\app\gui\views\MainWindow.axaml"
DST = r"D:\project\w1\app\gui\views\MessageAreaView.axaml"

with open(SRC, encoding="utf-8") as f:
    lines = f.readlines()

# The MainWindow.axaml was already modified — the original content is gone.
# We need to re-extract from the MessageAreaView.axaml that was already created
# (with converted bindings) and restore the ElementName=Root pattern.

with open(DST, encoding="utf-8") as f:
    content = f.read()

# Restore ElementName=Root bindings:
#   {Binding Foo} → {Binding DataContext.Foo, ElementName=Root}
#   {Binding Foo, ...} → {Binding DataContext.Foo, ElementName=Root, ...}
# Only for specific command/property names that were originally on MainViewModel
commands_to_restore = [
    "UseSuggestionCommand",
    "CopyMessageCommand",
    "RewindTurnAtCommand",
    "RegenerateLastReplyCommand",
    "CanRegenerate",
    "ToggleThinkingCommand",
    "OpenWorktreeInExplorerCommand",
    "OpenAgentTranscriptCommand",
    "TogglePromptCommand",
    "OpenDiffViewCommand",
    "FontSize",
]

for cmd in commands_to_restore:
    # {Binding Cmd} → {Binding DataContext.Cmd, ElementName=Root}
    content = re.sub(
        r"\{Binding\s+" + cmd + r"\}",
        r"{Binding DataContext." + cmd + r", ElementName=Root}",
        content,
    )
    # {Binding Cmd, ...} → {Binding DataContext.Cmd, ElementName=Root, ...}
    content = re.sub(
        r"\{Binding\s+" + cmd + r",\s*([^}]+)\}",
        r"{Binding DataContext." + cmd + r", ElementName=Root, \1}",
        content,
    )

# Add x:Name="Root" to the UserControl if not already present
if 'x:Name="Root"' not in content:
    content = content.replace(
        'x:Class="JoinCode.Gui.Views.MessageAreaView"',
        'x:Class="JoinCode.Gui.Views.MessageAreaView"\n             x:Name="Root"',
    )

with open(DST, "w", encoding="utf-8") as f:
    f.write(content)

print(f"Updated {DST}")

# Verify
remaining_direct = []
for cmd in commands_to_restore:
    if f"{{Binding {cmd}}}" in content or f"{{Binding {cmd}," in content:
        remaining_direct.append(cmd)
if remaining_direct:
    print(f"WARNING: Direct bindings remain for: {remaining_direct}", file=sys.stderr)
else:
    print("OK: All command bindings use ElementName=Root.")
