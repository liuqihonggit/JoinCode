using Infrastructure.IO.Services.FileOps;
using IO.FileSystem;

namespace Infrastructure.Tests.Services;

public sealed class NotebookReaderTests
{
    private static readonly IFileSystem Fs = TestFileSystem.Current;

    [Fact]
    public void IsNotebookExtension_IpynbFile_ReturnsTrue()
    {
        NotebookReader.IsNotebookExtension("notebook.ipynb").Should().BeTrue();
    }

    [Fact]
    public void IsNotebookExtension_UpperCase_ReturnsTrue()
    {
        NotebookReader.IsNotebookExtension("NOTEBOOK.IPYNB").Should().BeTrue();
    }

    [Fact]
    public void IsNotebookExtension_NonNotebookFile_ReturnsFalse()
    {
        NotebookReader.IsNotebookExtension("script.py").Should().BeFalse();
    }

    [Fact]
    public void IsNotebookExtension_NoExtension_ReturnsFalse()
    {
        NotebookReader.IsNotebookExtension("notebook").Should().BeFalse();
    }

    [Fact]
    public async Task ReadNotebookAsync_NonExistentFile_ReturnsError()
    {
        var result = await NotebookReader.ReadNotebookAsync(
            $"/test/nonexistent_{Guid.NewGuid()}.ipynb", Fs).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Notebook file not found:");
    }

    [Fact]
    public async Task ReadNotebookAsync_InvalidJson_ReturnsError()
    {
        var path = $"/test/invalid_{Guid.NewGuid():N}.ipynb";
        await Fs.WriteAllTextAsync(path, "not valid json").ConfigureAwait(true);
        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Failed to parse notebook:");
    }

    [Fact]
    public async Task ReadNotebookAsync_ValidNotebook_ReturnsFormattedContent()
    {
        var path = $"/test/valid_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {
                "language_info": { "name": "python" }
            },
            "cells": [
                {
                    "id": "cell-1",
                    "cell_type": "code",
                    "source": ["print('hello')"],
                    "metadata": {},
                    "outputs": []
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("<cell id=\"cell-1\">");
        result.Text.Should().Contain("print('hello')");
    }

    [Fact]
    public async Task ReadNotebookAsync_MarkdownCell_IncludesCellType()
    {
        var path = $"/test/md_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "id": "md-1",
                    "cell_type": "markdown",
                    "source": ["# Title"],
                    "metadata": {}
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("<cell_type>markdown</cell_type>");
        result.Text.Should().Contain("# Title");
    }

    [Fact]
    public async Task ReadNotebookAsync_CodeCellWithOutput_IncludesOutput()
    {
        var path = $"/test/codeout_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": { "language_info": { "name": "python" } },
            "cells": [
                {
                    "id": "code-1",
                    "cell_type": "code",
                    "source": ["print('hello')"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "stream",
                            "name": "stdout",
                            "text": ["hello\n"]
                        }
                    ]
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("hello");
    }

    [Fact]
    public async Task ReadNotebookAsync_ErrorOutput_IncludesErrorInfo()
    {
        var path = $"/test/err_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "id": "err-1",
                    "cell_type": "code",
                    "source": ["1/0"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "error",
                            "ename": "ZeroDivisionError",
                            "evalue": "division by zero",
                            "traceback": ["ZeroDivisionError: division by zero"]
                        }
                    ]
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("ZeroDivisionError");
        result.Text.Should().Contain("division by zero");
    }

    [Fact]
    public async Task ReadNotebookAsync_NonPythonLanguage_IncludesLanguageTag()
    {
        var path = $"/test/lang_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": { "language_info": { "name": "javascript" } },
            "cells": [
                {
                    "id": "js-1",
                    "cell_type": "code",
                    "source": ["console.log('hi')"],
                    "metadata": {},
                    "outputs": []
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("<language>javascript</language>");
    }

    [Fact]
    public async Task ReadNotebookAsync_CellWithoutId_UsesIndexAsId()
    {
        var path = $"/test/noid_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "cell_type": "code",
                    "source": ["x = 1"],
                    "metadata": {}
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("<cell id=\"cell-0\">");
    }

    [Fact]
    public async Task ReadNotebookAsync_EmptyCells_ReturnsEmptyContent()
    {
        var path = $"/test/empty_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": []
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text!.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task ReadNotebookAsync_ExecuteResultOutput_IncludesText()
    {
        var path = $"/test/exec_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "id": "exec-1",
                    "cell_type": "code",
                    "source": ["2 + 2"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "execute_result",
                            "data": { "text/plain": "4" },
                            "execution_count": 1
                        }
                    ]
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("4");
    }

    [Fact]
    public async Task ReadNotebookAsync_ImagePngOutput_ExtractsImage()
    {
        var path = $"/test/imgpng_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "id": "img-1",
                    "cell_type": "code",
                    "source": ["plt.plot()"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "display_data",
                            "data": {
                                "text/plain": "Figure",
                                "image/png": "aW1hZ2Ug\nZGF0YQ=="
                            }
                        }
                    ]
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Images.Should().NotBeNull();
        result.Images.Should().HaveCount(1);
        result.Images![0].MediaType.Should().Be("image/png");
        // 对齐 TS: 去除空白字符
        result.Images![0].Base64Data.Should().NotContain("\n");
        result.Images![0].Base64Data.Should().Be("aW1hZ2UgZGF0YQ==");
    }

    [Fact]
    public async Task ReadNotebookAsync_ImageJpegOutput_ExtractsImage()
    {
        var path = $"/test/imgjpg_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "id": "img-2",
                    "cell_type": "code",
                    "source": ["display(img)"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "execute_result",
                            "data": {
                                "text/plain": "JPEG",
                                "image/jpeg": "anBlZw=="
                            }
                        }
                    ]
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Images.Should().NotBeNull();
        result.Images.Should().HaveCount(1);
        result.Images![0].MediaType.Should().Be("image/jpeg");
        result.Images![0].Base64Data.Should().Be("anBlZw==");
    }

    [Fact]
    public async Task ReadNotebookAsync_MultipleImageOutputs_ExtractsAllImages()
    {
        var path = $"/test/multi_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "id": "multi-1",
                    "cell_type": "code",
                    "source": ["fig1()"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "display_data",
                            "data": { "image/png": "aW1nMQ==" }
                        }
                    ]
                },
                {
                    "id": "multi-2",
                    "cell_type": "code",
                    "source": ["fig2()"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "display_data",
                            "data": { "image/png": "aW1nMg==" }
                        }
                    ]
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Images.Should().NotBeNull();
        result.Images.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadNotebookAsync_StreamOutput_NoImageExtracted()
    {
        var path = $"/test/stream_{Guid.NewGuid():N}.ipynb";
        var json = """
        {
            "nbformat": 4,
            "nbformat_minor": 5,
            "metadata": {},
            "cells": [
                {
                    "id": "stream-1",
                    "cell_type": "code",
                    "source": ["print('hi')"],
                    "metadata": {},
                    "outputs": [
                        {
                            "output_type": "stream",
                            "name": "stdout",
                            "text": ["hi\n"]
                        }
                    ]
                }
            ]
        }
        """;
        await Fs.WriteAllTextAsync(path, json).ConfigureAwait(true);

        var result = await NotebookReader.ReadNotebookAsync(path, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Images.Should().BeNullOrEmpty();
    }
}
