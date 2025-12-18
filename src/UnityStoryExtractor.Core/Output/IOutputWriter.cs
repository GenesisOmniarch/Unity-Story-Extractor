using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Output;

/// <summary>
/// 出力ライターのインターフェース
/// </summary>
public interface IOutputWriter
{
    /// <summary>
    /// 対応する出力形式
    /// </summary>
    OutputFormat Format { get; }

    /// <summary>
    /// 抽出結果をファイルに書き込み
    /// </summary>
    Task WriteAsync(ExtractionResult result, string outputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 抽出結果を文字列に変換
    /// </summary>
    Task<string> ToStringAsync(ExtractionResult result, CancellationToken cancellationToken = default);
}
