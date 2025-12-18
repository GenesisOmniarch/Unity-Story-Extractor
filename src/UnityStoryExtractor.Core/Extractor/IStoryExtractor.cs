using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Extractor;

/// <summary>
/// ストーリー抽出器のインターフェース
/// </summary>
public interface IStoryExtractor
{
    /// <summary>
    /// ディレクトリからストーリーデータを抽出
    /// </summary>
    Task<ExtractionResult> ExtractFromDirectoryAsync(
        string directoryPath, 
        ExtractionOptions options, 
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 単一ファイルからストーリーデータを抽出
    /// </summary>
    Task<ExtractionResult> ExtractFromFileAsync(
        string filePath, 
        ExtractionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ファイルツリーノードから抽出
    /// </summary>
    Task<ExtractionResult> ExtractFromNodeAsync(
        FileTreeNode node, 
        ExtractionOptions options,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 抽出進捗情報
/// </summary>
public class ExtractionProgress
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public string CurrentOperation { get; set; } = string.Empty;
    public int ExtractedCount { get; set; }
    public double Percentage => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
}
