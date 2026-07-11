
namespace Services.Notebook;

/// <summary>
/// Notebook服务实现
/// </summary>
[Register]
public sealed partial class NotebookService : INotebookService
{
    [Inject] private readonly IFileOperationService _fileOperationService;
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly IFileHistoryService? _fileHistoryService;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />
    public async Task<NotebookDocument?> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            RecordNotebookMetrics("load", false);
            return null;
        }

        var doc = JsonSerializer.Deserialize(result.Content, NotebookDocumentJsonContext.Default.NotebookDocument);
        RecordNotebookMetrics("load", doc != null);
        return doc;
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(string filePath, NotebookDocument notebook, CancellationToken cancellationToken = default)
    {
        try
        {
            // 对齐 TS: fileHistoryTrackEdit — 编辑前创建备份
            if (_fileHistoryService is not null && _fs.FileExists(filePath))
            {
                await _fileHistoryService.BackupBeforeWriteAsync(filePath, cancellationToken).ConfigureAwait(false);
            }

            // 对齐 TS: IPYNB_INDENT = 1，使用 1 空格缩进写回 notebook
            var options = new JsonSerializerOptions(NotebookDocumentJsonContext.Default.Options)
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var context = new NotebookDocumentJsonContext(options);
            var json = JsonSerializer.Serialize(notebook, context.NotebookDocument);

            // 对齐 TS: readFileSyncWithMetadata + writeTextContent — 保持原始编码和换行符
            if (_fs.FileExists(filePath))
            {
                var metadata = await _fileOperationService.ReadFileWithMetadataAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (metadata.Success)
                {
                    var writeResult = await _fileOperationService.WriteFileWithEncodingAsync(
                        filePath, json, metadata.Encoding, metadata.LineEndings, cancellationToken).ConfigureAwait(false);
                    RecordNotebookMetrics("save", writeResult.Success);
                    return writeResult.Success;
                }
            }

            // 新文件：默认 UTF-8 + LF
            var result = await _fileOperationService.WriteFileAsync(filePath, json, cancellationToken).ConfigureAwait(false);
            RecordNotebookMetrics("save", result.Success);
            return result.Success;
        }
        catch
        {
            RecordNotebookMetrics("save", false);
            return false;
        }
    }

    /// <inheritdoc />
    public NotebookDocument Create(string? kernelName = null, string? language = null)
    {
        return new NotebookDocument
        {
            NbFormat = 4,
            NbFormatMinor = 5,
            Metadata = new NotebookMetadata
            {
                KernelSpec = !string.IsNullOrEmpty(kernelName) ? new KernelSpec
                {
                    DisplayName = kernelName,
                    Language = language ?? "python",
                    Name = kernelName.ToLowerInvariant()
                } : null,
                LanguageInfo = !string.IsNullOrEmpty(language) ? new LanguageInfo
                {
                    Name = language,
                    MimeType = $"text/x-{language}",
                    FileExtension = ".py"
                } : null
            },
            Cells = new List<NotebookCell>()
        };
    }

    /// <inheritdoc />
    public NotebookEditResult AddCell(NotebookDocument notebook, NotebookCellType cellType, string content, int? index = null)
    {
        var cell = new NotebookCell
        {
            // 对齐 TS: nbformat >= 4.5 时自动生成随机 cell ID
            Id = ShouldGenerateCellId(notebook) ? GenerateCellId() : null,
            CellType = cellType.ToCellTypeString(),
            Source = SplitWithNewlines(content),
            Metadata = new Dictionary<string, JsonElement>(),
            Outputs = cellType == NotebookCellType.Code ? new List<NotebookOutput>() : null,
            ExecutionCount = null
        };

        var insertIndex = index ?? notebook.Cells.Count;
        if (insertIndex < 0) insertIndex = 0;
        if (insertIndex > notebook.Cells.Count) insertIndex = notebook.Cells.Count;

        notebook.Cells.Insert(insertIndex, cell);

        return new NotebookEditResult
        {
            Success = true,
            Notebook = notebook,
            AffectedCellIndex = insertIndex
        };
    }

    /// <inheritdoc />
    public NotebookEditResult DeleteCell(NotebookDocument notebook, int index)
    {
        if (index < 0 || index >= notebook.Cells.Count)
        {
            return new NotebookEditResult
            {
                Success = false,
                ErrorMessage = $"无效的单元格索引: {index}"
            };
        }

        notebook.Cells.RemoveAt(index);

        return new NotebookEditResult
        {
            Success = true,
            Notebook = notebook
        };
    }

    /// <inheritdoc />
    public NotebookEditResult EditCell(NotebookDocument notebook, int index, string newContent, string? newCellType = null)
    {
        if (index < 0 || index >= notebook.Cells.Count)
        {
            return new NotebookEditResult
            {
                Success = false,
                ErrorMessage = $"无效的单元格索引: {index}"
            };
        }

        var cell = notebook.Cells[index];
        var lines = SplitWithNewlines(newContent);

        // 对齐 TS: replace 模式下支持修改 cell_type
        var resolvedCellType = !string.IsNullOrEmpty(newCellType) ? newCellType : cell.CellType;
        var isCodeCell = string.Equals(resolvedCellType, NotebookCellTypeConstants.Code, StringComparison.OrdinalIgnoreCase);

        // 编辑代码单元格后重置 execution_count 和清空 outputs，防止显示过时的执行结果
        notebook.Cells[index] = cell with
        {
            CellType = resolvedCellType,
            Source = lines,
            ExecutionCount = isCodeCell ? null : cell.ExecutionCount,
            Outputs = isCodeCell ? [] : cell.Outputs
        };

        return new NotebookEditResult
        {
            Success = true,
            Notebook = notebook,
            AffectedCellIndex = index
        };
    }

    /// <inheritdoc />
    public NotebookEditResult MoveCell(NotebookDocument notebook, int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= notebook.Cells.Count)
        {
            return new NotebookEditResult
            {
                Success = false,
                ErrorMessage = $"无效的源索引: {fromIndex}"
            };
        }

        if (toIndex < 0 || toIndex > notebook.Cells.Count)
        {
            return new NotebookEditResult
            {
                Success = false,
                ErrorMessage = $"无效的目标索引: {toIndex}"
            };
        }

        var cell = notebook.Cells[fromIndex];
        notebook.Cells.RemoveAt(fromIndex);

        // 调整插入位置（如果目标位置在删除位置之后）
        if (toIndex > fromIndex) toIndex--;

        notebook.Cells.Insert(toIndex, cell);

        return new NotebookEditResult
        {
            Success = true,
            Notebook = notebook,
            AffectedCellIndex = toIndex
        };
    }

    /// <inheritdoc />
    public NotebookEditResult ChangeCellType(NotebookDocument notebook, int index, NotebookCellType newType)
    {
        if (index < 0 || index >= notebook.Cells.Count)
        {
            return new NotebookEditResult
            {
                Success = false,
                ErrorMessage = $"无效的单元格索引: {index}"
            };
        }

        var cell = notebook.Cells[index];
        var newCellType = newType.ToCellTypeString();

        // 如果类型相同，不做任何操作
        if (cell.CellType == newCellType)
        {
            return new NotebookEditResult
            {
                Success = true,
                Notebook = notebook,
                AffectedCellIndex = index
            };
        }

        // 创建新的cell，保留内容但更改类型
        notebook.Cells[index] = cell with
        {
            CellType = newCellType,
            Outputs = newType == NotebookCellType.Code ? new List<NotebookOutput>() : null,
            ExecutionCount = null
        };

        return new NotebookEditResult
        {
            Success = true,
            Notebook = notebook,
            AffectedCellIndex = index
        };
    }

    /// <inheritdoc />
    public NotebookEditResult ExecuteCell(NotebookDocument notebook, int index, string? output = null)
    {
        if (index < 0 || index >= notebook.Cells.Count)
        {
            return new NotebookEditResult
            {
                Success = false,
                ErrorMessage = $"无效的单元格索引: {index}"
            };
        }

        var cell = notebook.Cells[index];

        // 只有代码单元格可以执行
        if (cell.Type != NotebookCellType.Code)
        {
            return new NotebookEditResult
            {
                Success = false,
                ErrorMessage = "只有代码单元格可以执行"
            };
        }

        var executionCount = notebook.Cells.OfType<NotebookCell>().Max(c => c.ExecutionCount ?? 0) + 1;

        var outputs = new List<NotebookOutput>();
        if (!string.IsNullOrEmpty(output))
        {
            outputs.Add(new NotebookOutput
            {
                OutputType = "stream",
                Name = "stdout",
                Text = SplitWithNewlines(output)
            });
        }

        notebook.Cells[index] = cell with
        {
            ExecutionCount = executionCount,
            Outputs = outputs
        };

        return new NotebookEditResult
        {
            Success = true,
            Notebook = notebook,
            AffectedCellIndex = index
        };
    }

    /// <inheritdoc />
    public NotebookEditResult ClearAllOutputs(NotebookDocument notebook)
    {
        for (int i = 0; i < notebook.Cells.Count; i++)
        {
            var cell = notebook.Cells[i];
            if (cell.Type == NotebookCellType.Code)
            {
                notebook.Cells[i] = cell with
                {
                    Outputs = new List<NotebookOutput>(),
                    ExecutionCount = null
                };
            }
        }

        return new NotebookEditResult
        {
            Success = true,
            Notebook = notebook
        };
    }

    /// <inheritdoc />
    public string? GetCellContent(NotebookDocument notebook, int index)
    {
        if (index < 0 || index >= notebook.Cells.Count)
        {
            return null;
        }

        return notebook.Cells[index].SourceText;
    }

    /// <inheritdoc />
    public List<(int Index, NotebookCellType Type, string Preview)> ListCells(NotebookDocument notebook)
    {
        return notebook.Cells.Select((cell, index) =>
        {
            var preview = cell.SourceText.Length > 50
                ? cell.SourceText[..50] + "..."
                : cell.SourceText;

            return (index, cell.Type, preview.Replace("\n", " "));
        }).ToList();
    }

    private void RecordNotebookMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("notebook.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Notebook operation count");

    private static List<string> SplitWithNewlines(string content)
    {
        var lines = content.Split('\n');
        return lines.Select((line, i) => i < lines.Length - 1 ? line + "\n" : line).ToList();
    }

    /// <summary>
    /// 对齐 TS: nbformat >= 4.5 时需要为每个 cell 生成唯一 ID
    /// </summary>
    private static bool ShouldGenerateCellId(NotebookDocument notebook)
        => notebook.NbFormat > 4 || (notebook.NbFormat == 4 && notebook.NbFormatMinor >= 5);

    /// <summary>
    /// 对齐 TS: 生成随机 cell ID (Math.random().toString(36).substring(2, 15))
    /// </summary>
    private static string GenerateCellId()
    {
        // 使用 Random.Shared 生成类似 TS 的 base36 随机 ID
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
        var span = new char[13];
        for (int i = 0; i < span.Length; i++)
            span[i] = chars[Random.Shared.Next(chars.Length)];
        return new string(span);
    }
}
