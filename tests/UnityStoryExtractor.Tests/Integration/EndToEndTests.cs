using System.Text;
using FluentAssertions;
using UnityStoryExtractor.Core.Extractor;
using UnityStoryExtractor.Core.Loader;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Output;
using Xunit;

namespace UnityStoryExtractor.Tests.Integration;

/// <summary>
/// エンドツーエンドテスト
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly string _testDir;
    private readonly IAssetLoader _loader;
    private readonly IStoryExtractor _extractor;

    public EndToEndTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"UnityStoryExtractorTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        
        _loader = new UnityAssetLoader();
        _extractor = new StoryExtractor();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { /* Ignore cleanup errors */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task FullWorkflow_ScanAndExtract_ShouldWork()
    {
        // Arrange - テストファイルを作成
        var assetsPath = Path.Combine(_testDir, "test.assets");
        var testContent = "This is a test dialogue for the story.\nAnother line of dialogue.";
        await File.WriteAllTextAsync(assetsPath, testContent);

        // Act - スキャン
        var rootNode = await _loader.ScanDirectoryAsync(_testDir);

        // Assert - スキャン結果
        rootNode.Should().NotBeNull();
        rootNode.Children.Should().ContainSingle(c => c.Name == "test.assets");

        // Act - 抽出
        var options = new ExtractionOptions
        {
            MinTextLength = 5,
            Keywords = new List<string> { "dialogue" }
        };
        var result = await _extractor.ExtractFromDirectoryAsync(_testDir, options);

        // Assert - 抽出結果
        result.Success.Should().BeTrue();
        result.ProcessedFiles.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FullWorkflow_WithJapaneseText_ShouldExtract()
    {
        // Arrange
        var assetsPath = Path.Combine(_testDir, "dialogue.assets");
        var testContent = "ここに日本語のダイアログがあります。\nキャラクター: こんにちは、お元気ですか？";
        await File.WriteAllTextAsync(assetsPath, testContent, Encoding.UTF8);

        // Act
        var options = new ExtractionOptions
        {
            PrioritizeJapaneseText = true,
            MinTextLength = 5
        };
        var result = await _extractor.ExtractFromDirectoryAsync(_testDir, options);

        // Assert
        result.Success.Should().BeTrue();
        result.ExtractedTexts.Any(t => t.Content.Contains("日本語")).Should().BeTrue();
    }

    [Fact]
    public async Task FullWorkflow_OutputToJson_ShouldCreateValidFile()
    {
        // Arrange
        var assetsPath = Path.Combine(_testDir, "story.assets");
        await File.WriteAllTextAsync(assetsPath, "Story content for testing.");
        
        var outputPath = Path.Combine(_testDir, "output.json");
        var options = new ExtractionOptions { MinTextLength = 5 };

        // Act
        var result = await _extractor.ExtractFromDirectoryAsync(_testDir, options);
        var writer = OutputWriterFactory.Create(OutputFormat.Json);
        await writer.WriteAsync(result, outputPath);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(outputPath);
        json.Should().Contain("\"success\"");
        json.Should().Contain("\"extractedTexts\"");
    }

    [Fact]
    public async Task FullWorkflow_WithMultipleFiles_ShouldProcessAll()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var path = Path.Combine(_testDir, $"assets_{i}.assets");
            await File.WriteAllTextAsync(path, $"Content for file {i}. This is dialogue number {i}.");
        }

        // Act
        var options = new ExtractionOptions();
        var result = await _extractor.ExtractFromDirectoryAsync(_testDir, options);

        // Assert
        result.ProcessedFiles.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task FullWorkflow_WithNestedDirectories_ShouldScanRecursively()
    {
        // Arrange
        var subDir = Path.Combine(_testDir, "SubFolder", "DeepFolder");
        Directory.CreateDirectory(subDir);
        
        await File.WriteAllTextAsync(Path.Combine(_testDir, "root.assets"), "Root content");
        await File.WriteAllTextAsync(Path.Combine(subDir, "deep.assets"), "Deep content");

        // Act
        var rootNode = await _loader.ScanDirectoryAsync(_testDir);

        // Assert
        rootNode.Children.Should().Contain(c => c.Name == "root.assets");
        
        var subFolder = rootNode.Children.FirstOrDefault(c => c.Name == "SubFolder");
        subFolder.Should().NotBeNull();
        subFolder!.Children.Should().Contain(c => c.Name == "DeepFolder");
    }

    [Fact]
    public async Task FullWorkflow_WithResSFile_ShouldLinkCorrectly()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testDir, "sharedassets0.assets"), "Assets content");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "sharedassets0.resS"), "ResS content");

        // Act
        var rootNode = await _loader.ScanDirectoryAsync(_testDir);

        // Assert
        var assetsNode = rootNode.Children.FirstOrDefault(c => c.Name == "sharedassets0.assets");
        assetsNode.Should().NotBeNull();
        assetsNode!.AssociatedResS.Should().NotBeNull();
        assetsNode.AssociatedResS!.Name.Should().Be("sharedassets0.resS");
    }

    [Fact]
    public async Task FullWorkflow_ProgressReporting_ShouldWork()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_testDir, $"file_{i}.assets"), $"Content {i}");
        }

        var progressValues = new List<double>();
        var progress = new Progress<ExtractionProgress>(p => progressValues.Add(p.Percentage));

        // Act
        var options = new ExtractionOptions();
        await _extractor.ExtractFromDirectoryAsync(_testDir, options, progress);

        // Assert
        progressValues.Should().NotBeEmpty();
    }
}
