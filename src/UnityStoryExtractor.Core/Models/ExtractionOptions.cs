namespace UnityStoryExtractor.Core.Models;

/// <summary>
/// 抽出オプションを表すクラス
/// </summary>
public class ExtractionOptions
{
    /// <summary>
    /// TextAssetを抽出するかどうか
    /// </summary>
    public bool ExtractTextAssets { get; set; } = true;

    // 別名プロパティ（GUI互換用）
    public bool ExtractTextAsset { get => ExtractTextAssets; set => ExtractTextAssets = value; }

    /// <summary>
    /// MonoBehaviourを抽出するかどうか
    /// </summary>
    public bool ExtractMonoBehaviours { get; set; } = true;

    // 別名プロパティ（GUI互換用）
    public bool ExtractMonoBehaviour { get => ExtractMonoBehaviours; set => ExtractMonoBehaviours = value; }

    /// <summary>
    /// アセンブリから文字列を抽出するかどうか
    /// </summary>
    public bool ExtractAssemblyStrings { get; set; } = true;

    // 別名プロパティ（GUI互換用）
    public bool ExtractAssembly { get => ExtractAssemblyStrings; set => ExtractAssemblyStrings = value; }

    /// <summary>
    /// バイナリデータからテキストを抽出するかどうか
    /// </summary>
    public bool ExtractBinaryText { get; set; } = true;

    /// <summary>
    /// .resSファイルを処理するかどうか
    /// </summary>
    public bool ProcessResSFiles { get; set; } = true;

    /// <summary>
    /// 暗号化データの復号を試みるかどうか
    /// </summary>
    public bool AttemptDecryption { get; set; } = true;

    /// <summary>
    /// キーワードフィルタ（空の場合は全て抽出）
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// 最小テキスト長（これより短いテキストは無視）
    /// </summary>
    public int MinTextLength { get; set; } = 1;

    /// <summary>
    /// 最大テキスト長（これより長いテキストは分割）
    /// </summary>
    public int MaxTextLength { get; set; } = int.MaxValue;

    /// <summary>
    /// 出力形式
    /// </summary>
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Json;

    /// <summary>
    /// 並列処理を使用するかどうか
    /// </summary>
    public bool UseParallelProcessing { get; set; } = true;

    /// <summary>
    /// 最大並列度
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// ストリーミング読み込みを使用するかどうか（大容量ファイル用）
    /// </summary>
    public bool UseStreamingLoad { get; set; } = true;

    /// <summary>
    /// ストリーミングのチャンクサイズ（バイト）
    /// </summary>
    public int StreamingChunkSize { get; set; } = 64 * 1024 * 1024; // 64MB

    /// <summary>
    /// 日本語テキストを優先するかどうか
    /// </summary>
    public bool PrioritizeJapaneseText { get; set; } = true;

    /// <summary>
    /// カスタム復号キー
    /// </summary>
    public byte[]? DecryptionKey { get; set; }

    /// <summary>
    /// カスタム復号アルゴリズム名
    /// </summary>
    public string? DecryptionAlgorithm { get; set; }

    /// <summary>
    /// プラグインディレクトリ
    /// </summary>
    public string? PluginDirectory { get; set; }

    /// <summary>
    /// 除外パターン
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new()
    {
        "*.png", "*.jpg", "*.jpeg", "*.wav", "*.mp3", "*.ogg",
        "*.fbx", "*.obj", "*.shader", "*.mat"
    };

    /// <summary>
    /// 対象Unityバージョン（空の場合は全バージョン）
    /// </summary>
    public string? TargetUnityVersion { get; set; }

    /// <summary>
    /// ログ出力を有効にするかどうか
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// 詳細ログを有効にするかどうか
    /// </summary>
    public bool VerboseLogging { get; set; } = false;
}

/// <summary>
/// 出力形式の列挙
/// </summary>
public enum OutputFormat
{
    Json,
    Text,
    Csv,
    Xml
}
