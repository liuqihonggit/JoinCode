namespace JoinCode.Abstractions.LLM.Chat;

public static class ToolListDriftClassifier
{
    public static ToolDriftReport Classify(IReadOnlyList<ToolSpec> before, IReadOnlyList<ToolSpec> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (before.Count == 0 && after.Count == 0)
        {
            return new ToolDriftReport { Kind = ToolDriftKind.Identity, Summary = "Both empty" };
        }

        if (before.Count == 0)
        {
            return new ToolDriftReport
            {
                Kind = ToolDriftKind.Append,
                AddedNames = after.Select(a => a.Name).ToList(),
                Summary = $"All {after.Count} tools are new"
            };
        }

        var removedNames = new List<string>();
        var beforeByName = new Dictionary<string, ToolSpec>(before.Count);
        for (var i = 0; i < before.Count; i++)
        {
            beforeByName[before[i].Name] = before[i];
        }

        var afterByName = new Dictionary<string, ToolSpec>(after.Count);
        foreach (var a in after)
        {
            afterByName[a.Name] = a;
        }

        foreach (var b in before)
        {
            if (!afterByName.ContainsKey(b.Name))
            {
                removedNames.Add(b.Name);
            }
        }

        if (removedNames.Count > 0)
        {
            return new ToolDriftReport
            {
                Kind = ToolDriftKind.Remove,
                RemovedNames = removedNames,
                Summary = $"Removed: {string.Join(", ", removedNames)}"
            };
        }

        var addedNames = new List<string>();
        foreach (var a in after)
        {
            if (!beforeByName.ContainsKey(a.Name))
            {
                addedNames.Add(a.Name);
            }
        }

        if (addedNames.Count > 0)
        {
            if (IsAppendOnly(before, after, addedNames))
            {
                return new ToolDriftReport
                {
                    Kind = ToolDriftKind.Append,
                    AddedNames = addedNames,
                    Summary = $"Appended: {string.Join(", ", addedNames)}"
                };
            }

            return new ToolDriftReport
            {
                Kind = ToolDriftKind.Reorder,
                AddedNames = addedNames,
                Summary = $"Non-append addition with {addedNames.Count} new tools"
            };
        }

        var editedNames = new List<string>();
        var reorderedNames = new List<string>();
        var nameOrderChanged = false;

        for (var i = 0; i < before.Count; i++)
        {
            if (after[i].Name != before[i].Name)
            {
                nameOrderChanged = true;
                break;
            }
        }

        if (nameOrderChanged)
        {
            for (var i = 0; i < before.Count; i++)
            {
                if (after[i].Name != before[i].Name)
                {
                    reorderedNames.Add($"{before[i].Name}→{after[i].Name}");
                }
            }

            return new ToolDriftReport
            {
                Kind = ToolDriftKind.Reorder,
                ReorderedNames = reorderedNames,
                Summary = $"Reordered: {string.Join(", ", reorderedNames)}"
            };
        }

        for (var i = 0; i < before.Count; i++)
        {
            if (!ContentEquals(before[i], after[i]))
            {
                editedNames.Add(before[i].Name);
            }
        }

        if (editedNames.Count > 0)
        {
            return new ToolDriftReport
            {
                Kind = ToolDriftKind.Edit,
                EditedNames = editedNames,
                Summary = $"Edited: {string.Join(", ", editedNames)}"
            };
        }

        return new ToolDriftReport { Kind = ToolDriftKind.Identity, Summary = "No change" };
    }

    private static bool IsAppendOnly(IReadOnlyList<ToolSpec> before, IReadOnlyList<ToolSpec> after, IReadOnlyList<string> addedNames)
    {
        if (after.Count != before.Count + addedNames.Count)
        {
            return false;
        }

        for (var i = 0; i < before.Count; i++)
        {
            if (after[i].Name != before[i].Name || !ContentEquals(before[i], after[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContentEquals(ToolSpec a, ToolSpec b)
    {
        if (a.Name != b.Name) return false;
        if (a.Description != b.Description) return false;
        if (a.InputSchemaJson != b.InputSchemaJson) return false;
        return true;
    }
}
