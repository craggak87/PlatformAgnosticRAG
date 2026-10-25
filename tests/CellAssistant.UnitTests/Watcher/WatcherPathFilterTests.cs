namespace CellAssistant.UnitTests.Watcher;

using System.IO;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure.Watcher;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class WatcherPathFilterTests
{
    private readonly string _rootPath = Path.GetFullPath("/test/vault");
    private readonly DebouncedFileWatcherService _service;

    public WatcherPathFilterTests()
    {
        var options = Options.Create(new KnowledgeBaseOptions { RootPath = _rootPath });
        var indexer = Substitute.For<IDocumentIndexer>();
        _service = new DebouncedFileWatcherService(options, indexer, NullLogger<DebouncedFileWatcherService>.Instance);
    }

    [Theory]
    [InlineData("Equipment/ROBOT01.md", true)]
    [InlineData("Faults/FLT-001.md", true)]
    [InlineData("Manuals/RobotManual.pdf", true)]
    [InlineData("Maintenance/log.txt", true)]
    [InlineData("overview.md", true)]
    [InlineData("Equipment/SubFolder/Robot.md", true)]
    [InlineData("Equipment/UPPERCASE.MD", true)]
    public void IsPathMonitored_ForValidVaultFiles_ReturnsTrue(string relativePath, bool expected)
    {
        string fullPath = Path.Combine(_rootPath, relativePath);
        _service.IsPathMonitored(fullPath).Should().Be(expected);
    }

    [Theory]
    [InlineData(".obsidian/workspace.json", false)]
    [InlineData(".obsidian/plugins/dataview/main.js", false)]
    [InlineData(".obsidian/note.md", false)]
    [InlineData(".git/config", false)]
    [InlineData(".hidden_note.md", false)]
    [InlineData("Equipment/.DS_Store", false)]
    [InlineData("Faults/.trash/FLT.md", false)]
    [InlineData("Equipment/Note.md~", false)]
    [InlineData("Equipment/~$Manual.pdf", false)]
    [InlineData("Equipment/temp.tmp", false)]
    [InlineData("Equipment/diagram.png", false)]
    [InlineData("Equipment/code.cs", false)]
    [InlineData("Drafts/Unfinished.md", false)]
    [InlineData("Archive/OldNotes.md", false)]
    public void IsPathMonitored_ForIgnoredFiles_ReturnsFalse(string relativePath, bool expected)
    {
        string fullPath = Path.Combine(_rootPath, relativePath);
        _service.IsPathMonitored(fullPath).Should().Be(expected);
    }
}
