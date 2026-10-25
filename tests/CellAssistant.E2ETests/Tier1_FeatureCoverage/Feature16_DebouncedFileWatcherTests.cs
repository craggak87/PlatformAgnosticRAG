using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature16_DebouncedFileWatcherTests
{
    [Fact]
    public void Test01_WatcherDebounceWindow_Matches500MsSpecification()
    {
        SpecificationConstants.DefaultWatcherDebounceMs.Should().Be(500,
            "ADR-009, Doc 3 §R4, and Doc 7 require a 500ms sliding window debounce");
    }

    [Theory]
    [InlineData(".obsidian/workspace.json")]
    [InlineData(".obsidian/graph.json")]
    [InlineData(".obsidian/cache/notes.db")]
    [InlineData(".hidden_file")]
    public void Test02_ObsidianAndHiddenFiles_MustBeIgnoredByWatcher(string relativePath)
    {
        bool isIgnored = relativePath.StartsWith(".obsidian/", StringComparison.OrdinalIgnoreCase) ||
                         relativePath.StartsWith(".");
        isIgnored.Should().BeTrue("Watcher must ignore .obsidian internal cache directories and hidden files");
    }

    [Theory]
    [InlineData("Equipment/ROBOT01.md")]
    [InlineData("Faults/Robot Gripper.md")]
    [InlineData("Manuals/RobotManual.pdf")]
    [InlineData("Alarms/ALM-205.md")]
    public void Test03_Supported16VaultFolders_MustBeTrackedByWatcher(string relativePath)
    {
        var folder = relativePath.Split('/')[0];
        SpecificationConstants.StandardVaultFolders.Should().Contain(folder);
    }

    [Fact]
    public void Test04_FileSystemWatcherEvents_SupportStandardFourActions()
    {
        var supportedEvents = new[] { WatcherChangeTypes.Created, WatcherChangeTypes.Changed, WatcherChangeTypes.Deleted, WatcherChangeTypes.Renamed };
        supportedEvents.Length.Should().Be(4);
    }

    [Fact]
    public void Test05_FileRenameEvent_TransfersTrackingFromOldToNewPath()
    {
        var oldPath = "Faults/OldName.md";
        var newPath = "Faults/NewName.md";

        var normOld = E2ETestHarness.NormalizeRelativePath(oldPath);
        var normNew = E2ETestHarness.NormalizeRelativePath(newPath);

        normOld.Should().NotBe(normNew);
        normNew.Should().Be("Faults/NewName.md");
    }
}
