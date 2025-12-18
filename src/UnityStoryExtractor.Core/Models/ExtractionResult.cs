using System.Text.Json.Serialization;

namespace UnityStoryExtractor.Core.Models;

/// <summary>
/// 抽出結果全体を表すクラス
/// </summary>
public class ExtractionResult
{
    /// <summary>
    /// 抽出が成功したかどうか
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// ソースパス
    /// </summary>
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 検出されたUnityバージョン
    /// </summary>
    [JsonPropertyName("unityVersion")]
    public string UnityVersion { get; set; } = string.Empty;

    /// <summary>
    /// 抽出されたテキストのリスト
    /// </summary>
    [JsonPropertyName("extractedTexts")]
    public List<ExtractedText> ExtractedTexts { get; set; } = new();

    /// <summary>
    /// 処理されたファイル数
    /// </summary>
    [JsonPropertyName("processedFiles")]
    public int ProcessedFiles { get; set; }

    /// <summary>
    /// 合計抽出アイテム数
    /// </summary>
    [JsonPropertyName("totalExtracted")]
    public int TotalExtracted { get; set; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    [JsonPropertyName("errors")]
    public List<ExtractionError> Errors { get; set; } = new();

    /// <summary>
    /// 警告メッセージ
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// 抽出開始時刻
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 抽出終了時刻
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs => (long)(EndTime - StartTime).TotalMilliseconds;

    /// <summary>
    /// 統計情報
    /// </summary>
    [JsonPropertyName("statistics")]
    public ExtractionStatistics Statistics { get; set; } = new();
}

/// <summary>
/// 抽出エラー情報
/// </summary>
public class ExtractionError
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("exception")]
    public string? Exception { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 抽出統計情報
/// </summary>
public class ExtractionStatistics
{
    [JsonPropertyName("textAssetCount")]
    public int TextAssetCount { get; set; }

    [JsonPropertyName("monoBehaviourCount")]
    public int MonoBehaviourCount { get; set; }

    [JsonPropertyName("assemblyStringCount")]
    public int AssemblyStringCount { get; set; }

    [JsonPropertyName("binaryTextCount")]
    public int BinaryTextCount { get; set; }

    [JsonPropertyName("encryptedCount")]
    public int EncryptedCount { get; set; }

    [JsonPropertyName("decryptedCount")]
    public int DecryptedCount { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("resSFilesProcessed")]
    public int ResSFilesProcessed { get; set; }
}
