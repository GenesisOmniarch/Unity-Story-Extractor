using System.Text;
using FluentAssertions;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Parser;
using Xunit;

namespace UnityStoryExtractor.Tests.Unit;

/// <summary>
/// パーサーのユニットテスト
/// </summary>
public class ParserTests
{
    [Fact]
    public async Task TextAssetParser_ShouldExtractUtf8Text()
    {
        // Arrange
        var parser = new TextAssetParser();
        var options = new ExtractionOptions { MinTextLength = 2 };
        var testText = "This is a test dialogue for the game.";
        var data = Encoding.UTF8.GetBytes(testText);

        // Act
        var result = await parser.ParseBinaryAsync(data, "test.assets", options);

        // Assert
        result.Success.Should().BeTrue();
        result.Assets.Should().NotBeEmpty();
        result.Assets.Any(a => a.TextContent.Any(t => t.Contains("test dialogue"))).Should().BeTrue();
    }

    [Fact]
    public async Task TextAssetParser_ShouldExtractJapaneseText()
    {
        // Arrange
        var parser = new TextAssetParser();
        var options = new ExtractionOptions 
        { 
            MinTextLength = 2,
            PrioritizeJapaneseText = true
        };
        var testText = "これはテストです。日本語のダイアログ。";
        var data = Encoding.UTF8.GetBytes(testText);

        // Act
        var result = await parser.ParseBinaryAsync(data, "test.assets", options);

        // Assert
        result.Success.Should().BeTrue();
        result.Assets.Should().NotBeEmpty();
        result.Assets.Any(a => a.TextContent.Any(t => t.Contains("日本語"))).Should().BeTrue();
    }

    [Fact]
    public async Task TextAssetParser_WithKeywordFilter_ShouldFilterResults()
    {
        // Arrange
        var parser = new TextAssetParser();
        var options = new ExtractionOptions 
        { 
            MinTextLength = 2,
            Keywords = new List<string> { "dialogue" }
        };
        var testText = "This is a dialogue.\nThis is not related.";
        var data = Encoding.UTF8.GetBytes(testText);

        // Act
        var result = await parser.ParseBinaryAsync(data, "test.assets", options);

        // Assert
        result.Success.Should().BeTrue();
        // キーワード "dialogue" を含むテキストのみが抽出されるべき
        foreach (var asset in result.Assets)
        {
            foreach (var text in asset.TextContent)
            {
                text.ToLowerInvariant().Should().Contain("dialogue");
            }
        }
    }

    [Fact]
    public async Task TextAssetParser_WithMinLength_ShouldFilterShortTexts()
    {
        // Arrange
        var parser = new TextAssetParser();
        var options = new ExtractionOptions { MinTextLength = 10 };
        var testText = "Short\nThis is a longer text that should be extracted.";
        var data = Encoding.UTF8.GetBytes(testText);

        // Act
        var result = await parser.ParseBinaryAsync(data, "test.assets", options);

        // Assert
        result.Success.Should().BeTrue();
        foreach (var asset in result.Assets)
        {
            foreach (var text in asset.TextContent)
            {
                text.Length.Should().BeGreaterOrEqualTo(10);
            }
        }
    }

    [Fact]
    public async Task MonoBehaviourParser_ShouldHandleEmptyData()
    {
        // Arrange
        var parser = new MonoBehaviourParser();
        var options = new ExtractionOptions();
        var data = Array.Empty<byte>();

        // Act
        var result = await parser.ParseBinaryAsync(data, "test.assets", options);

        // Assert
        result.Success.Should().BeTrue();
        result.Assets.Should().BeEmpty();
    }
}
