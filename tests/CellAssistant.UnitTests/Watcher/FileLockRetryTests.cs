namespace CellAssistant.UnitTests.Watcher;

using System;
using System.IO;
using System.Threading.Tasks;
using CellAssistant.Infrastructure.Watcher;
using FluentAssertions;
using Xunit;

public class FileLockRetryTests
{
    [Fact]
    public async Task WaitForFileAvailableAsync_WhenFileNotLocked_ReturnsTrueImmediately()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "sample content");
            bool result = await DebouncedFileWatcherService.WaitForFileAvailableAsync(
                tempFile, maxRetries: 3, initialDelayMs: 10);

            result.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WaitForFileAvailableAsync_WhenFileReleasedDuringRetry_ReturnsTrue()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "locked content");

            var fileLockTask = Task.Run(async () =>
            {
                using var fs = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                await Task.Delay(50);
            });

            bool result = await DebouncedFileWatcherService.WaitForFileAvailableAsync(
                tempFile, maxRetries: 5, initialDelayMs: 20);

            result.Should().BeTrue();
            await fileLockTask;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WaitForFileAvailableAsync_WhenFilePermanentlyLocked_ReturnsFalseAfterMaxRetries()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "locked content");

            using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool result = await DebouncedFileWatcherService.WaitForFileAvailableAsync(
                    tempFile, maxRetries: 3, initialDelayMs: 10);

                result.Should().BeFalse();
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
