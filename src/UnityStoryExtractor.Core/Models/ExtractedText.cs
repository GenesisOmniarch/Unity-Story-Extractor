using System.Text.Json.Serialization;

namespace UnityStoryExtractor.Core.Models;

/// <summary>
/// 抽出されたテキストデータを表すクラス
/// </summary>
public class ExtractedText
{
    /// <summary>
    /// ソースアセット名
    /// </summary>
    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = string.Empty;

    /// <summary>
    /// ソースファイルパス
    /// </summary>
    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// アセットタイプ
    /// </summary>
    [JsonPropertyName("assetType")]
    public string AssetType { get; set; } = string.Empty;

    /// <summary>
    /// 抽出されたコンテンツ
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 抽出元（TextAsset, MonoBehaviour, Assembly, Binary など）
    /// </summary>
    [JsonPropertyName("extractionSource")]
    public ExtractionSource Source { get; set; }

    /// <summary>
    /// 抽出日時
    /// </summary>
    [JsonPropertyName("extractedAt")]
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// テキストのエンコーディング
    /// </summary>
    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "UTF-8";

    /// <summary>
    /// メタデータ（追加情報）
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 子テキスト（構造化データの場合）
    /// </summary>
    [JsonPropertyName("children")]
    public List<ExtractedText>? Children { get; set; }
}

/// <summary>
/// 抽出ソースの列挙
/// </summary>
public enum ExtractionSource
{
    TextAsset,
    MonoBehaviour,
    ScriptableObject,
    Assembly,
    IL2CPP,
    Binary,
    ResS,
    Unknown
}
