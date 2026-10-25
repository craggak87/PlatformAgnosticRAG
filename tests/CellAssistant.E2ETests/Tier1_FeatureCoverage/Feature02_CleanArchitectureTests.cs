using System.Xml.Linq;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature02_CleanArchitectureTests
{
    private readonly string _repoRoot;

    public Feature02_CleanArchitectureTests()
    {
        // Resolve repository root relative to current execution
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "1.Cell_Knowledge_Assistant_ADR.md")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        _repoRoot = current ?? Directory.GetCurrentDirectory();
    }

    [Fact]
    public void Test01_ProhibitedCloudAiSdks_AreNeverReferencedInAnyCsproj()
    {
        var csprojFiles = Directory.GetFiles(_repoRoot, "*.csproj", SearchOption.AllDirectories);
        foreach (var file in csprojFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var prohibited in SpecificationConstants.ProhibitedCloudPackages)
            {
                content.Should().NotContain(prohibited,
                    $"ADR-002 and ADR-013 strictly forbid cloud AI SDK '{prohibited}' in '{file}'");
            }
        }
    }

    [Fact]
    public void Test02_DomainProject_IfPresent_MustHaveZeroExternalPackageDependencies()
    {
        var domainCsproj = Path.Combine(_repoRoot, "src", "CellAssistant.Domain", "CellAssistant.Domain.csproj");
        if (File.Exists(domainCsproj))
        {
            var xml = XDocument.Load(domainCsproj);
            var packageRefs = xml.Descendants("PackageReference").ToList();
            packageRefs.Should().BeEmpty("Domain layer must have zero external package dependencies per ADR-007");

            var projectRefs = xml.Descendants("ProjectReference").ToList();
            projectRefs.Should().BeEmpty("Domain layer must have zero project dependencies per ADR-007");
        }
    }

    [Fact]
    public void Test03_ApplicationProject_IfPresent_MustDependOnlyOnDomain()
    {
        var appCsproj = Path.Combine(_repoRoot, "src", "CellAssistant.Application", "CellAssistant.Application.csproj");
        if (File.Exists(appCsproj))
        {
            var xml = XDocument.Load(appCsproj);
            var projectRefs = xml.Descendants("ProjectReference")
                .Select(p => p.Attribute("Include")?.Value ?? "")
                .ToList();

            foreach (var pref in projectRefs)
            {
                pref.Should().Contain("CellAssistant.Domain", "Application may only depend on Domain per ADR-007");
                pref.Should().NotContain("Infrastructure");
                pref.Should().NotContain("Api");
                pref.Should().NotContain("Worker");
            }
        }
    }

    [Fact]
    public void Test04_InfrastructureProject_IfPresent_MustNotDependOnApiOrWorker()
    {
        var infraCsproj = Path.Combine(_repoRoot, "src", "CellAssistant.Infrastructure", "CellAssistant.Infrastructure.csproj");
        if (File.Exists(infraCsproj))
        {
            var xml = XDocument.Load(infraCsproj);
            var projectRefs = xml.Descendants("ProjectReference")
                .Select(p => p.Attribute("Include")?.Value ?? "")
                .ToList();

            foreach (var pref in projectRefs)
            {
                pref.Should().NotContain("CellAssistant.Api");
                pref.Should().NotContain("CellAssistant.Worker");
            }
        }
    }

    [Fact]
    public void Test05_CodeLayout_AdheresToCleanArchitectureDirectoryTree()
    {
        // Directory layout contract from PROJECT.md § Code Layout
        var projectMd = Path.Combine(_repoRoot, ".agents", "teamwork", "PROJECT.md");
        if (File.Exists(projectMd))
        {
            var content = File.ReadAllText(projectMd);
            content.Should().Contain("CellAssistant.Domain");
            content.Should().Contain("CellAssistant.Application");
            content.Should().Contain("CellAssistant.Infrastructure");
            content.Should().Contain("CellAssistant.Api");
            content.Should().Contain("CellAssistant.Worker");
        }

        // If src directory has been created by implementation track, verify standard structure
        var srcDir = Path.Combine(_repoRoot, "src");
        if (Directory.Exists(srcDir))
        {
            Directory.Exists(Path.Combine(_repoRoot, "tests")).Should().BeTrue();
        }
    }
}
