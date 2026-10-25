using System.Reflection;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary02_CleanArchitectureBoundaryTests
{
    [Fact]
    public void Test01_LoadedAssemblies_DoNotContainCloudAiNamespaces()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in loaded)
        {
            var name = asm.GetName().Name ?? "";
            foreach (var prohibited in SpecificationConstants.ProhibitedCloudPackages)
            {
                name.Should().NotContain(prohibited, "ADR-002 prohibits cloud AI dependencies in the entire process space");
            }
        }
    }

    [Fact]
    public void Test02_DisallowedTelemetryAndAnalytics_AreExcluded()
    {
        var disallowed = new[] { "Microsoft.ApplicationInsights", "OpenTelemetry.Exporter.OpenTelemetryProtocol", "Segment" };
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "");

        foreach (var bad in disallowed)
        {
            loaded.Should().NotContain(bad);
        }
    }

    [Fact]
    public void Test03_DomainContracts_DoNotDependOnAspNetCoreOrEfCore()
    {
        var forbiddenDomainDeps = new[] { "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore" };
        var loadedDomain = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "CellAssistant.Domain");

        if (loadedDomain != null)
        {
            var referenced = loadedDomain.GetReferencedAssemblies().Select(r => r.Name ?? "");
            foreach (var forbidden in forbiddenDomainDeps)
            {
                referenced.Should().NotContain(forbidden, "Domain layer must have 0 framework dependencies");
            }
        }
    }

    [Fact]
    public void Test04_CleanArchitecture_BoundaryBetweenWorkerAndApiIsIsolated()
    {
        // Worker and Api must be independent executable hosts
        var apiAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "CellAssistant.Api");
        var workerAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "CellAssistant.Worker");

        if (apiAssembly != null && workerAssembly != null)
        {
            apiAssembly.GetReferencedAssemblies().Select(r => r.Name).Should().NotContain("CellAssistant.Worker");
            workerAssembly.GetReferencedAssemblies().Select(r => r.Name).Should().NotContain("CellAssistant.Api");
        }
    }

    [Fact]
    public void Test05_ApplicationLayer_HasZeroReferencesToSqliteOrQdrant()
    {
        var appAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "CellAssistant.Application");
        if (appAssembly != null)
        {
            var refs = appAssembly.GetReferencedAssemblies().Select(r => r.Name ?? "");
            refs.Should().NotContain("Microsoft.Data.Sqlite");
            refs.Should().NotContain("Qdrant.Client");
        }
    }
}
