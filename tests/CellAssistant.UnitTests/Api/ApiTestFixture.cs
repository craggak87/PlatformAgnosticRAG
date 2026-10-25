namespace CellAssistant.UnitTests.Api;

using System;
using CellAssistant.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

public class ApiTestFixture : WebApplicationFactory<Program>
{
    public IQueryService MockQueryService { get; } = Substitute.For<IQueryService>();
    public IDocumentRepository MockDocumentRepository { get; } = Substitute.For<IDocumentRepository>();
    public ISearchHistoryRepository MockSearchHistoryRepository { get; } = Substitute.For<ISearchHistoryRepository>();
    public IIndexerStateRepository MockIndexerStateRepository { get; } = Substitute.For<IIndexerStateRepository>();
    public IVectorStore MockVectorStore { get; } = Substitute.For<IVectorStore>();
    public IDocumentIndexer MockDocumentIndexer { get; } = Substitute.For<IDocumentIndexer>();
    public IHealthCheckService MockHealthCheckService { get; } = Substitute.For<IHealthCheckService>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQueryService>();
            services.AddSingleton(MockQueryService);

            services.RemoveAll<IDocumentRepository>();
            services.AddSingleton(MockDocumentRepository);

            services.RemoveAll<ISearchHistoryRepository>();
            services.AddSingleton(MockSearchHistoryRepository);

            services.RemoveAll<IIndexerStateRepository>();
            services.AddSingleton(MockIndexerStateRepository);

            services.RemoveAll<IVectorStore>();
            services.AddSingleton(MockVectorStore);

            services.RemoveAll<IDocumentIndexer>();
            services.AddSingleton(MockDocumentIndexer);

            services.RemoveAll<IHealthCheckService>();
            services.AddSingleton(MockHealthCheckService);
        });
    }
}
