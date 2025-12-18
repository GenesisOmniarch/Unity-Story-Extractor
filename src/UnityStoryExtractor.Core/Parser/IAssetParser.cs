using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Parser;

/// <summary>
/// アセットパーサーのインターフェース
/// </summary>
public interface IAssetParser
{
    /// <summary>
    /// アセットファイルを解析
    /// </summary>
    Task<ParseResult> ParseAsync(string filePath, ExtractionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// バイナリデータを解析
    /// </summary>
    Task<ParseResult> ParseBinaryAsync(byte[] data, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// ストリームから解析（大容量ファイル用）
    /// </summary>
    Task<ParseResult> ParseStreamAsync(Stream stream, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// このパーサーがサポートするファイルタイプ
    /// </summary>
    IEnumerable<FileNodeType> SupportedTypes { get; }
}

/// <summary>
/// 解析結果
/// </summary>
public class ParseResult
{
    public bool Success { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public List<ParsedAsset> Assets { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 解析されたアセット
/// </summary>
public class ParsedAsset
{
    public long PathId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public byte[]? RawData { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
    public List<string> TextContent { get; set; } = new();
    public bool IsEncrypted { get; set; }
    public EncryptionType DetectedEncryption { get; set; }
}
