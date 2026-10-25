namespace CellAssistant.UnitTests.Watcher;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure.Watcher;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class DebounceSlidingWindowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IDocumentIndexer _indexer = Substitute.For<IDocumentIndexer>();

    public DebounceSlidingWindowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "watcher_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "Equipment"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ProcessRawEvent_SingleChange_InvokesIndexFileAsyncOnce()
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempDir,
            WatcherDebounceMs = 50
        });

        using var service = new DebouncedFileWatcherService(options, _indexer, NullLogger<DebouncedFileWatcherService>.Instance);
        await service.StartAsync();

        string file = Path.Combine(_tempDir, "Equipment", "robot.md");
        await File.WriteAllTextAsync(file, "# Robot Note");

        var tcs = new TaskCompletionSource<bool>();
        service.ProcessRawEvent(WatcherChangeTypes.Changed, file, tcs: tcs);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task);
        (await tcs.Task).Should().BeTrue();

        await _indexer.Received(1).IndexFileAsync(file, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessRawEvent_RapidBurstOfChanges_CollapsesIntoSingleIndexCall()
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempDir,
            WatcherDebounceMs = 100
        });

        using var service = new DebouncedFileWatcherService(options, _indexer, NullLogger<DebouncedFileWatcherService>.Instance);
        await service.StartAsync();

        string file = Path.Combine(_tempDir, "Equipment", "plc.md");
        await File.WriteAllTextAsync(file, "# PLC Note");

        TaskCompletionSource<bool>? finalTcs = null;
        for (int i = 0; i < 5; i++)
        {
            finalTcs = new TaskCompletionSource<bool>();
            service.ProcessRawEvent(WatcherChangeTypes.Changed, file, tcs: finalTcs);
            await Task.Delay(20); // Fire inside sliding window
        }

        var completed = await Task.WhenAny(finalTcs!.Task, Task.Delay(3000));
        completed.Should().Be(finalTcs.Task);

        await _indexer.Received(1).IndexFileAsync(file, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessRawEvent_DeleteEvent_InvokesRemoveFileAsync()
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempDir,
            WatcherDebounceMs = 50
        });

        using var service = new DebouncedFileWatcherService(options, _indexer, NullLogger<DebouncedFileWatcherService>.Instance);
        await service.StartAsync();

        string file = Path.Combine(_tempDir, "Equipment", "deleted.md");
        var tcs = new TaskCompletionSource<bool>();
        service.ProcessRawEvent(WatcherChangeTypes.Deleted, file, tcs: tcs);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task);

        await _indexer.Received(1).RemoveFileAsync(file, Arg.Any<CancellationToken>());
    }
}
