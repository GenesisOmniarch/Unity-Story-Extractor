using FluentAssertions;
using UnityStoryExtractor.Core.Extractor;
using UnityStoryExtractor.Core.Models;
using Xunit;

namespace UnityStoryExtractor.Tests.Unit;

/// <summary>
/// 抽出器のユニットテスト
/// </summary>
public class ExtractorTests
{
    [Fact]
    public async Task StoryExtractor_ExtractFromFile_WithNonExistentFile_ShouldReturnError()
    {
        // Arrange
        var extractor = new StoryExtractor();
        var options = new ExtractionOptions();
        var nonExistentPath = "/nonexistent/file.assets";

        // Act
        var result = await extractor.ExtractFromFileAsync(nonExistentPath, options);

        // Assert
        result.Success.Should().BeTrue(); // 処理自体は成功
        result.Errors.Should().NotBeEmpty(); // ただしエラーが記録される
    }

    [Fact]
    public async Task StoryExtractor_ExtractFromDirectory_WithNonExistentDirectory_ShouldReturnError()
    {
        // Arrange
        var extractor = new StoryExtractor();
        var options = new ExtractionOptions();
        var nonExistentPath = "/nonexistent/directory";

        // Act
        var result = await extractor.ExtractFromDirectoryAsync(nonExistentPath, options);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StoryExtractor_ExtractFromDirectory_WithCancellation_ShouldBeCancelled()
    {
        // Arrange
        var extractor = new StoryExtractor();
        var options = new ExtractionOptions();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await extractor.ExtractFromDirectoryAsync(tempDir, options, cancellationToken: cts.Token);

            // Assert
            result.Warnings.Should().Contain(w => w.Contains("キャンセル"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExtractionOptions_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new ExtractionOptions();

        // Assert
        options.ExtractTextAssets.Should().BeTrue();
        options.ExtractMonoBehaviours.Should().BeTrue();
        options.ExtractAssemblyStrings.Should().BeTrue();
        options.ExtractBinaryText.Should().BeTrue();
        options.ProcessResSFiles.Should().BeTrue();
        options.AttemptDecryption.Should().BeTrue();
        options.UseParallelProcessing.Should().BeTrue();
        options.UseStreamingLoad.Should().BeTrue();
        options.PrioritizeJapaneseText.Should().BeTrue();
        options.MinTextLength.Should().Be(1);
        options.OutputFormat.Should().Be(OutputFormat.Json);
    }

    [Fact]
    public void ExtractionResult_DurationMs_ShouldCalculateCorrectly()
    {
        // Arrange
        var result = new ExtractionResult
        {
            StartTime = new DateTime(2024, 1, 1, 12, 0, 0),
            EndTime = new DateTime(2024, 1, 1, 12, 0, 5) // 5 seconds later
        };

        // Act & Assert
        result.DurationMs.Should().Be(5000);
    }

    [Fact]
    public void ExtractionStatistics_InitialValues_ShouldBeZero()
    {
        // Arrange & Act
        var stats = new ExtractionStatistics();

        // Assert
        stats.TextAssetCount.Should().Be(0);
        stats.MonoBehaviourCount.Should().Be(0);
        stats.AssemblyStringCount.Should().Be(0);
        stats.BinaryTextCount.Should().Be(0);
        stats.EncryptedCount.Should().Be(0);
        stats.DecryptedCount.Should().Be(0);
        stats.TotalBytes.Should().Be(0);
    }
}
