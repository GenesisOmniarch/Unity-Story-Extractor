using System.Text.Json;
using FluentAssertions;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Output;
using Xunit;

namespace UnityStoryExtractor.Tests.Unit;

/// <summary>
/// 出力ライターのユニットテスト
/// </summary>
public class OutputTests
{
    private ExtractionResult CreateSampleResult()
    {
        return new ExtractionResult
        {
            Success = true,
            SourcePath = "/path/to/game",
            UnityVersion = "2021.3.43f1",
            ProcessedFiles = 10,
            TotalExtracted = 5,
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            Statistics = new ExtractionStatistics
            {
                TextAssetCount = 3,
                MonoBehaviourCount = 2,
                TotalBytes = 1024
            },
            ExtractedTexts = new List<ExtractedText>
            {
                new ExtractedText
                {
                    AssetName = "Dialogue1",
                    AssetType = "TextAsset",
                    SourceFile = "/path/to/file.assets",
                    Content = "Hello, this is a test dialogue.",
                    Source = ExtractionSource.TextAsset
                },
                new ExtractedText
                {
                    AssetName = "Dialogue2",
                    AssetType = "TextAsset",
                    SourceFile = "/path/to/file.assets",
                    Content = "これは日本語のテストです。",
                    Source = ExtractionSource.TextAsset
                }
            }
        };
    }

    [Fact]
    public void OutputWriterFactory_Create_ShouldReturnCorrectType()
    {
        // Act & Assert
        OutputWriterFactory.Create(OutputFormat.Json).Should().BeOfType<JsonOutputWriter>();
        OutputWriterFactory.Create(OutputFormat.Text).Should().BeOfType<TextOutputWriter>();
        OutputWriterFactory.Create(OutputFormat.Csv).Should().BeOfType<CsvOutputWriter>();
        OutputWriterFactory.Create(OutputFormat.Xml).Should().BeOfType<XmlOutputWriter>();
    }

    [Fact]
    public async Task JsonOutputWriter_ToStringAsync_ShouldProduceValidJson()
    {
        // Arrange
        var writer = new JsonOutputWriter();
        var result = CreateSampleResult();

        // Act
        var json = await writer.ToStringAsync(result);

        // Assert
        json.Should().NotBeNullOrEmpty();
        
        // JSONとして解析可能か確認
        var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetProperty("sourcePath").GetString().Should().Be("/path/to/game");
        parsed.RootElement.GetProperty("unityVersion").GetString().Should().Be("2021.3.43f1");
    }

    [Fact]
    public async Task JsonOutputWriter_ToStringAsync_ShouldHandleJapaneseCharacters()
    {
        // Arrange
        var writer = new JsonOutputWriter();
        var result = CreateSampleResult();

        // Act
        var json = await writer.ToStringAsync(result);

        // Assert
        json.Should().Contain("日本語");
        json.Should().Contain("テスト");
    }

    [Fact]
    public async Task TextOutputWriter_ToStringAsync_ShouldContainAllInfo()
    {
        // Arrange
        var writer = new TextOutputWriter();
        var result = CreateSampleResult();

        // Act
        var text = await writer.ToStringAsync(result);

        // Assert
        text.Should().Contain("Unity Story Extractor");
        text.Should().Contain("/path/to/game");
        text.Should().Contain("2021.3.43f1");
        text.Should().Contain("TextAsset: 3");
        text.Should().Contain("日本語");
    }

    [Fact]
    public async Task CsvOutputWriter_ToStringAsync_ShouldContainHeader()
    {
        // Arrange
        var writer = new CsvOutputWriter();
        var result = CreateSampleResult();

        // Act
        var csv = await writer.ToStringAsync(result);

        // Assert
        var lines = csv.Split(Environment.NewLine);
        lines[0].Should().Contain("AssetName");
        lines[0].Should().Contain("AssetType");
        lines[0].Should().Contain("Content");
    }

    [Fact]
    public async Task CsvOutputWriter_ToStringAsync_ShouldEscapeQuotes()
    {
        // Arrange
        var writer = new CsvOutputWriter();
        var result = new ExtractionResult
        {
            ExtractedTexts = new List<ExtractedText>
            {
                new ExtractedText
                {
                    AssetName = "Test",
                    AssetType = "TextAsset",
                    SourceFile = "/path",
                    Content = "Text with \"quotes\" inside",
                    Source = ExtractionSource.TextAsset
                }
            }
        };

        // Act
        var csv = await writer.ToStringAsync(result);

        // Assert
        csv.Should().Contain("\"\"quotes\"\""); // CSV escaped quotes
    }

    [Fact]
    public async Task XmlOutputWriter_ToStringAsync_ShouldProduceValidXml()
    {
        // Arrange
        var writer = new XmlOutputWriter();
        var result = CreateSampleResult();

        // Act
        var xml = await writer.ToStringAsync(result);

        // Assert
        xml.Should().StartWith("<?xml");
        xml.Should().Contain("<ExtractionResult>");
        xml.Should().Contain("</ExtractionResult>");
        xml.Should().Contain("<SourcePath>");
        xml.Should().Contain("<UnityVersion>");
    }

    [Fact]
    public async Task OutputWriter_WriteAsync_ShouldCreateFile()
    {
        // Arrange
        var writer = new JsonOutputWriter();
        var result = CreateSampleResult();
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            await writer.WriteAsync(result, tempFile);

            // Assert
            File.Exists(tempFile).Should().BeTrue();
            var content = await File.ReadAllTextAsync(tempFile);
            content.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
