using FluentAssertions;
using UnityStoryExtractor.Core.Loader;
using UnityStoryExtractor.Core.Models;
using Xunit;

namespace UnityStoryExtractor.Tests.Unit;

/// <summary>
/// ローダーのユニットテスト
/// </summary>
public class LoaderTests
{
    private readonly UnityAssetLoader _loader;

    public LoaderTests()
    {
        _loader = new UnityAssetLoader();
    }

    [Theory]
    [InlineData("test.assets", true)]
    [InlineData("sharedassets0.assets", true)]
    [InlineData("resources.assets", true)]
    [InlineData("test.resS", true)]
    [InlineData("test.ress", true)]
    [InlineData("Assembly-CSharp.dll", true)]
    [InlineData("globalgamemanagers", true)]
    [InlineData("level0", true)]
    [InlineData("test.bundle", true)]
    [InlineData("test.unity3d", true)]
    [InlineData("test.ab", true)]
    [InlineData("test.png", false)]
    [InlineData("test.txt", false)]
    [InlineData("test.exe", false)]
    public void IsSupportedFile_ShouldReturnCorrectResult(string fileName, bool expected)
    {
        // Act
        var result = _loader.IsSupportedFile(fileName);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithNonExistentDirectory_ShouldThrowException()
    {
        // Arrange
        var nonExistentPath = "/nonexistent/path/that/does/not/exist";

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _loader.ScanDirectoryAsync(nonExistentPath));
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithCancellation_ShouldThrowOperationCancelledException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // テストファイルを作成
            File.WriteAllText(Path.Combine(tempDir, "test.assets"), "test");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => _loader.ScanDirectoryAsync(tempDir, cancellationToken: cts.Token));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinkResSFiles_ShouldLinkCorrectly()
    {
        // Arrange
        var root = new FileTreeNode
        {
            Name = "Data",
            IsDirectory = true,
            Children = new List<FileTreeNode>
            {
                new FileTreeNode
                {
                    Name = "sharedassets0.assets",
                    FullPath = "/Data/sharedassets0.assets",
                    NodeType = FileNodeType.AssetsFile
                },
                new FileTreeNode
                {
                    Name = "sharedassets0.resS",
                    FullPath = "/Data/sharedassets0.resS",
                    NodeType = FileNodeType.ResSFile
                }
            }
        };

        // Act
        _loader.LinkResSFiles(root);

        // Assert
        var assetsNode = root.Children.First(c => c.NodeType == FileNodeType.AssetsFile);
        assetsNode.AssociatedResS.Should().NotBeNull();
        assetsNode.AssociatedResS!.Name.Should().Be("sharedassets0.resS");
    }
}
