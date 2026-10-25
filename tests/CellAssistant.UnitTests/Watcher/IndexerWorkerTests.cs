namespace CellAssistant.UnitTests.Watcher;

using System;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using CellAssistant.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public class IndexerWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_RunsInitialization_ThenIndexAll_ThenStartsWatcher()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);

        var sqliteInit = Substitute.For<ISqliteDatabaseInitializer>();
        var vectorStore = Substitute.For<IVectorStore>();
        var indexer = Substitute.For<IDocumentIndexer>();
        var watcher = Substitute.For<IFileWatcherService>();

        serviceProvider.GetService(typeof(ISqliteDatabaseInitializer)).Returns(sqliteInit);
        serviceProvider.GetService(typeof(IVectorStore)).Returns(vectorStore);
        serviceProvider.GetService(typeof(IDocumentIndexer)).Returns(indexer);

        indexer.IndexAllAsync(Arg.Any<CancellationToken>())
            .Returns(new IndexerRunResult(
                TotalFilesScanned: 10,
                FilesIndexed: 2,
                FilesSkippedUnchanged: 8,
                FilesDeleted: 0,
                ErrorsCount: 0,
                Duration: TimeSpan.FromMilliseconds(150),
                Success: true));

        var worker = new IndexerWorker(scopeFactory, watcher, NullLogger<IndexerWorker>.Instance);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(150); // Cancel shortly after start

        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        await sqliteInit.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        await vectorStore.Received(1).InitializeCollectionAsync(Arg.Any<CancellationToken>());
        await indexer.Received(1).IndexAllAsync(Arg.Any<CancellationToken>());
        await watcher.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await watcher.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }
}
